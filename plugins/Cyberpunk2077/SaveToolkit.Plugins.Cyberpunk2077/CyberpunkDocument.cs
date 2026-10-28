using System.Buffers.Binary;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.References;

namespace SaveToolkit.Plugins.Cyberpunk2077;

internal sealed class CyberpunkDocument : BoundSaveDocument, IQueryableDocument
{
    private const int PageSize = 128;
    private const uint MaximumStack = 999_999; // Editor policy, not a proven game limit.
    private const uint MaximumMoney = 100_000_000; // Editor policy, not a proven game limit.
    private readonly string _path;
    private readonly CsavContainer _container;
    private readonly CsavTree _tree;
    private readonly IReadOnlyList<InventoryItem> _items;
    private readonly FactInspection _facts;
    private readonly InventoryInspection _inventory;
    private readonly CyberpunkReferences _references;
    private readonly byte[] _edited;
    private readonly InventoryItem? _money;
    private readonly ItemNames _names = new();
    private readonly ProgressionData? _progression;
    private readonly ProgressionEditor? _progressionEditor;
    private readonly string? _progressionWarning;
    private readonly EquipmentInspection _equipment;
    private readonly DevelopmentInspection _development;
    private readonly WorldInspection _world;
    private readonly InspectionTable _factCatalog;
    private readonly ItemStatsInspection _itemStats;
    private readonly JournalInspection _journal;
    private readonly CyberwareInspection _cyberware;
    private readonly IReadOnlyDictionary<string, InspectionTable> _inspectionTables;

    public CyberpunkDocument(string path, CsavContainer container, CsavTree tree, IReadOnlyList<InventoryItem> items,
        IReferenceContext references)
    {
        _path = path;
        _container = container;
        _tree = tree;
        _items = items;
        _edited = container.Payload.ToArray();
        _progression = ProgressionData.TryRead(container, tree, out _progressionWarning);
        _progressionEditor = _progression is null ? null : new ProgressionEditor(_progression, _edited);
        var candidates = items.Where(i => i.ContainerId == 1 && i.TweakId == InventoryReader.MoneyTweakId &&
            i.QuantityOffset.HasValue && tree.Nodes[i.NodeId].Size == 29 && i.Structure == 0).ToArray();
        _money = candidates.Length == 1 ? candidates[0] : null;
        _references = new CyberpunkReferences(references, container.GameVersion);
        ScriptableReader? reader = null;
        string? inspectionWarning = null;
        try { reader = new ScriptableReader(container, tree); }
        catch (SaveFormatException ex) { inspectionWarning = ex.Message; }
        EquipmentData? equipment = null;
        string? equipmentWarning = inspectionWarning;
        if (reader is not null)
        {
            try { equipment = EquipmentData.Read(reader, container, tree, items); }
            catch (SaveFormatException ex) { equipmentWarning = ex.Message; }
        }
        _equipment = new EquipmentInspection(equipment, _references, equipmentWarning);
        _cyberware = new CyberwareInspection(items, equipment, _references, container, tree);
        _development = new DevelopmentInspection(reader, inspectionWarning, _progression);
        _world = new WorldInspection(reader, container, tree, _references, inspectionWarning);
        _itemStats = new ItemStatsInspection(container, tree, items);
        _journal = new JournalInspection(container, tree);
        _inventory = new InventoryInspection(items, container, tree, _references, _names, _edited, QuantityField, ReadField, equipment);
        var facts = tree.Nodes.Where(n => n.Name == "FactsTable").ToDictionary(n => n.Id,
            n => FactsReader.Read(BinaryData.Slice(container.Payload, n.Offset, n.Size)));
        _facts = new FactInspection(facts, _references, ReadField,
            (id, value) => Field(id, "Raw fact value", IntegerSpec.UInt32, () => FieldValue.Of((ulong)value)));
        _factCatalog = FactCatalogInspection.Build(facts, _references);
        _inspectionTables = new[] { _equipment.Table, _cyberware.Table, _development.Table, _world.Vehicles, _world.Travel, _factCatalog, _itemStats.Table, _journal.Table }
            .ToDictionary(table => table.Metadata.Id, StringComparer.Ordinal);
    }

    public override string Title => "Cyberpunk 2077 · CSAV";
    public override bool CanEdit => _items.Any(IsWritable) || _progressionEditor?.CanEdit == true;

