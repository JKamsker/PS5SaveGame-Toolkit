using System.Buffers.Binary;
using System.Globalization;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.FinalFantasy7;

internal sealed class FinalFantasy7Document : BoundSaveDocument, IQueryableDocument, IActionPlanningDocument, IPresetDocument
{
    private const int RecordsPerSection = 32;
    private const int MaximumBlobPage = 64 * 1024;
    private const string IntegrityWarning = WriteWarnings.Experimental;
    private readonly ProgressionEdits _progression = new();
    private readonly ScalarBindings _bindings;
    private readonly string _path;
    private readonly byte[] _bytes;
    private readonly Ff7Schema _schema;
    private readonly bool _common;
    private readonly IReadOnlyList<LinkedRecord> _records;
    private readonly GameplayFields _gameplay;
    private readonly RecordExplorer _explorer;
    private readonly CharacterView _characters;
    private readonly OwnedRelationsView? _ownedRelations;
    private readonly ReferenceData _references;
    private readonly DocumentCollections? _collections;
    private readonly DocumentPlanning _planning;
    private readonly FinalFantasy7PresetDocument _presetDocument;
    private readonly RegionalMaps? _maps;
    private readonly FolioGraphs? _folios;
    private readonly SocketGraphs? _sockets;
    private readonly ProgressSummary? _progressSummary;
    private readonly CollectionQuery _recordCollection;
    private bool _spatialPublished;

    public FinalFantasy7Document(string path, string name, byte[] bytes, string titleId, Ff7Schema schema, bool common,
        IReadOnlyList<LinkedRecord> records, IReferenceContext? references = null)
    {
        _path = path;
        _bindings = new(bytes, Field);
        _bytes = bytes;
        _schema = schema;
        _common = common;
        _records = records;
        _recordCollection = RecordCollection.Create(records);
        _references = new(references ?? IReferenceContext.Empty, schema);
        _gameplay = new(bytes, schema, _references, _progression);
        _explorer = new(bytes, records, Bind);
        _characters = new(bytes, schema, _references, Bind, _progression);
        _planning = new(schema, _gameplay, _references, _characters);
        Presets = new(_planning, new(schema, _gameplay, _references, new(bytes, schema, _references)), common, titleId, schema.Variant);
        _presetDocument = new(Presets, _planning, _references, schema, titleId, common, _references.ContentIdentity, Bind, ApplyEdits);
        _ownedRelations = !common && !schema.IsRemake ? new(bytes, _references, Bind) : null;
        if (!common)
        {
            var progress = new ProgressData(bytes, schema, flag => Field($"progress/{flag.Id}/bit", "Stored bit",
                IntegerSpec.UInt8 with { Min = 0, Max = 1 }, () => FieldValue.Of((long)((_bytes[flag.Offset] >> flag.Bit) & 1)),
                value => _bytes[flag.Offset] = (byte)((_bytes[flag.Offset] & ~(1 << flag.Bit)) | (value.AsInt32() << flag.Bit)),
                value => value.AsInteger() < 0 || value.AsInteger() > 1 ? "Stored bit must be 0 or 1." : null) with
            {
                Confidence = Confidence.Inferred,
                Description = $"Absolute byte 0x{flag.Offset:X}, bit {flag.Bit}. {flag.Source}; {EmbeddedCatalogs.Attribution}. " +
                    WriteWarnings.Progress,
                Warning = WriteWarnings.Progress,
            });
            _collections = new(_gameplay, _references, Bind, progress);
            _collections.Add(new EnemyIntel(bytes, schema).Collection());
            _maps = new(progress, _references);
            if (!schema.IsRemake)
            {
                _folios = new(bytes, _references, Bind);
                _sockets = new(bytes, _references, Bind);
                _collections.Add(new ResearchProgress(bytes, Bind).Collection());
                _progressSummary = new(_references, Bind);
            }
            if (_ownedRelations is not null)
            {
                _collections.Add(_ownedRelations.Collection("weapon"));
                _collections.Add(_ownedRelations.Collection("materia"));
            }
        }
        Title = $"{schema.Name} · {name}";
    }

    public override string Title { get; }
    public override bool CanEdit => !_common;
    internal SemanticPresets Presets { get; }

