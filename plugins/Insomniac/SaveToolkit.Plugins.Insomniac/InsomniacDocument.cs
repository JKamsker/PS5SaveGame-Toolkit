using System.Globalization;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.Insomniac;

internal sealed class InsomniacDocument : BoundSaveDocument, IQueryableDocument, IActionPlanningDocument
{
    private readonly string _path;
    private readonly string _fileName;
    private readonly Dat1Container _container;
    private readonly RiftPlayerView _player;
    private readonly RiftScalarWrites _writes;
    private readonly RiftBoltPlanning _planning;
    private readonly Dat1ExplorerView _explorer;
    private readonly Dictionary<string, Func<FieldValue>> _liveReadOnly = new(StringComparer.Ordinal);
    private readonly DdlSections _sections;
    private readonly DdlTreeView _tree;
    private readonly RiftReadOnlyViews _readOnly;
    private readonly RiftWorldView _world;
    private readonly InventoryView _inventory;
    private readonly RiftCollections _collections = new();
    private readonly RiftArsenalView _arsenal;
    private readonly RiftArmourView _armour;
    private readonly IReadOnlyList<ViewNode> _progress;
    private readonly IReadOnlyList<ViewNode> _collectibles;
    private readonly IReferenceCatalog? _guideMarkers;
    private readonly IReferenceContext _references;
    private readonly Dictionary<string, Func<IReadOnlyList<ViewNode>>> _lazy = new(StringComparer.Ordinal);

    internal InsomniacDocument(string path, Dat1Container container, string fileName, IReferenceContext references, bool supportedTitle)
    {
        _path = path;
        _references = references;
        _fileName = fileName;
        _container = container;
        _sections = new DdlSections(container);
        _player = new RiftPlayerView(container, _sections);
        _writes = new RiftScalarWrites(container, _sections, supportedTitle);
        _planning = new RiftBoltPlanning(_writes);
        var schema = references.Catalog("rift-schema");
        _tree = new DdlTreeView(BindReadOnly, _lazy, schema);
        _explorer = new Dat1ExplorerView(container, _sections, _tree, _lazy, BindReadOnly);
        _readOnly = new RiftReadOnlyViews(_sections, references.Catalog("rift-weapons"), schema, BindScalar);
        _world = new RiftWorldView(_sections, BindReadOnly);
        var blocks = InventoryReader.Read(container);
        _inventory = new InventoryView(blocks,
            references.Catalog("rift-item-types"), BindReadOnly, _lazy, _collections);
        _inventory.RegisterCollections();
        _arsenal = new RiftArsenalView(blocks, references.Catalog("rift-weapons"),
            references.Catalog("rift-weapon-upgrades"), _collections, BindReadOnly, _lazy);
        _armour = new RiftArmourView(_sections, blocks, references.Catalog("rift-armor"), _collections, BindReadOnly);
        _progress = new RiftProgressView(_sections, _collections, BindReadOnly).Build();
        _collectibles = new RiftCollectibleView(_sections, references.Catalog("rift-world"), _collections, BindReadOnly).Build();
        _guideMarkers = references.Catalog(RiftGuideMaps.CatalogId);
    }

    public override string Title => _fileName.Equals("i29-userprefs.save", StringComparison.OrdinalIgnoreCase)
        ? "Rift Apart · User preferences"
        : "Rift Apart · Gameplay save";

    public override bool CanEdit => _writes.CanEdit;

    public IReadOnlyList<QueryableCollection> Collections => _collections.Metadata;
    public QueryableCollectionPage Query(string collectionId, QueryableCollectionQuery query) => _collections.Query(collectionId, query);

