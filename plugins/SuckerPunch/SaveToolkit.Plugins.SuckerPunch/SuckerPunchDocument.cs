using System.Buffers.Binary;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Saves;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.SuckerPunch;

internal sealed class SuckerPunchDocument : BoundSaveDocument, IActionPlanningDocument, IQueryableDocument, IAdditionalOutputOwner, IFieldMetadataDocument
{
    private const string InventoryBlockId = "blocks/inventory";
    private const string ProgressBlockId = "blocks/progress";
    private readonly ISaveFile _file;
    private readonly byte[] _bytes;
    private readonly SaveContainer _container;
    private readonly TsushimaInventory? _inventory;
    private readonly TsushimaProgress? _progress;
    private readonly IReadOnlyList<TsushimaIdentifier> _identifiers;
    private readonly IReferenceContext _references;
    private readonly IReferenceCatalog? _items;
    private readonly TsushimaReferenceViews _views;
    private readonly TsushimaSpatialViews _spatialViews;
    private readonly TsushimaQueryableCollections _queryableCollections;
    private readonly TsushimaQuantityPlanning _quantityPlanning;
    private readonly SuckerPunchCaption _caption;
    private readonly SuckerPunchCaptionEditing _captionEditing;
    private readonly TsushimaFieldMetadata _fieldMetadata;
    private readonly SuckerPunchOverview _overview;
    private readonly TsushimaPlayTime? _playTime;

    private SuckerPunchDocument(ISaveFile file, byte[] bytes, SaveContainer container, IReferenceContext references)
    {
        _file = file;
        _bytes = bytes;
        _container = container;
        _references = references;
        if (container.Kind == ContainerKind.Tsushima)
        {
            var content = bytes.AsSpan(0, (int)container.Word1);
            _inventory = TsushimaInventory.Parse(content);
            _progress = TsushimaProgress.Parse(content, container.Word3);
            _identifiers = TsushimaIdentifiers.Scan(content);
            _items = references.Catalog(TsushimaIds.Items);
        }
        else
        {
            _identifiers = [];
        }

        _overview = new SuckerPunchOverview(file, bytes, container, _progress);
        if (_inventory is not null && _progress?.PlaySeconds is { } seconds)
        {
            _playTime = new TsushimaPlayTime(bytes, _progress.PlayTimeOffset, seconds);
        }

        _caption = SuckerPunchCaption.Open(file, container, CanEdit);
        _captionEditing = new SuckerPunchCaptionEditing(file, bytes, _caption);
        _fieldMetadata = new TsushimaFieldMetadata(bytes, _inventory, _playTime);
        _views = new TsushimaReferenceViews(references, _identifiers, BindReadOnly);
        _spatialViews = new TsushimaSpatialViews(references, _identifiers);
        _queryableCollections = new TsushimaQueryableCollections(_views, _identifiers.Count > 0);
        _quantityPlanning = new TsushimaQuantityPlanning(bytes, container, _inventory);
    }

    public override string Title => string.IsNullOrWhiteSpace(_caption.Text ?? _file.Directory.ParamSfo?.SubTitle)
        ? $"{_file.Directory.TitleId} · {_file.FileName}"
        : $"{_file.Directory.TitleId} · {_caption.Text ?? _file.Directory.ParamSfo?.SubTitle}";

    public IReadOnlyList<string> AdditionalOutputPaths => _caption.AdditionalOutputPaths;

    public override bool CanEdit => _inventory is not null;

    public static SuckerPunchDocument Open(ISaveFile file, IReferenceContext references)
    {
        var bytes = file.ReadAllBytes();
        return new SuckerPunchDocument(file, bytes, SaveContainer.Parse(bytes, file.Directory.TitleId), references);
    }