    public override ViewNode BuildView() => new TabsNode([
        new Tab("Overview", [
            new StatsNode([
                new Stat("Save version", _container.SaveVersion.ToString()),
                new Stat("Game version", _container.GameVersion.ToString()),
                new Stat("LZ4 chunks", _container.Chunks.Count.ToString()),
                new Stat("Nodes", _tree.Nodes.Count.ToString()),
                new Stat("Expanded bytes", _edited.Length.ToString())]),
            new TextNode(CanEdit
                ? "Experimental: player attributes, point balances and supported player stack quantities can be changed. Console loading has not been tested."
                : "Read-only: no editable records in a verified PS5 schema were found.") { Tone = Tone.Warning },
            new TextNode(_progressionWarning ?? "Level, street cred and XP are read-only until their transition thresholds are verified."),
            .. (_money is null ? Array.Empty<ViewNode>() : [QuantityField(_money)])]),
        new Tab("Player development", _progressionEditor?.Build(binding => Field(binding.Id, binding.Label, binding.Spec,
            binding.Get, binding.Set, binding.Validate) with { Confidence = Confidence.Verified })
            ?? [new TextNode(_progressionWarning ?? "Player development layout is not verified; edits refused.") { Tone = Tone.Warning }]),
        new Tab("Inventory", _inventory.Build()),
        new Tab("Equipment and cyberware", [.. _equipment.Build(), .. _cyberware.Build()]),
        new Tab("Item stat modifiers", _itemStats.Build()),
        new Tab("Skills and perks", _development.Build()),
        new Tab("World", _world.Build()),
        new Tab("Journal diagnostics", _journal.Build()),
        new Tab("Facts", [_facts.View(), _factCatalog.View()]),
        new Tab("Reference catalogs", _references.Build(CanEdit)),
        new Tab("Node tree", TreePage(-1, 0)),
        new Tab("Container", [SectionNode.Lazy("Chunk table", "chunks"),
            new HexNode("original", _container.Original.Length) { Title = "Original CSAV bytes" },
            new HexNode("payload", _edited.Length) { Title = "Expanded payload with fixed patches (point additions materialize on export)" }])]);

    public override IReadOnlyList<ViewNode> ExpandNode(string lazyId)
    {
        if (lazyId == "chunks")
        {
            return [ChunkTable()];
        }

        var parts = lazyId.Split('/');
        if (parts.Length == 2 && parts[0] == "node" && int.TryParse(parts[1], out var nodeId) &&
            nodeId >= 0 && nodeId < _tree.Nodes.Count)
        {
            var node = _tree.Nodes[nodeId];
            return [new StatsNode([new Stat("Node ID", node.Id.ToString()), new Stat("Payload offset", $"0x{node.Offset:X}"),
                new Stat("Size", node.Size.ToString())]),
                new HexNode($"node/{node.Id}", node.Size) { Title = $"{node.Name} bytes (includes node ID)" },
                .. TreePage(node.Id, 0)];
        }

        if (parts.Length == 3 && int.TryParse(parts[1], out var id) && int.TryParse(parts[2], out var start) && start >= 0)
        {
            if (parts[0] == "tree" && id >= -1 && id < _tree.Nodes.Count)
            {
                return TreePage(id, start);
            }

            if (parts[0] == "inventory" && id == 0 && start <= _items.Count)
            {
                return _inventory.Page(start);
            }
            if (parts[0] == "item-parts" && _items.Any(item => item.NodeId == id)) return _inventory.Parts(id, start);
        }

        throw new KeyNotFoundException($"Unknown lazy node '{lazyId}'.");
    }

    public override IReadOnlyList<FileOutput> Serialize()
    {
        var bytes = _container.Rebuild(_edited, _progression?.Rebuild(_edited));
        if (_progressionEditor is not null)
        {
            var reopened = CsavContainer.Parse(bytes);
            _progressionEditor.Verify(reopened, CsavTree.Parse(reopened));
        }

        return [new FileOutput(_path, bytes)];
    }

    public IReadOnlyList<QueryableCollection> Collections => [_inventory.Metadata, _facts.Metadata, .. _inspectionTables.Values.Select(table => table.Metadata)];

    public QueryableCollectionPage Query(string collectionId, QueryableCollectionQuery query) => collectionId switch
    {
        InventoryInspection.CollectionId => _inventory.Query(query),
        FactInspection.CollectionId => _facts.Query(query),
        _ => _inspectionTables.TryGetValue(collectionId, out var table) ? table.Query(query)
            : throw new KeyNotFoundException($"Unknown collection '{collectionId}'."),
    };

    public override AssetContent? GetAsset(string assetId) => _references.GetAsset(assetId);

    public override ActionOutcome InvokeAction(string actionId, IReadOnlyDictionary<string, FieldValue> parameters) =>
        _references.IsInspection(actionId) ? _references.Inspect(actionId, parameters) : base.InvokeAction(actionId, parameters);

    private FieldNode ReadField(string id, string label, FieldSpec spec, string value) => Field(id, label, spec, () => FieldValue.Of(value));