    public PresetCapability PresetCapability => _presetDocument.PresetCapability;
    public PresetAction CapturePreset(PresetCaptureRequest request) => _presetDocument.CapturePreset(request);
    public SaveToolkit.Sdk.PresetResolution ResolvePresetInstances(IReadOnlyList<PresetInstanceReference> instances) =>
        _presetDocument.ResolvePresetInstances(instances);
    public SaveToolkit.Sdk.PresetResolution ResolvePresetInstances(IReadOnlyList<PresetInstanceReference> instances,
        IReadOnlyDictionary<string, string> choices) => _presetDocument.ResolvePresetInstances(instances, choices);
    public ActionPlan PlanPreset(IReadOnlyList<PresetAction> actions, SaveToolkit.Sdk.PresetResolution resolution) =>
        _presetDocument.PlanPreset(actions, resolution);
    public EditOutcome ApplyPreset(IReadOnlyList<PresetAction> actions, SaveToolkit.Sdk.PresetResolution resolution, ActionPlan plan) =>
        _presetDocument.ApplyPreset(actions, resolution, plan);

    public override ViewNode BuildView() => new TabsNode(
    [
        new Tab("Overview",
        [
            new StatsNode(
            [
                new("Schema", _schema.Name),
                new("Role", _common ? "Common/system" : "Gameplay"),
                new("Bytes", _bytes.Length.ToString("N0")),
                new("Records (including tail)", _records.Count.ToString()),
            ]),
            new TextNode(IntegrityWarning) { Tone = Tone.Warning },
            new TextNode("Record framing is verified against samples. Gameplay meanings use cited public schemas; demo mappings remain inferred. Remake, retail Rebirth and the demo expose experimental character, owned-weapon, quantity and materia AP/star edits.") { Tone = Tone.Info },
            new SectionNode("Gameplay", _common
                ? [new TextNode("Common-file settings have no substantiated gameplay mapping. Use the record explorer.")]
                : _gameplay.Summary(Bind)),
        ]),
        new Tab("Characters", _common ? [] : [.. _planning.CharacterViews(), _characters.Build(_planning.CharacterViews())]),
        new Tab("Catalog", _collections?.CatalogViews() ?? []),
        new Tab("Inventory", _collections is null ? [] : [_planning.InventoryView(), _collections.View("inventory")]),
        new Tab("Materia", _collections is null ? [] : [.. _planning.MateriaViews(), _collections.View("materia")]),
        new Tab("Progress", _collections is null ? [] : [_collections.View("progress"),
            .. (_collections.Contains("research-progress") ? new ViewNode[] { _collections.View("research-progress"), _progressSummary!.Build() } : [])]),
        new Tab("Enemy intel", _collections is null ? [] : [_collections.View("enemy-intel")]),
        new Tab(RegionalMaps.TabTitle, _maps?.Sections() ?? []),
        new Tab(SocketGraphs.TabTitle, _common ? [] : _sockets is null
            ? [new TextNode("Remake weapon-upgrade nodes, SP costs, prerequisite edges and socket links are unmapped. Equipment writes require complete instance/capacity handling; see the owner checklist.")]
            : [.. _sockets.Sections(), .. _folios!.Sections()]),
        new Tab("Cards", _collections?.Contains("cards") == true ? [_collections.View("cards")] : []),
        new Tab("Owned items", _ownedRelations is null || _collections is null ? [] :
            [_collections.View("owned-weapon"), _collections.View("owned-materia")]),
        new Tab("Records", [_recordCollection.View(), .. RecordGroups()]),
        new Tab("Raw file", [new HexNode("file", _bytes.Length) { Title = "Original bytes (paged)" }]),
    ]);

    public IReadOnlyList<QueryableCollection> Collections => [_recordCollection.Metadata, .. _collections?.Metadata ?? []];

    public QueryableCollectionPage Query(string collectionId, QueryableCollectionQuery query) =>
        collectionId == RecordCollection.Id ? _recordCollection.Query(query) :
            _collections?.Query(collectionId, query) ?? throw new KeyNotFoundException("Common files have no gameplay collections.");

    public ActionPlan PlanAction(string actionId, IReadOnlyDictionary<string, FieldValue> parameters, ActionScope scope) =>
        _common ? new() { Errors = [new("action", "Common files are read-only.")] } : _planning.Plan(actionId, parameters, scope);

    public EditOutcome ApplyActionPlan(string actionId, IReadOnlyDictionary<string, FieldValue> parameters, ActionScope scope, ActionPlan plan)
        => CanEdit ? _planning.Apply(actionId, parameters, scope, plan, Bind, ApplyEdits)
            : EditOutcome.Rejected("action", "Common files are read-only.");