    public override ViewNode BuildView()
    {
        var overview = _overview.Build((id, label, spec, get) => Field(id, label, spec, get), _caption.Available).ToList();
        overview.AddRange(_caption.View());
        BindCaption();
        if (_playTime is { } timer)
        {
            overview.Add(Field(TsushimaPlayTime.ValueId, "Play time", timer.Spec, timer.Read, timer.Write, timer.Validate) with
            {
                Confidence = Confidence.Inferred,
                Description = "Float32 at difficulty-table + 231; whole minutes match all v48/v49 slot subtitles. " + TsushimaPlayTime.Warning,
                Warning = TsushimaPlayTime.Warning,
            });
        }

        var tabs = new List<Tab> { new("Overview", overview) };
        if (_inventory is not null)
        {
            tabs.Add(new Tab("Inventory", [new TextNode(
                TsushimaWriteWarnings.Console + " Experimental edits: only existing unlocked records can change. " +
                "Ammo updates both count copies. The game may clamp quantities to its capacity limits.") { Tone = Tone.Warning },
                _views.Inventory(_inventory, record => _fieldMetadata.Quantity(record, _items, RegisterField),
                    record => _fieldMetadata.Mirror(record, RegisterField)), TsushimaQuantityPlanning.View, TsushimaQuantityPlanning.InventoryView,
                TsushimaQuantityPlanning.RestoreAmmoView, TsushimaReferenceViews.ResourceGuide]));
            if (_identifiers.Count > 0)
            {
                tabs.Add(new Tab("Equipment", [new TextNode(
                    "Read-only identifier evidence. Presence in the save does not prove ownership, an equipped slot, " +
                    "a loadout, or a writable equipment record.") { Tone = Tone.Warning },
                    _views.Identifiers(IdentifierGroup.Equipment, TsushimaIds.Equipment), .. TsushimaReferenceViews.EquipmentGuides]));
                tabs.Add(new Tab("Techniques", [new TextNode(
                    "Read-only identifier evidence. Prerequisites, points, unlock state and progression effects are " +
                    "not established by these strings.") { Tone = Tone.Warning },
                    _views.Identifiers(IdentifierGroup.Technique, TsushimaIds.Techniques), _spatialViews.TechniqueGraph(),
                    TsushimaReferenceViews.TechniqueGuide]));
                tabs.Add(new Tab("Character", [new TextNode(
                    "Read-only relationships inferred from identifier names. Values are not exposed as edits, and " +
                    "the scanner does not distinguish definitions, counters, ownership or active state.") { Tone = Tone.Warning },
                    _views.Character()]));
            }
            tabs.Add(new Tab("World", [new TextNode(
                "Shrine reference by region. Discovery, completion and rewards cannot be read from the supplied saves yet; " +
                "all location states remain Unknown. This is not a missing-content tracker.") { Tone = Tone.Warning },
                _spatialViews.CollectibleMap(), _views.WorldReferenceTable(),
                TsushimaReferenceViews.WorldGuide]));
            tabs.Add(new Tab("Blocks", BuildBlocks()));
        }

        tabs.Add(new Tab("Raw bytes", [new HexNode("content", _bytes.Length) { Title = "Complete save bytes" }]));
        return new TabsNode(tabs);
    }

    public ActionPlan PlanAction(string actionId, IReadOnlyDictionary<string, FieldValue> parameters, ActionScope scope)
        => actionId == SuckerPunchCaption.ActionId ? _caption.Plan(parameters, scope) : _quantityPlanning.Plan(actionId, parameters, scope);
    public IReadOnlyList<QueryableCollection> Collections => _queryableCollections.Collections;

    public QueryableCollectionPage Query(string collectionId, QueryableCollectionQuery query)
        => _queryableCollections.Query(collectionId, query);
    public EditOutcome ApplyActionPlan(string actionId, IReadOnlyDictionary<string, FieldValue> parameters, ActionScope scope, ActionPlan plan) =>
        ActionPlans.ApplyScalar(plan, () => PlanAction(actionId, parameters, scope),
            new(actionId, "The action preview is stale or does not match the request. Preview again."),
            _ => { BuildView(); },
            current => current.Changes.Where(change => !change.Derived)
                .Select(change => new ValueEdit(change.ValueId, change.NewValue)).ToArray(),
            ApplyEdits, ActionPlanWarningPolicy.UsePlan);

    protected override IEnumerable<ValueChange> DerivedChanges(IReadOnlyList<ValueChange> applied)
    {
        if (applied.Count == 0)
        {
            return [];
        }

        var derived = new List<ValueChange>();
        foreach (var change in applied)
        {
            var record = _inventory?.Records.SingleOrDefault(record => record.ValueId == change.ValueId);
            if (record?.IsAmmo == true)
            {
                derived.Add(new ValueChange(record.MirrorValueId, change.OldValue,
                    FieldValue.Of((ulong)ReadWord(record.QuantityOffset + sizeof(uint)))));
            }
        }

        var old = ReadWord(8);
        var checksum = SaveContainer.CalculateTsushimaChecksum(_bytes, _container.Word1, _container.Magic, _container.Word3);
        WriteWord(8, checksum);
        if (old != checksum)
        {
            derived.Add(new ValueChange(TsushimaIds.ChecksumValue, FieldValue.Of((ulong)old), FieldValue.Of((ulong)checksum)));
        }
        return derived;
    }

    public override EditOutcome ApplyEdits(IReadOnlyList<ValueEdit> edits)
    {
        if (edits.Any(edit => edit.ValueId == SuckerPunchCaption.ValueId)) return _captionEditing.Apply(edits);
        var outcome = base.ApplyEdits(edits);
        // Raw blocks contain snapshot text; rebuild them and the current timer value after edits.
        var warnings = new List<string>(outcome.Warnings);
        if (outcome.Succeeded && outcome.Changes.Count > 0)
        {
            if (edits.Any(edit => edit.ValueId == TsushimaPlayTime.ValueId))
            {
                warnings.Add(TsushimaPlayTime.Warning);
            }
            if (edits.Any(edit => edit.ValueId.StartsWith("inventory/", StringComparison.Ordinal)))
            {
                warnings.Add(TsushimaQuantityPlanning.CapacityWarning);
            }
        }
        return outcome with { StructureChanged = outcome.Succeeded && outcome.Changes.Count > 0, Warnings = warnings };
    }