    public override ViewNode BuildView()
    {
        _lazy.Clear();
        var tabs = new List<Tab>();
        if (_player.HasCampaign)
        {
            var player = _player.Build(BindScalar);
            tabs.Add(new Tab("Player", _planning.Build() is { } action ? [.. player, action] : player));
        }
        if (_player.HasCampaign)
        {
            tabs.Add(new Tab("Loadout", _readOnly.Loadout()));
            tabs.Add(new Tab("Weapons", _arsenal.Weapons()));
            tabs.Add(new Tab("Armour", _armour.Build()));
            tabs.Add(new Tab("Upgrades", _arsenal.Upgrades()));
            var guideMaps = RiftGuideMaps.Build(_guideMarkers, _references, _lazy);
            tabs.Add(new Tab("Collectibles", guideMaps is null ? _collectibles : [.. _collectibles, guideMaps]));
            tabs.Add(new Tab("Inventory", _inventory.Build()));
            tabs.Add(new Tab("World", [.. _progress, .. _world.Build()]));
            tabs.Add(new Tab("Campaign details", _readOnly.CampaignDetails()));
        }
        if (_sections.Roots.ContainsKey(DdlSections.PreferencesTag))
            tabs.Add(new Tab("Preferences", _readOnly.Preferences()));
        tabs.Add(new Tab("Systems", _sections.Roots.Select(root =>
        {
            var id = $"system/{root.Key:x8}";
            _lazy[id] = () => _tree.Fields($"ddl/{root.Key:x8}", root.Value);
            return (ViewNode)new SectionNode(_sections.Label(root.Key), [])
            {
                LazyId = id,
                Collapsed = true,
                Badge = $"{root.Value.Count} root fields",
                Description = DdlSections.Attribution,
            };
        }).ToArray()));
        tabs.AddRange([
            new Tab("Overview", [
                new StatsNode([
                    new Stat("File size", FormatBytes(_container.Bytes.Length)),
                    new Stat("Sections", _container.SectionCount.ToString(CultureInfo.InvariantCulture)),
                    new Stat("Auxiliary records", _container.AuxiliaryCount.ToString(CultureInfo.InvariantCulture)),
                    new Stat("DAT1 word", $"0x{_container.Word:X8}"),
                ]),
                new SectionNode("Container header", _explorer.HeaderFields()),
                new TextNode(RiftScalarWrites.Warning)
                {
                    Tone = Tone.Warning,
                },
            ]),
            new Tab("Sections", _explorer.BuildSectionsView()),
            new Tab("Raw bytes", [new HexNode("file", _container.Bytes.Length) { Title = "Complete DAT1 file" }]),
        ]);
        return new TabsNode(tabs);
    }

    public override IReadOnlyList<ViewNode> ExpandNode(string lazyId) =>
        _lazy.TryGetValue(lazyId, out var load)
            ? load()
            : throw new KeyNotFoundException($"Unknown DAT1 section '{lazyId}'.");

    public override ReadOnlyMemory<byte> ReadBlob(string blobId, long offset, int count)
    {
        ReadOnlySpan<byte> source = blobId switch
        {
            "file" => _container.Bytes,
            "prefix" => _container.Bytes.AsSpan(_container.DescriptorEnd, _container.PrefixLength),
            _ when _explorer.TrySection(blobId, out var section) => _container.SectionBytes(section),
            _ => throw new KeyNotFoundException($"Unknown DAT1 blob '{blobId}'."),
        };

        if (offset < 0 || count < 0 || offset > source.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(offset));
        }

