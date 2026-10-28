using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.AtomicHeart;

internal sealed class AtomicHeartDocument(string path, byte[] bytes, SaveExtensionFile parsed) : BoundSaveDocument
{
    private const int PageSize = 99; // Leave room for a continuation node within a 100-node expansion.
    public override string Title => "Atomic Heart slot";
    public override bool CanEdit => false;

    public override ViewNode BuildView() => new TabsNode([
        new Tab("Overview", [
            new StatsNode([
                new Stat("Engine", parsed.Engine), new Stat("Package version", parsed.PackageVersion.ToString()),
                new Stat("Custom versions", parsed.CustomVersions.ToString()), new Stat("SHA-1", "Verified"),
                new Stat("Compression", "Oodle · 8C 06"), new Stat("Chunks", parsed.Chunks.Count.ToString()),
                new Stat("Declared decompressed bytes", parsed.Chunks.Sum(c => (long)c.UncompressedBytes).ToString()),
            ]),
            new TextNode("Read-only. Slot metadata and file SHA-1 are validated. Compressed gameplay records remain opaque; Oodle decoding is unavailable.") { Tone = Tone.Warning },
        ]),
        new Tab("Slot metadata", [SectionNode.Lazy("Tagged properties", "metadata/0")]),
        new Tab("Gameplay records", [SectionNode.Lazy("Compression chunks", "chunks/0"),
            new HexNode("data", parsed.DataLength) { Title = "Original compressed SlotData" }]),
    ]);

    public override IReadOnlyList<ViewNode> ExpandNode(string lazyId)
    {
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
            var nodes = new List<ViewNode> { new TableNode([new("offset", "Offset"), new("compressed", "Compressed bytes"), new("plain", "Declared plain bytes")], rows) };
            if (start + PageSize < parsed.Chunks.Count) nodes.Add(SectionNode.Lazy("More chunks", $"chunks/{start + PageSize}"));
            return nodes;
        }
        throw new KeyNotFoundException(lazyId);
    }

    private ViewNode Metadata(SlotProperty property, int index)
    {
        if (property.Value is not { } value)
            return new SectionNode($"{property.Name} · {property.StructType ?? property.Type}", [new HexNode($"property/{index}", property.Length)]);
        FieldSpec spec = value.Kind == FieldValueKind.Text ? new TextSpec() : IntegerSpec.Int64;
        return Field($"value/{index}", property.Name, spec, () => value) with
        {
            Confidence = Confidence.Verified,
            Description = $"Serialized {property.StructType ?? property.Type}" + (property.StructType is "DateTime" or "Timespan" ? " · 100 ns ticks" : ""),
            Warning = "Storage type and serialized name are verified; game-specific effects are unverified.",
        };
    }

    public override ReadOnlyMemory<byte> ReadBlob(string blobId, long offset, int count)
    {
        var start = parsed.DataOffset;
        var length = parsed.DataLength;
        if (blobId != "data")
        {
            var parts = blobId.Split('/');
            if (parts.Length != 2 || parts[0] != "property" || !int.TryParse(parts[1], out var index) || index < 0 || index >= parsed.Properties.Count)
                throw new KeyNotFoundException(blobId);
            start = parsed.Properties[index].Offset;
            length = parsed.Properties[index].Length;
        }
        if (offset < 0 || offset > length || count < 0) throw new ArgumentOutOfRangeException(nameof(offset));
        return bytes.AsMemory(start + (int)offset, Math.Min(count, length - (int)offset));
    }

    public override IReadOnlyList<FileOutput> Serialize()
    {
        _ = SaveExtensionFile.Parse(bytes);
        return [new FileOutput(path, bytes.ToArray())];
    }
}
