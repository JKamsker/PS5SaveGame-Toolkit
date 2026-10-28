using SaveToolkit.Sdk;
using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;
using SaveToolkit.Sdk.Collections;

namespace SaveToolkit.Plugins.AtomicHeart;

internal sealed class AtomicHeartDocument : BoundSaveDocument, IQueryableDocument
{
    internal const int PageSize = 99;
    private readonly string path;
    private readonly byte[] bytes;
    private readonly SaveExtensionFile parsed;
    private readonly RecordIndex index;
    private readonly IReferenceContext references;
    private readonly RecordViews records;
    private readonly InventoryViews inventory;
    private readonly RecordCollection collection;

    internal AtomicHeartDocument(string path, byte[] bytes, SaveExtensionFile parsed, RecordIndex index, IReferenceContext references)
    {
        this.path = path;
        this.bytes = bytes;
        this.parsed = parsed;
        this.index = index;
        this.references = references;
        records = new RecordViews(index, BindReadOnly);
        inventory = new InventoryViews(index, references.Catalog(InventoryViews.CatalogId), BindReadOnly);
        collection = new RecordCollection(index);
    }

    public override string Title => "Atomic Heart slot";
    public override bool CanEdit => false;
    public IReadOnlyList<QueryableCollection> Collections => [collection.Info];
    public QueryableCollectionPage Query(string collectionId, QueryableCollectionQuery query) => collection.Query(collectionId, query);

    public override ViewNode BuildView() => new TabsNode([
        new Tab("Overview", [
            new StatsNode([
                new Stat("Engine", parsed.Engine), new Stat("Package version", parsed.PackageVersion.ToString()),
                new Stat("Custom versions", parsed.CustomVersions.ToString()), new Stat("SHA-1", "Verified"),
                new Stat("Compression", "Kraken · managed decoder"), new Stat("Chunks", parsed.Chunks.Count.ToString()),
                new Stat("Decoded bytes", index.Plain.Length.ToString()), new Stat("Levels", index.Levels.Count.ToString()),
                new Stat("Objects", index.Objects.Count.ToString()),
            ]),
            new TextNode("Read-only. Chunk sizes, decoded array length and record boundaries are validated. Native payloads and unknown bytes are retained. Names and serialized amounts do not prove gameplay effects or safe edit limits. Console validation and an independent decoder comparison remain pending.") { Tone = Tone.Warning },
        ]),
        new Tab("Inventory inspection", inventory.BuildView()),
        new Tab("Slot metadata", [SectionNode.Lazy("Tagged properties", "metadata/0")]),
        new Tab("Record explorer", [collection.View, .. records.BuildView()]),
        new Tab("Raw data", [SectionNode.Lazy("Compression chunks", "chunks/0"),
            new HexNode("data", parsed.DataLength) { Title = "Original compressed SlotData" },
            new HexNode("plain", index.Plain.Length) { Title = "Decoded SlotData (offsets in the record explorer)" }]),
    ]);

    public override IReadOnlyList<ViewNode> ExpandNode(string lazyId)
    {
        if (lazyId.StartsWith("inspect/", StringComparison.Ordinal)) return records.Expand(lazyId);
        if (lazyId.StartsWith("inventory/", StringComparison.Ordinal)) return inventory.Expand(lazyId);
        var parts = lazyId.Split('/');
        if (parts.Length != 2 || !int.TryParse(parts[1], out var start) || start < 0) throw new KeyNotFoundException(lazyId);
        if (parts[0] == "metadata" && (start == 0 || start < parsed.Properties.Count))
        {
            var nodes = parsed.Properties.Skip(start).Take(PageSize).Select((p, i) => Metadata(p, start + i)).ToList();
            if (start + PageSize < parsed.Properties.Count) nodes.Add(SectionNode.Lazy("More metadata", $"metadata/{start + PageSize}"));
            return nodes;
        }
        if (parts[0] == "chunks" && start < parsed.Chunks.Count)
        {
            var rows = parsed.Chunks.Skip(start).Take(PageSize).Select((c, i) => new TableRow((start + i).ToString(),
                new Dictionary<string, ViewNode> { ["offset"] = new TextNode($"0x{c.Offset:X}"),
                    ["compressed"] = new TextNode(c.CompressedBytes.ToString()), ["plain"] = new TextNode(c.UncompressedBytes.ToString()) })).ToArray();
            var nodes = new List<ViewNode> { new TableNode([new("offset", "File offset"), new("compressed", "Compressed bytes"), new("plain", "Validated plain bytes")], rows) };
            if (start + PageSize < parsed.Chunks.Count) nodes.Add(SectionNode.Lazy("More chunks", $"chunks/{start + PageSize}"));
            return nodes;
        }
        throw new KeyNotFoundException(lazyId);
    }

    private ViewNode Metadata(SlotProperty property, int ordinal)
    {
        if (property.Value is not { } value)
            return new SectionNode($"{property.Name} · {property.StructType ?? property.Type}", [new HexNode($"property/{ordinal}", property.Length)]);
        FieldSpec spec = value.Kind == FieldValueKind.Text ? new TextSpec() : IntegerSpec.Int64;
        return BindReadOnly($"metadata/{Uri.EscapeDataString(property.Name)}/{ordinal}", property.Name, spec, () => value) with
        {
            Confidence = Confidence.Verified,
            Description = $"Serialized {property.StructType ?? property.Type}" + (property.StructType is "DateTime" or "Timespan" ? "100 ns ticks" : ""),
            Warning = "Storage type and serialized name are verified; game-specific effects are unverified.",
        };
    }

    private FieldNode BindReadOnly(string id, string label, FieldSpec spec, Func<FieldValue> get) => Field(id, label, spec, get);

    public override AssetContent? GetAsset(string assetId) => references.GetAsset(assetId);

    public override ReadOnlyMemory<byte> ReadBlob(string blobId, long offset, int count)
    {
        var source = bytes;
        var start = parsed.DataOffset;
        var length = parsed.DataLength;
        if (blobId == "plain") { source = index.Plain; start = 0; length = source.Length; }
        else if (blobId.StartsWith("plain/", StringComparison.Ordinal))
        {
            source = index.Plain;
            var parts = blobId.Split('/');
            if (parts.Length != 3 || !int.TryParse(parts[1], out start) || !int.TryParse(parts[2], out length) ||
                start < 0 || length < 0 || start > source.Length - length) throw new KeyNotFoundException(blobId);
        }
        else if (blobId != "data")
        {
            var parts = blobId.Split('/');
            if (parts.Length != 2 || parts[0] != "property" || !int.TryParse(parts[1], out var ordinal) || ordinal < 0 || ordinal >= parsed.Properties.Count)
                throw new KeyNotFoundException(blobId);
            start = parsed.Properties[ordinal].Offset;
            length = parsed.Properties[ordinal].Length;
        }
        if (offset < 0 || offset > length || count < 0) throw new ArgumentOutOfRangeException(nameof(offset));
        return source.AsMemory(start + (int)offset, Math.Min(count, length - (int)offset));
    }

    public override IReadOnlyList<FileOutput> Serialize()
    {
        var validated = SaveExtensionFile.Parse(bytes);
        var reopened = SlotDataDecoder.Decode(bytes, validated);
        if (!reopened.Plain.AsSpan().SequenceEqual(index.Plain)) throw new SaveFormatException("Decoded no-op changed unexpectedly.");
        return [new FileOutput(path, bytes.ToArray())];
    }
}