    public override IReadOnlyList<ViewNode> ExpandNode(string lazyId)
    {
        var parts = lazyId.Split('/');
        if (parts.Length == 3 && parts[0] == "spatial" && TryIndex(parts[2], out var spatialIndex))
        {
            return ExpandSpatial(parts[1], spatialIndex);
        }
        if (parts.Length == 2 && TryIndex(parts[1], out var index))
        {
            if (parts[0] is "inventory" or "materia" && !_common)
            {
                return [_gameplay.Page(parts[0] == "materia", index, Bind)];
            }

            if (parts[0] == "records" && index < (_records.Count + RecordsPerSection - 1) / RecordsPerSection)
            {
                return [.. _records.Skip(index * RecordsPerSection).Take(RecordsPerSection)
                    .Select(r => SectionNode.Lazy($"#{r.Index} {r.Tag} · 0x{r.Offset:X}", $"record/{r.Index}"))];
            }

            if (parts[0] == "record" && index < _records.Count)
            {
                return _explorer.Record(index);
            }
        }

        if (parts.Length == 3 && parts[0] == "owned" && _ownedRelations is not null &&
            TryIndex(parts[2], out var ownedPage))
        {
            return [_ownedRelations.Page(parts[1], ownedPage)];
        }

        if (parts.Length == 3 && parts[0] == "catalog" && parts[1] is "items" or "materia" && !_common &&
            TryIndex(parts[2], out var catalogPage))
        {
            return [_references.CatalogPage(parts[1] == "materia", catalogPage)];
        }

        if (parts.Length == 4 && parts[0] == "words" && TryIndex(parts[1], out var record) &&
            TryIndex(parts[2], out var offset) && TryIndex(parts[3], out var length))
        {
            return _explorer.Words(record, offset, length);
        }

        throw new KeyNotFoundException($"Unknown FFVII section '{lazyId}'.");
    }

    private IReadOnlyList<ViewNode> ExpandSpatial(string kind, int index)
    {
        var nodes = kind switch
        {
            "map" when _maps is not null => _maps.Expand(index),
            "folio" when _folios is not null => _folios.Expand(index),
            "sockets" when _sockets is not null => _sockets.Expand(index),
            _ => throw new KeyNotFoundException("Unknown FFVII spatial view."),
        };
        _spatialPublished = true;
        return nodes;
    }

    public override ReadOnlyMemory<byte> ReadBlob(string blobId, long offset, int count)
    {
        var start = 0;
        var length = _bytes.Length;
        if (blobId != "file")
        {
            var parts = blobId.Split('/');
            if (parts.Length != 2 || parts[0] != "record" || !TryIndex(parts[1], out var index) || index >= _records.Count)
            {
                throw new KeyNotFoundException($"Unknown FFVII blob '{blobId}'.");
            }

            start = _records[index].Offset;
            length = _records[index].Length;
        }

        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(offset, length);
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(count, MaximumBlobPage);
        return _bytes.AsMemory(start + (int)offset, Math.Min(count, length - (int)offset)).ToArray();
    }

    public override EditOutcome ApplyEdits(IReadOnlyList<ValueEdit> edits)
    {
        if (!CanEdit) return base.ApplyEdits(edits);
        try
        {
            var staged = _progression.Expand(edits, GetValue);
            var outcome = _bindings.HasConflictingAliases(staged)
                ? EditOutcome.Rejected("edit", "Conflicting aliases address the same bytes.")
                : base.ApplyEdits(staged);
            return outcome with { StructureChanged = outcome.Succeeded && _spatialPublished && outcome.Changes.Any(change =>
                change.ValueId.StartsWith("progress/", StringComparison.Ordinal) ||
                change.ValueId.StartsWith("character/", StringComparison.Ordinal) && change.ValueId.EndsWith("/weapon", StringComparison.Ordinal)) };
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or OverflowException or KeyNotFoundException)
        {
            return EditOutcome.Rejected("edit", exception.Message);
        }
    }

    public override AssetContent? GetAsset(string assetId) => _references.GetAsset(assetId);

    public override IReadOnlyList<FileOutput> Serialize()
    {
        var output = _bytes.ToArray();
        _ = LinkedRecords.Parse(output, _schema, _common);
        return [new FileOutput(_path, output)];
    }

    private FieldNode Bind(ScalarField scalar) => _bindings.Bind(scalar);

    private IEnumerable<ViewNode> RecordGroups() => Enumerable.Range(0, (_records.Count + RecordsPerSection - 1) / RecordsPerSection)
        .Select(page => SectionNode.Lazy($"Explore records {page * RecordsPerSection}–{Math.Min(_records.Count, (page + 1) * RecordsPerSection) - 1}", $"records/{page}"));

    private static bool TryIndex(string text, out int value) =>
        int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value) && value >= 0;
}