    public override EditOutcome ApplyEdits(IReadOnlyList<ValueEdit> edits)
    {
        var before = _progressionEditor?.DerivedValues();
        var outcome = base.ApplyEdits(edits);
        if (!outcome.Succeeded || before is null)
        {
            return outcome;
        }

        var after = _progressionEditor!.DerivedValues();
        return outcome with { Changes = [.. outcome.Changes, .. before.Where(p => p.Value != after[p.Key])
            .Select(p => new ValueChange(p.Key, p.Value, after[p.Key]))] };
    }

    public override ReadOnlyMemory<byte> ReadBlob(string blobId, long offset, int count)
    {
        ReadOnlyMemory<byte> data = blobId switch
        {
            "original" => _container.Original,
            "payload" => _edited,
            _ => NodeBlob(blobId),
        };
        if (offset < 0 || offset > data.Length || count < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(offset));
        }

        return data.Slice((int)offset, Math.Min(count, data.Length - (int)offset)).ToArray();
    }

    private ReadOnlyMemory<byte> NodeBlob(string id)
    {
        if (id.StartsWith("node/", StringComparison.Ordinal) && int.TryParse(id.AsSpan(5), out var index) && index >= 0 && index < _tree.Nodes.Count)
        {
            var node = _tree.Nodes[index];
            return _edited.AsMemory(node.Offset, node.Size);
        }

        throw new KeyNotFoundException($"Unknown blob '{id}'.");
    }

    private FieldNode QuantityField(InventoryItem item)
    {
        var offset = item.QuantityOffset!.Value;
        var writable = IsWritable(item);
        var maximum = Math.Max(item == _money ? MaximumMoney : MaximumStack, BinaryData.UInt32(_container.Payload, offset));
        Action<FieldValue>? setter = null;
        Func<FieldValue, string?>? validate = null;
        if (writable)
        {
            setter = value => BinaryPrimitives.WriteUInt32LittleEndian(_edited.AsSpan(offset), value.AsUInt32());
            validate = value => value.Kind != FieldValueKind.Integer || value.AsInteger() < 0 || value.AsInteger() > maximum
                ? "Quantity is outside the editor's supported range." : null;
        }

        return Field($"inventory/{item.NodeId}/quantity", item.TweakId == InventoryReader.MoneyTweakId ? "Money (Items.money)" : "Quantity",
            writable ? IntegerSpec.UInt32 with { Max = maximum } : IntegerSpec.UInt32,
            () => FieldValue.Of((ulong)BinaryData.UInt32(_edited, offset)), setter, validate)
        with {
            Confidence = Confidence.Verified,
            Description = item == _money ? CyberpunkReferences.BaselineAttribution : null,
            Warning = writable ? "Experimental PS5 write; the editor cap is a policy, not a verified game limit." : null,
        };
    }

    private bool IsWritable(InventoryItem item) => item == _money || item.ContainerId == 1 &&
        item.QuantityOffset.HasValue && _tree.Nodes[item.NodeId].Size == 29 && item.Structure == 0 &&
        _names.Find(item.TweakId)?.Kind is "Ammo" or "Component" or "Consumable";

    private IReadOnlyList<ViewNode> TreePage(int parent, int start)
    {
        var ids = parent < 0 ? _tree.Roots : _tree.Nodes[parent].Children;
        if (start > ids.Count)
        {
            throw new KeyNotFoundException("Node page exceeds the child list.");
        }

        var nodes = ids.Skip(start).Take(PageSize).Select(id => (ViewNode)SectionNode.Lazy($"{id}: {_tree.Nodes[id].Name}", $"node/{id}", _tree.Nodes[id].Size.ToString())).ToList();
        if (start + PageSize < ids.Count)
        {
            nodes.AddRange(NextPage("Nodes", $"tree/{parent}", start, ids.Count));
        }

        return nodes;
    }

    private static IEnumerable<ViewNode> NextPage(string title, string prefix, int start, int count) =>
        start + PageSize < count ? [SectionNode.Lazy($"{title}: next {PageSize}", $"{prefix}/{start + PageSize}")] : [];

    private TableNode ChunkTable() => new([new("index", "Chunk"), new("offset", "File offset"), new("stored", "Stored bytes"), new("expanded", "Expanded bytes")],
        _container.Chunks.Select((chunk, index) => new TableRow(index.ToString(), new Dictionary<string, ViewNode>
        {
            ["index"] = new TextNode(index.ToString()), ["offset"] = new TextNode($"0x{chunk.Offset:X}"),
            ["stored"] = new TextNode(chunk.StoredSize.ToString()), ["expanded"] = new TextNode(chunk.ExpandedSize.ToString()),
        })).ToArray());
}