        var length = Math.Min(count, source.Length - checked((int)offset));
        return source.Slice(checked((int)offset), length).ToArray();
    }

    public override IReadOnlyList<FileOutput> Serialize()
    {
        var reparsed = Dat1Container.Parse(_container.Bytes);
        _ = new RiftPlayerView(reparsed);
        _ = InventoryReader.Read(reparsed);
        return [new FileOutput(_path, reparsed.Bytes)];
    }

    private FieldNode BindReadOnly(string id, string label, FieldSpec spec, FieldValue value)
    {
        if (_writes.TryDiagnostic(id, out var write)) return BindLiveReadOnly(id, label, spec, () => write.Value);
        if (id == RiftScalarWrites.BalanceId && _writes.HasBalance)
            return BindLiveReadOnly(id, label, spec, () => _writes.Balance);
        return Field(id, label, spec, () => value);
    }

    private FieldNode BindScalar(string id, string label, FieldSpec spec, FieldValue value) =>
        _writes.TryWrite(id, out var write)
            ? Field(id, label, write.Spec, () => write.Value, next => _writes.Set(write, next), write.Validate)
            : BindReadOnly(id, label, spec, value);

    private FieldNode BindLiveReadOnly(string id, string label, FieldSpec spec, Func<FieldValue> get)
    {
        _liveReadOnly[id] = get;
        return Field(id, label, spec, get);
    }

    public override EditOutcome ApplyEdits(IReadOnlyList<ValueEdit> edits)
    {
        if (_writes.ValidateBatch(edits) is { } error) return new EditOutcome { Errors = [error] };
        var before = _liveReadOnly.ToDictionary(pair => pair.Key, pair => pair.Value(), StringComparer.Ordinal);
        var outcome = base.ApplyEdits(edits);
        if (!outcome.Succeeded) return outcome;
        var derived = before.Select(pair => new ValueChange(pair.Key, pair.Value, _liveReadOnly[pair.Key]()))
            .Where(change => change.OldValue != change.NewValue);
        return outcome with { Changes = [.. outcome.Changes, .. derived] };
    }

    public ActionPlan PlanAction(string actionId, IReadOnlyDictionary<string, FieldValue> parameters, ActionScope scope)
    {
        var plan = _planning.Plan(actionId, parameters, scope);
        if (plan.Errors.Count > 0) return plan;
        var changes = new List<PlannedChange>(plan.Changes);
        foreach (var (id, get) in _liveReadOnly)
        {
            if (!_writes.TryDiagnostic(id, out var diagnostic)) continue;
            var primary = plan.Changes.FirstOrDefault(change => !change.Derived &&
                _writes.TryWrite(change.ValueId, out var write) && ReferenceEquals(write, diagnostic));
            if (primary is not null)
                changes.Add(new(id, $"Raw diagnostic: {diagnostic.Field.Name}", get(), primary.NewValue) { Derived = true });
        }
        return plan with { Changes = changes };
    }

    public EditOutcome ApplyActionPlan(string actionId, IReadOnlyDictionary<string, FieldValue> parameters, ActionScope scope, ActionPlan plan)
    {
        var current = PlanAction(actionId, parameters, scope);
        if (current.Errors.Count > 0) return new() { Errors = current.Errors, Warnings = current.Warnings };
        if (plan.Errors.Count > 0 || !current.Changes.SequenceEqual(plan.Changes) ||
            !current.SkippedRows.SequenceEqual(plan.SkippedRows) || !current.Warnings.SequenceEqual(plan.Warnings))
            return EditOutcome.Rejected(actionId, "The preview changed; preview again before applying.") with { Warnings = current.Warnings };
        // Register primary and derived bindings even for callers that have not built the view.
        _ = _player.Build(BindScalar);
        return ApplyEdits(current.Changes.Where(change => !change.Derived)
            .Select(change => new ValueEdit(change.ValueId, change.NewValue)).ToArray()) with { Warnings = current.Warnings };
    }

    public override ActionOutcome InvokeAction(string actionId, IReadOnlyDictionary<string, FieldValue> parameters) =>
        actionId == RiftBoltPlanning.ActionId
            ? new() { Message = "Preview and apply this action through F6 planning.", Warnings = [RiftScalarWrites.Warning] }
            : base.InvokeAction(actionId, parameters);

    private static string FormatBytes(int count) => count >= 1024 * 1024
        ? $"{count / (1024d * 1024d):0.0} MiB"
        : $"{count / 1024d:0.0} KiB";
}
