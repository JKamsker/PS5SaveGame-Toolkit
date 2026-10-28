using System.Globalization;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Saves;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.BaldursGate3;

internal sealed record DocumentEntry(string Name, byte[] Bytes, LarianResource? Resource);

internal sealed class BaldursGate3Document : BoundSaveDocument, IQueryableDocument, IActionPlanningDocument
{
    private readonly string _path;
    private readonly byte[] _original;
    private readonly LspkPackage? _package;
    private readonly IReadOnlyList<DocumentEntry> _entries;
    private readonly ResourceExplorer _explorer;
    private readonly IReferenceContext _referenceContext;
    private readonly Bg3References _references;
    private readonly LsmfInspection _lsmf;
    private readonly SavedItemInspection _items;
    private readonly ReferenceCatalogView _catalogs;
    private readonly StoryInspection _story;
    private readonly LsmfLiveWorkspace _live;
    private readonly LsmfLiveActions _actions;
    private readonly PartySummaryEdits? _summary;
    private readonly SavedQuantityEdits _quantities;
    private readonly Dictionary<string, byte[]> _edits = new(StringComparer.Ordinal);

    private BaldursGate3Document(ISaveFile file, byte[] bytes, LspkPackage? package, List<DocumentEntry> entries, IReferenceContext references)
    {
        _path = file.RelativePath;
        _original = bytes;
        _package = package;
        _entries = entries;
        _explorer = new ResourceExplorer(entries, Bind);
        _referenceContext = references;
        _references = new Bg3References(references);
        _lsmf = new LsmfInspection(entries, BindInspection);
        _quantities = new SavedQuantityEdits(entries);
        _items = new SavedItemInspection(entries, _references, BindInspection, BindQuantity, ReadQuantity);
        _catalogs = new ReferenceCatalogView(_references);
        _story = new StoryInspection(entries);
        _live = new LsmfLiveWorkspace(entries, BindLive, BindInspection, BindSlot);
        _actions = new LsmfLiveActions(_live, BindLive, ApplyEdits);
        _summary = PartySummaryEdits.Create(entries);
        CanEdit = (package is null || package.Version == 18) &&
            (entries.Any(e => e.Resource?.Attributes.Any(AttributeEncoder.CanWrite) == true) || _summary?.HasFields == true ||
                _live.Sources.Any(source => source.Numbers.Count > 0));
        Title = $"Baldur's Gate 3 · {file.FileName}";
    }

    public override string Title { get; }
    public override bool CanEdit { get; }

    internal static BaldursGate3Document Open(ISaveFile file, IReferenceContext references)
    {
        var bytes = file.ReadAllBytes();
        var budget = new ResourceBudget();
        LspkPackage? package = null;
        var entries = new List<DocumentEntry>();
        if (bytes.AsSpan().StartsWith("LSPK"u8))
        {
            package = LspkPackage.Read(bytes);
            foreach (var entry in package.Entries)
            {
                var content = package.Extract(entry);
                entries.Add(new DocumentEntry(entry.Name, content, Bg3Serialization.ReadResource(entry.Name, content, budget)));
            }
        }
        else
        {
            var resource = Bg3Serialization.ReadResource(file.FileName, bytes, budget) ?? throw new SaveFormatException("Expected an LSPK, LSOF or LSX file.");
            entries.Add(new DocumentEntry(file.FileName, bytes, resource));
        }

        return new BaldursGate3Document(file, bytes, package, entries, references);
    }

    public override ViewNode BuildView() => new TabsNode(
    [
        new Tab("Overview",
        [
            new StatsNode(
            [
                new Stat("Format", _package is null ? _entries[0].Resource!.Format : $"LSPK v{_package.Version}"),
                new Stat("Entries", _entries.Count.ToString()),
                new Stat("Resources", _entries.Count(e => e.Resource is not null).ToString()),
                new Stat("Original bytes", _original.Length.ToString("N0")),
            ]),
            new TextNode(CanEdit
                ? "Experimental primitive, party-summary and bounded live NewAge editing. Not validated on console; the game may reject edited saves. See Live NewAge for storage/identity warnings."
                : "Read-only resource. Writing is supported for LSF primitives and LSPK v18 only.") { Tone = Tone.Info },
            .. PackageMetadata(),
            .. _explorer.EntryList(_package),
        ]),
        new Tab("Party", PartyView.Build(_entries, Bind, BindSummary, _references)),
        new Tab("Inventory", _items.Build()),
        new Tab("Catalogs", _catalogs.Build()),
        new Tab("NewAge", _lsmf.Build()),
        new Tab("Live NewAge", _live.Build()),
        new Tab("Story", _story.Build()),
        new Tab("Resources", _explorer.ResourceList()),
        new Tab("Raw", [new HexNode("original", _original.Length) { Title = "Original file (unchanged)" }]),
    ]);

