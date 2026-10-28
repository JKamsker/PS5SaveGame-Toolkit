using System.Buffers.Binary;
using System.Globalization;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.FinalFantasy7;

internal sealed class FinalFantasy7Document : BoundSaveDocument, IQueryableDocument, IActionPlanningDocument
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
    private readonly MateriaPlanning _materiaPlanning;

    public FinalFantasy7Document(string path, string name, byte[] bytes, Ff7Schema schema, bool common,
        IReadOnlyList<LinkedRecord> records, IReferenceContext? references = null)
    {
        _path = path;
        _bindings = new(bytes, Field);
        _bytes = bytes;
        _schema = schema;
        _common = common;
        _records = records;
        _references = new(references ?? IReferenceContext.Empty, schema);
        _gameplay = new(bytes, schema, _references, _progression);
        _materiaPlanning = new(schema, _gameplay, _references);
        _explorer = new(bytes, records, Bind);
        _characters = new(bytes, schema, _references, Bind, _progression);
        _ownedRelations = !common && !schema.IsRemake ? new(bytes, _references) : null;
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
        new Tab("Characters", _common ? [] : [_characters.Build()]),
        new Tab("Catalog", _collections?.CatalogViews() ?? []),
        new Tab("Inventory", _collections is null ? [] : [_collections.View("inventory")]),
        new Tab("Materia", _collections is null ? [] : [_materiaPlanning.View(), _collections.View("materia")]),
        new Tab("Progress", _collections is null ? [] : [_collections.View("progress")]),
        new Tab("Cards", _collections?.Contains("cards") == true ? [_collections.View("cards")] : []),
        new Tab("Owned items", _ownedRelations is null || _collections is null ? [] :
            [_collections.View("owned-weapon"), _collections.View("owned-materia")]),
        new Tab("Records", [RecordTable(), .. RecordGroups()]),
        new Tab("Raw file", [new HexNode("file", _bytes.Length) { Title = "Original bytes (paged)" }]),
    ]);

    public IReadOnlyList<QueryableCollection> Collections => _collections?.Metadata ?? [];

    public QueryableCollectionPage Query(string collectionId, QueryableCollectionQuery query) =>
        _collections?.Query(collectionId, query) ?? throw new KeyNotFoundException("Common files have no gameplay collections.");

    public ActionPlan PlanAction(string actionId, IReadOnlyDictionary<string, FieldValue> parameters, ActionScope scope) =>
        _common ? new() { Errors = [new("action", "Common files are read-only.")] } : _materiaPlanning.Plan(actionId, parameters, scope);

    public EditOutcome ApplyActionPlan(string actionId, IReadOnlyDictionary<string, FieldValue> parameters, ActionScope scope, ActionPlan plan)
    {
        var current = PlanAction(actionId, parameters, scope);
        if (!CanEdit || current.Errors.Count > 0 || !current.Changes.SequenceEqual(plan.Changes) ||
            !current.SkippedRows.SequenceEqual(plan.SkippedRows) || !current.Warnings.SequenceEqual(plan.Warnings) || plan.Errors.Count > 0)
        {
            return EditOutcome.Rejected("action", "The AP plan is invalid or has changed; preview again.");
        }

        foreach (var change in current.Changes)
        {
            var slot = int.Parse(change.ValueId.Split('/')[1], CultureInfo.InvariantCulture);
            _ = _gameplay.Row(true, slot, Bind);
        }

        return ApplyEdits([.. current.Changes.Select(change => new ValueEdit(change.ValueId, change.NewValue))]);
    }

    public override IReadOnlyList<ViewNode> ExpandNode(string lazyId)
    {
        var parts = lazyId.Split('/');
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
            return _bindings.HasConflictingAliases(staged)
                ? EditOutcome.Rejected("edit", "Conflicting aliases address the same bytes.")
                : base.ApplyEdits(staged);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or OverflowException or KeyNotFoundException)
        {
            return EditOutcome.Rejected("edit", exception.Message);
        }
    }

    public override IReadOnlyList<FileOutput> Serialize()
    {
        var output = _bytes.ToArray();
        _ = LinkedRecords.Parse(output, _schema, _common);
        return [new FileOutput(_path, output)];
    }

    private FieldNode Bind(ScalarField scalar) => _bindings.Bind(scalar);

    private TableNode RecordTable() => new(
        [new("index", "#"), new("tag", "Tag"), new("offset", "Offset"), new("length", "Span bytes"),
            new("next", "Next pointer"), new("version", "Raw +0x20"), new("state", "State byte")],
        [.. _records.Select(r => new TableRow($"record-row/{r.Index}", new Dictionary<string, ViewNode>(StringComparer.Ordinal)
        {
            ["index"] = new TextNode(r.Index.ToString()),
            ["tag"] = new TextNode(r.Tag),
            ["offset"] = Hex(r.Offset),
            ["length"] = new TextNode(r.Length.ToString()),
            ["next"] = Hex(r.Next),
            ["version"] = Hex(r.Version),
            ["state"] = Hex(r.State),
        }))]) { Title = "Linked records", Description = "Offsets are absolute. Empty records retain their complete allocated span. Tail pointer is shown literally." };

    private IEnumerable<ViewNode> RecordGroups() => Enumerable.Range(0, (_records.Count + RecordsPerSection - 1) / RecordsPerSection)
        .Select(page => SectionNode.Lazy($"Explore records {page * RecordsPerSection}–{Math.Min(_records.Count, (page + 1) * RecordsPerSection) - 1}", $"records/{page}"));

    private static TextNode Hex(long value) => new($"0x{value:X}") { Monospace = true };
    private static bool TryIndex(string text, out int value) =>
        int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value) && value >= 0;
}
