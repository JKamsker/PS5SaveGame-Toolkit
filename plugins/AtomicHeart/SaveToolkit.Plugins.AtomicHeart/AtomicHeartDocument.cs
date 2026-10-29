using SaveToolkit.Sdk;
using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;
using SaveToolkit.Sdk.Collections;

namespace SaveToolkit.Plugins.AtomicHeart;

internal sealed class AtomicHeartDocument : BoundSaveDocument, IQueryableDocument, IActionPlanningDocument
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
    private readonly InventoryState state;
    private readonly InventoryCollection inventoryCollection;
    private readonly SpatialViews spatial;
    private readonly SlotSerialization serialization;
    private readonly ReferencePresentation referencePresentation;

    internal AtomicHeartDocument(string path, byte[] bytes, SaveExtensionFile parsed, RecordIndex index, IReferenceContext references)
    {
        this.path = path;
        this.bytes = bytes;
        this.parsed = parsed;
        this.index = index;
        this.references = references;
        referencePresentation = new ReferencePresentation(references);
        state = new InventoryState(index, references.Catalog(InventoryViews.CatalogId));
        records = new RecordViews(index, BindReadOnly, state.Current, state.Catalog);
        inventory = new InventoryViews(state, BindInventory);
        inventoryCollection = new InventoryCollection(state, inventory);
        spatial = new SpatialViews(index, state, BindReadOnly);
        serialization = new SlotSerialization(bytes, parsed, index);
        // Register eligible fields even when a whole-document action precedes inventory queries.
        inventory.RegisterBindings();
        collection = new RecordCollection(index);
    }

    public override string Title => "Atomic Heart slot";
    public override bool CanEdit => state.Items.Any(state.CanSetAmount);
    public IReadOnlyList<QueryableCollection> Collections => [collection.Info, inventoryCollection.Info];
    public QueryableCollectionPage Query(string collectionId, QueryableCollectionQuery query) => collectionId == InventoryCollection.Id
        ? inventoryCollection.Query(query) : collection.Query(collectionId, query);

    public override ViewNode BuildView() => new TabsNode([
        new Tab("Overview", [
            new StatsNode([
                new Stat("Engine", parsed.Engine), new Stat("Package version", parsed.PackageVersion.ToString()),
                new Stat("Custom versions", parsed.CustomVersions.ToString()), new Stat("SHA-1", "Verified"),
                new Stat("Compression", "Kraken · managed decoder"), new Stat("Chunks", parsed.Chunks.Count.ToString()),
                new Stat("Decoded bytes", index.Plain.Length.ToString()), new Stat("Levels", index.Levels.Count.ToString()),
                new Stat("Objects", index.Objects.Count.ToString()),
            ]),
            new TextNode("Experimental existing inventory writes. Chunk sizes, decoded array length and record boundaries are validated; unknown bytes are retained. " + InventoryState.WriteWarning) { Tone = Tone.Warning },
        ]),
        new Tab("Inventory inspection", inventory.BuildView()),
        new Tab("Skills and world", spatial.BuildView()),
        new Tab("Reference guide", referencePresentation.BuildView()),
        new Tab("Slot metadata", [SectionNode.Lazy("Tagged properties", "metadata/0")]),
        new Tab("Record explorer", [collection.View, .. records.BuildView()]),
        new Tab("Raw data", [SectionNode.Lazy("Compression chunks", "chunks/0"),
            new HexNode("data", parsed.DataLength) { Title = "Original compressed SlotData" },
            new HexNode("plain", index.Plain.Length) { Title = "Original decoded SlotData (original offsets in the record explorer)" }]),
    ]);

    public override IReadOnlyList<ViewNode> ExpandNode(string lazyId)
    {
        if (lazyId.StartsWith("references/", StringComparison.Ordinal)) return referencePresentation.Expand(lazyId);
        if (lazyId.StartsWith("inspect/", StringComparison.Ordinal)) return records.Expand(lazyId);
        if (lazyId.StartsWith("spatial/", StringComparison.Ordinal)) return spatial.Expand(lazyId);
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
    private FieldNode BindInventory(string id, string label, FieldSpec spec, Func<FieldValue> get,
        Action<FieldValue>? set, Func<FieldValue, string?>? validate) => Field(id, label, spec, get, set, validate);

    public override EditOutcome ApplyEdits(IReadOnlyList<ValueEdit> edits)
    {
        var outcome = base.ApplyEdits(edits);
        return outcome.Succeeded ? outcome with { Warnings = [InventoryState.WriteWarning],
            StructureChanged = edits.Any(e => e.ValueId.EndsWith("/asset", StringComparison.Ordinal)) } : outcome;
    }

    public override ActionOutcome InvokeAction(string actionId, IReadOnlyDictionary<string, FieldValue> parameters) =>
        actionId == CatalogueInspection.ActionId
            ? CatalogueInspection.Inspect(state.Catalog, parameters)
            : base.InvokeAction(actionId, parameters);

    public ActionPlan PlanAction(string actionId, IReadOnlyDictionary<string, FieldValue> parameters, ActionScope scope) =>
        InventoryPlanning.Plan(state, actionId, parameters, scope);

    public EditOutcome ApplyActionPlan(string actionId, IReadOnlyDictionary<string, FieldValue> parameters, ActionScope scope, ActionPlan plan)
    {
        var fresh = PlanAction(actionId, parameters, scope);
        if (fresh.Errors.Count != 0) return new EditOutcome { Errors = fresh.Errors };
        if (!InventoryPlanning.Equivalent(fresh, plan)) return EditOutcome.Rejected(actionId, "Preview no longer matches this document.");
        return ApplyEdits(fresh.Changes.Select(c => new ValueEdit(c.ValueId, c.NewValue)).ToArray()) with { Warnings = fresh.Warnings };
    }

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
        return [new FileOutput(path, serialization.Serialize(state.Edits()))];
    }
}
