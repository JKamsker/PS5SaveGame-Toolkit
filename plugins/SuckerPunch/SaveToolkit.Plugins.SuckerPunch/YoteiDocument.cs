using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Saves;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.SuckerPunch;

/// <summary>Yōtei's fixed-width writer is deliberately separate from Tsushima's five-word records.</summary>
internal sealed class YoteiDocument : BoundSaveDocument, IActionPlanningDocument, IQueryableDocument
{
    private readonly ISaveFile _file;
    private readonly byte[] _bytes;
    private readonly SaveContainer _container;
    private readonly YoteiState? _state;
    private readonly IReferenceContext _references;
    private readonly YoteiViews? _views;
    private readonly YoteiPlanning _planning;
    private readonly Dictionary<string, uint> _limits;
    private readonly HashSet<string> _editable;

    private YoteiDocument(ISaveFile file, byte[] bytes, IReferenceContext references)
    {
        _file = file;
        _bytes = bytes;
        _references = references;
        _container = SaveContainer.Parse(bytes, file.Directory.TitleId);
        if (_container.Kind != ContainerKind.YoteiEmpty) _state = YoteiState.Parse(bytes, _container);
        _limits = _state?.Inventory.Where(row => row.ObservedMaximum.HasValue).ToDictionary(row => row.Name,
            row => Math.Max(row.ObservedMaximum!.Value, row.Quantity(bytes)), StringComparer.Ordinal) ?? [];
        _editable = _state?.Inventory.Where(row => _container.ChecksumValid && row.Eligible(bytes))
            .Select(row => row.Name).ToHashSet(StringComparer.Ordinal) ?? [];
        if (_state is not null) _views = new YoteiViews(bytes, _state, references, BindReadOnly);
        _planning = new YoteiPlanning(bytes, _state, _limits, _editable);
    }

    public static YoteiDocument Open(ISaveFile file, IReferenceContext references) => new(file, file.ReadAllBytes(), references);
    public override string Title => $"PPSA26344 · {_file.Directory.Name} · {_file.FileName}";
    public override bool CanEdit => _editable.Count > 0;

    public override ViewNode BuildView()
    {
        var header = Enumerable.Range(0, 4).Select(index => (ViewNode)Field($"container/word{index}",
            index switch { 0 => "Magic", 1 => "Header word 1 (meaning unknown)", 2 => "Declared content end", _ => "Additive checksum candidate" },
            IntegerSpec.UInt32 with { Format = IntegerFormat.Hex }, () => FieldValue.Of((ulong)YoteiFormat.Word(_bytes, index * sizeof(uint)))))
            .ToArray();
        var overview = new List<ViewNode>
        {
            new StatsNode([new Stat("Title", "PPSA26344"), new Stat("File", _file.PathInDirectory),
                new Stat("Size", $"{_bytes.Length:N0} bytes"),
                new Stat("Format", _state is null ? "Empty placeholder" : "Yōtei named records"),
                new Stat("Checksum", _container.ChecksumValid ? "Candidate matches; tail coverage unknown" : "Candidate does not match")]),
            new SectionNode("Container header", header),
            new TextNode(YoteiFormat.Warning) { Tone = Tone.Warning },
            new TextNode("Save caption unavailable: manual caption editing supports Tsushima PPSA02225 only; Yōtei role evidence is deferred.")
                { Tone = Tone.Warning },
        };
        if (_state is not null)
            overview.Add(new TextNode($"Inventory at 0x{_state.InventoryStart:X}; secondary table at 0x{_state.RegistryStart:X}. " +
                "The secondary table stays unchanged. Play time, health, spirit and current difficulty are not reliably decoded. " +
                "The Medium/Ship string is settings evidence, not a proven gameplay difficulty field."));
        var tabs = new List<Tab> { new("Overview", overview) };
        if (_state is not null && _views is not null)
        {
            tabs.Add(new("Inventory", [new TextNode(YoteiFormat.Warning) { Tone = Tone.Warning },
                _views.Inventory(QuantityField, CopyField), YoteiPlanning.View]));
            tabs.Add(new("Equipment", [new TextNode("1,037 registry names and two opaque bytes per name. " +
                "All registry bytes are identical across generations: these are not proven ownership or equipped flags.") { Tone = Tone.Warning },
                _views.GearTable]));
            tabs.Add(new("Techniques", _views.Techniques()));
            tabs.Add(new("World", _views.World()));
            tabs.Add(new("Statistics", [_views.Statistics()]));
        }
        tabs.Add(new("Raw bytes", [new HexNode("content", _bytes.Length)]));
        return new TabsNode(tabs);
    }