    public override IReadOnlyList<ViewNode> ExpandNode(string lazyId) => lazyId switch
    {
        _ when lazyId.StartsWith("live/", StringComparison.Ordinal) => _live.Expand(lazyId),
        _ when lazyId.StartsWith(LsmfInspection.Prefix, StringComparison.Ordinal) => _lsmf.Expand(lazyId),
        _ when lazyId.StartsWith(SavedItemInspection.Prefix, StringComparison.Ordinal) => _items.Expand(lazyId),
        _ when lazyId.StartsWith(StoryInspection.Prefix, StringComparison.Ordinal) => _story.Expand(lazyId),
        _ => _explorer.Expand(lazyId, _package),
    };

    public IReadOnlyList<QueryableCollection> Collections =>
        [.. _catalogs.Collections, _items.Collection, .. _live.Collections];

    public QueryableCollectionPage Query(string collectionId, QueryableCollectionQuery query) =>
        collectionId.StartsWith("live/", StringComparison.Ordinal) ? _live.Query(collectionId, query) :
        collectionId == _items.Collection.Id ? _items.Query(query) : _catalogs.Query(collectionId, query, BindInspection);

    public override IReadOnlyList<FileOutput> Serialize() =>
        Bg3Serialization.Write(_path, _original, _package, _entries, _edits, _summary, _live);

    public ActionPlan PlanAction(string actionId, IReadOnlyDictionary<string, FieldValue> parameters, ActionScope scope) =>
        _actions.Plan(actionId, parameters, scope);

    public EditOutcome ApplyActionPlan(string actionId, IReadOnlyDictionary<string, FieldValue> parameters, ActionScope scope, ActionPlan plan) =>
        CanEdit ? _actions.Apply(actionId, parameters, scope) : EditOutcome.Rejected(actionId, "This package version is read-only.");

    public override DocumentMemento RestoreMemento(DocumentMemento memento) => _actions.RestoreMemento(memento);

    private FieldNode BindLive(LsmfLiveSource source, LsmfNumber number) =>
        Field(source.Id(number), number.Label, new IntegerSpec(number.Min, number.Max), () => FieldValue.Of(source.Get(number)),
            CanEdit ? next => source.Set(number, next) : null) with
        {
            Confidence = Confidence.Unknown,
            Description = $"{number.Family} source row {number.Row}; fixed-width PS5 storage. Range {number.Min}–{number.Max}, bounded by imported storage values. " + LsmfLiveSource.Warning,
            Warning = LsmfLiveSource.Warning,
        };

    private FieldNode BindSlot(LsmfLiveSource source, LsmfSlot slot) =>
        Field(source.Prefix + "/" + slot.Key + "/key", "Container map key candidate", IntegerSpec.UInt16,
            () => FieldValue.Of(source.Slot(slot))) with
        {
            Confidence = Confidence.Unknown,
            Description = "Exact stored u16 map key; grid semantics have not been validated on console. " + LsmfLiveSource.Warning,
        };

    public override ReadOnlyMemory<byte> ReadBlob(string blobId, long offset, int count)
    {
        var bytes = blobId == "original" ? _original.AsMemory() : _explorer.Blob(blobId);
        if (offset < 0 || offset > bytes.Length || count < 0)
            throw new ArgumentOutOfRangeException(nameof(offset), "Invalid BG3 blob range.");
        return bytes.Slice((int)offset, Math.Min(count, bytes.Length - (int)offset));
    }

    public override AssetContent? GetAsset(string assetId)
    {
        if (!_explorer.TryEntryAsset(assetId, out var entry))
        {
            return _referenceContext.GetAsset(assetId);
        }

        return new AssetContent("image/webp", entry.Bytes.ToArray());
    }