    public override IReadOnlyList<FileOutput> Serialize()
    {
        var output = _bytes.ToArray();
        var parsed = SaveContainer.Parse(output, _file.Directory.TitleId);
        if (parsed.Kind == ContainerKind.Tsushima)
        {
            var content = output.AsSpan(0, (int)parsed.Word1);
            _ = TsushimaInventory.Parse(content);
            var progress = TsushimaProgress.Parse(content, parsed.Word3);
            if (_playTime is not null && (progress?.PlaySeconds is not { } seconds || seconds != _playTime.Read().AsDouble()))
            {
                throw new SaveFormatException("The edited Tsushima play-time field did not reparse correctly.");
            }
        }

        return _caption.Available ? [new(_file.RelativePath, output), _caption.Serialize()] : [new(_file.RelativePath, output)];
    }

    public override IReadOnlyList<ViewNode> ExpandNode(string lazyId) => lazyId switch
    {
        SuckerPunchCaption.DetailsId when _caption.Available => [_caption.FieldView],
        InventoryBlockId when _inventory is not null => [TsushimaReferenceViews.RawInventory(_inventory, ReadWord)],
        _ when lazyId.StartsWith(TsushimaIds.GuidePrefix, StringComparison.Ordinal) && _inventory is not null => _views.ExpandGuide(lazyId),
        ProgressBlockId when _progress is not null => [new TextNode(
            $"Difficulty block: 0x{_progress.Start:X}–0x{_progress.End:X} (exclusive). " +
            "Four length-prefixed difficulty names each precede a float32 counter. Counter semantics are inferred; " +
            "their sum does not match subtitle play time. The separate v48/v49 play timer is at table + 231.")],
        _ => throw new KeyNotFoundException($"Unknown lazy node '{lazyId}'."),
    };

    public override ReadOnlyMemory<byte> ReadBlob(string blobId, long offset, int count)
    {
        if (blobId != "content")
        {
            throw new KeyNotFoundException($"Unknown blob '{blobId}'.");
        }

        if (offset < 0 || count < 0 || offset > _bytes.Length - count)
        {
            throw new ArgumentOutOfRangeException(nameof(offset));
        }

        // A caller must not be able to mutate the document through the returned memory.
        return _bytes.AsSpan((int)offset, count).ToArray();
    }

    public override AssetContent? GetAsset(string assetId) => _references.GetAsset(assetId);

    private FieldNode BindReadOnly(FieldNode node)
    {
        _ = Field(node.ValueId, node.Label, node.Spec, () => node.Value);
        return node;
    }

    private IReadOnlyList<ViewNode> BuildBlocks()
    {
        var nodes = new List<ViewNode>
        {
            new TextNode($"Inventory: 0x{_inventory!.Start:X}–0x{_inventory.End:X} (exclusive). " +
                "uint32 count, then 48 records: uint32 name length, ASCII name, five little-endian words."),
            SectionNode.Lazy("Inventory record words", InventoryBlockId, "48 records"),
        };
        if (_progress is not null)
        {
            nodes.Add(SectionNode.Lazy("Difficulty and play-time block", ProgressBlockId));
        }

        return nodes;
    }

    public override IReadOnlyList<FieldMetadata> ResolveFieldMetadata(IReadOnlyList<string> valueIds)
    {
        ArgumentNullException.ThrowIfNull(valueIds);
        if (valueIds.Count > FieldMetadataLimits.MaxBatchSize) throw new ArgumentOutOfRangeException(nameof(valueIds));
        foreach (var id in valueIds) ArgumentException.ThrowIfNullOrEmpty(id);
        BindCaption();
        _fieldMetadata.Bind(valueIds, RegisterField);
        return base.ResolveFieldMetadata(valueIds);
    }

    private void BindCaption()
    {
        if (_caption.Available) RegisterField(_caption.Metadata, _caption.Read, _caption.Write, _caption.Validate);
    }

    public override DocumentMemento RestoreMemento(DocumentMemento memento)
    {
        try { return base.RestoreMemento(memento); }
        finally { BindCaption(); }
    }

    protected override DocumentMemento? CaptureSnapshot() => _captionEditing.Capture();
    protected override void LoadSnapshot(DocumentMemento snapshot) => _captionEditing.Restore(snapshot);

    private uint ReadWord(int offset) => BinaryPrimitives.ReadUInt32LittleEndian(_bytes.AsSpan(offset));
    private void WriteWord(int offset, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(_bytes.AsSpan(offset), value);
}