    private FieldNode QuantityField(YoteiRecord row)
    {
        var reference = _references.Catalog(YoteiViews.ItemsCatalog)?.Find(row.Name);
        var max = _limits.GetValueOrDefault(row.Name, row.Quantity(_bytes));
        return Field(row.QuantityId, reference?.Name ?? row.Name, new IntegerSpec(0, max),
            () => FieldValue.Of((ulong)row.Quantity(_bytes)),
            _editable.Contains(row.Name) ? value => row.Write(_bytes, value.AsUInt32()) : null,
            value => value.Kind != FieldValueKind.Integer || value.AsInteger() < 0 || value.AsInteger() > max
                ? $"Quantity must be an integer between 0 and {max}." : null) with
        {
            Confidence = Confidence.Inferred,
            Description = $"Raw ID {row.Name}; unaligned uint32 at 0x{row.QuantityOffset:X}; 21-byte trailer. " +
                YoteiFormat.Warning + " " + reference?.AttributionText,
            Warning = _editable.Contains(row.Name) ? YoteiFormat.Warning : "Unmapped, locked, inconsistent or checksum-mismatched record; inspection only.",
        };
    }

    private FieldNode CopyField(YoteiRecord row) => Field(row.CopyId, "Count copy", IntegerSpec.UInt32,
        () => FieldValue.Of((ulong)YoteiFormat.Word(_bytes, row.QuantityOffset + sizeof(uint)))) with
    { Confidence = Confidence.Inferred, Description = "Equal to the primary ammunition count in every sample; updated together." };

    private FieldNode BindReadOnly(FieldNode node)
    {
        _ = Field(node.ValueId, node.Label, node.Spec, () => node.Value);
        return node;
    }

    protected override IEnumerable<ValueChange> DerivedChanges(IReadOnlyList<ValueChange> applied)
    {
        if (applied.Count == 0 || _state is null) return [];
        var derived = new List<ValueChange>();
        foreach (var change in applied)
        {
            var row = _state.Inventory.Single(record => record.QuantityId == change.ValueId);
            if (row.Ammo) derived.Add(new(row.CopyId, change.OldValue, change.NewValue));
        }
        var before = YoteiFormat.Word(_bytes, YoteiFormat.ChecksumOffset);
        var after = YoteiFormat.Checksum(_bytes);
        YoteiFormat.Write(_bytes, YoteiFormat.ChecksumOffset, after);
        if (before != after) derived.Add(new("container/word3", FieldValue.Of((ulong)before), FieldValue.Of((ulong)after)));
        return derived;
    }

    public override EditOutcome ApplyEdits(IReadOnlyList<ValueEdit> edits)
    {
        var result = base.ApplyEdits(edits);
        return result.Succeeded && result.Changes.Count > 0
            ? result with { Warnings = [YoteiFormat.Warning], StructureChanged = true } : result;
    }

    public ActionPlan PlanAction(string actionId, IReadOnlyDictionary<string, FieldValue> parameters, ActionScope scope)
        => _planning.Plan(actionId, parameters, scope);

    public EditOutcome ApplyActionPlan(string actionId, IReadOnlyDictionary<string, FieldValue> parameters, ActionScope scope, ActionPlan plan)
    {
        var current = PlanAction(actionId, parameters, scope);
        if (current.Errors.Count > 0) return new EditOutcome { Errors = current.Errors };
        if (!QuantityPlanComparison.Matches(current, plan))
            return EditOutcome.Rejected(actionId, "The Yōtei quantity preview is stale or does not match the request. Preview again.");
        _ = BuildView();
        return ApplyEdits(current.Changes.Where(change => !change.Derived).Select(change => new ValueEdit(change.ValueId, change.NewValue)).ToArray())
            with { Warnings = current.Warnings };
    }

    public IReadOnlyList<QueryableCollection> Collections => _views is null ? [] : [_views.GearCollection];
    public QueryableCollectionPage Query(string collectionId, QueryableCollectionQuery query) => _views?.Query(collectionId, query)
        ?? throw new KeyNotFoundException($"Unknown collection '{collectionId}'.");

    public override IReadOnlyList<FileOutput> Serialize()
    {
        var output = _bytes.ToArray();
        var container = SaveContainer.Parse(output, _file.Directory.TitleId);
        if (_state is not null)
        {
            var reparsed = YoteiState.Parse(output, container);
            if (_container.ChecksumValid && !container.ChecksumValid ||
                !reparsed.Inventory.Select(row => row.Quantity(output)).SequenceEqual(_state.Inventory.Select(row => row.Quantity(_bytes))))
                throw new SaveFormatException("The edited Yōtei output failed checksum or quantity validation.");
        }
        return [new FileOutput(_file.RelativePath, output)];
    }

    public override AssetContent? GetAsset(string assetId) => _references.GetAsset(assetId);
    public override ReadOnlyMemory<byte> ReadBlob(string blobId, long offset, int count)
    {
        if (blobId != "content") throw new KeyNotFoundException($"Unknown blob '{blobId}'.");
        if (offset < 0 || count < 0 || offset > _bytes.Length - count) throw new ArgumentOutOfRangeException(nameof(offset));
        return _bytes.AsSpan((int)offset, count).ToArray();
    }
}