    private FieldNode Bind(string id, string label, ResourceAttribute attribute)
    {
        var value = AttributeDecoder.Read(attribute);
        var editable = CanEdit && AttributeEncoder.CanWrite(attribute);
        var spec = AttributeDecoder.Spec(value, attribute.Type);
        var quantity = _quantities.Find(id);
        if (quantity is not null) spec = quantity.Spec;
        if (editable && spec is TextSpec text)
        {
            spec = text with { MaxLength = AttributeEncoder.MaxTextLength };
        }
        string? warning = null;
        if (editable)
            warning = quantity is null
                ? "Raw primitive: changing identifiers or inconsistent gameplay values may prevent loading. No in-game validation."
                : SavedQuantityEdits.Warning;
        if (attribute.Type is LarianAttributeType.Float or LarianAttributeType.Double && value.Kind == FieldValueKind.Text)
            warning = "Non-finite floating-point value; displayed as text and preserved unchanged.";

        return Field(id, label, spec,
            () => _edits.TryGetValue(id, out var data) ? AttributeDecoder.Read(attribute with { Data = data }) : value,
            editable ? next => SetValue(id, attribute, next) : null) with
        {
            Confidence = Confidence.Unknown,
            Description = $"Stored type: {attribute.Type}. Raw field meaning is not validated against game behavior. " +
                (quantity is null ? "Not validated on console." : SavedQuantityEdits.Warning),
            Warning = warning,
        };
    }

    private void SetValue(string id, ResourceAttribute attribute, FieldValue value)
    {
        if (_quantities.Find(id) is { } quantity)
            BinaryData.Require(value.AsInteger() >= 1 && value.AsInteger() <= quantity.Max,
                $"Quantity candidate must be between 1 and {quantity.Max}; zero/removal semantics are unknown.");
        var data = AttributeEncoder.Write(attribute, value);
        if (data.AsSpan().SequenceEqual(attribute.Data.Span))
        {
            _edits.Remove(id);
        }
        else
        {
            _edits[id] = data;
        }
    }

    private FieldNode? BindQuantity(int entry, int node) => _quantities.Find(entry, node) is { } quantity
        ? Bind(quantity.Id, "Explicit quantity candidate", quantity.Attribute) : null;

    private Int128? ReadQuantity(int entry, int node)
    {
        var quantity = _quantities.Find(entry, node);
        return quantity is null ? null : AttributeDecoder.Read(_edits.TryGetValue(quantity.Id, out var data)
            ? quantity.Attribute with { Data = data } : quantity.Attribute).AsInteger();
    }

    private FieldNode BindSummary(string id, string label, FieldValue value)
    {
        var number = CanEdit ? _summary?.Find(id) : null;
        FieldSpec spec = value.Kind == FieldValueKind.Integer ? IntegerSpec.Int64 : new TextSpec();
        if (number is not null) spec = new IntegerSpec(number.Min, number.Max);
        return Field(id, label, spec, () => number is null ? value : _summary!.Get(number),
            number is null ? null : next => _summary!.Set(number, next)) with
        {
            Confidence = Confidence.Inferred,
            Description = number is null ? "SaveInfo.json party summary; unsupported or ambiguous values remain read-only."
                : $"Existing SaveInfo.json metadata token. Based on the PS5 summary and Hawk-on FIELD_MAPPING.md. " +
                    $"Range {number.Min}–{number.Max}: {(number.Name == "Level" ? "base-game cap 12 (bg3.wiki contributors, CC BY-SA 4.0; https://bg3.wiki/wiki/Experience), extended only to observed levels" : "conservative observed summary XP range; gameplay limits/meaning are unknown")}. " +
                    PartySummaryEdits.Warning,
            Warning = number is null ? null : PartySummaryEdits.Warning,
        };
    }

    private FieldNode BindInspection(string id, string label, FieldValue value) =>
        Field(id, label, value.Kind == FieldValueKind.Integer
            ? value.AsInteger() > long.MaxValue ? IntegerSpec.UInt64 : IntegerSpec.Int64 : new TextSpec(), () => value) with
        {
            Confidence = Confidence.Inferred,
            Description = "Original saved structure only. Gameplay meaning and joins have not been established with controlled PS5 captures.",
        };

    private IEnumerable<ViewNode> PackageMetadata()
    {
        if (_package is not null)
        {
            yield return new TextNode($"Original header digest ({(_package.Version == 18 ? "verified" : "unverified legacy format")}): {_package.HeaderDigest}") { Monospace = true };
            yield return new TextNode($"Package flags: 0x{_package.Flags:X2}. All payloads decompressed and resource tables validated.");
        }
    }

}
