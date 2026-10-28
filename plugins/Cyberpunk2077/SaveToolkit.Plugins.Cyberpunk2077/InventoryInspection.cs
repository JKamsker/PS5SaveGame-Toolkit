using System.Globalization;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.Cyberpunk2077;

internal sealed class InventoryInspection
{
    public const string CollectionId = "inventory";
    public const int EagerLimit = 128;
    private readonly IReadOnlyList<InventoryItem> _items;
    private readonly CsavContainer _container;
    private readonly CsavTree _tree;
    private readonly CyberpunkReferences _references;
    private readonly ItemNames _baseline;
    private readonly Func<InventoryItem, FieldNode> _quantity;
    private readonly Func<string, string, FieldSpec, string, FieldNode> _readField;
    private readonly InspectionCollection<InventoryItem> _collection;
    private readonly EquipmentData? _equipment;
    private readonly ItemDetailInspection _details;

    public InventoryInspection(IReadOnlyList<InventoryItem> items, CsavContainer container, CsavTree tree,
        CyberpunkReferences references, ItemNames baseline, byte[] edited, Func<InventoryItem, FieldNode> quantity,
        Func<string, string, FieldSpec, string, FieldNode> readField, EquipmentData? equipment = null)
    {
        _items = items;
        _container = container;
        _tree = tree;
        _references = references;
        _baseline = baseline;
        _quantity = quantity;
        _readField = readField;
        _equipment = equipment;
        _details = new ItemDetailInspection(container, tree, references);
        QueryableColumn[] columns = [new("node", "Instance / raw bytes"), new("container", "Container ID", IntegerSpec.UInt64),
            new("id", "Item name / identifier"), new("category", "Catalog category"), new("hash", "TweakDB ID"),
            new("quantity", "Quantity", IntegerSpec.UInt32), new("equipped", "Owner-1 loadout"), new("flags", "Stored item flags"), new("details", "Evidence and details")];
        _collection = new(new QueryableCollection(CollectionId, "Inventory", columns, items.Count)
        {
            Description = "Search covers every item and its full saved identity. Exact filters: container, category, id, hash, quantity, node. " +
                "Container 1 is the observed player inventory; other container ownership remains unknown. " +
                "Equipment joins require full instance identity. Extended bodies are inspected read-only; catalog categories never authorize edits.",
        }, items, item => item.NodeId.ToString(CultureInfo.InvariantCulture), new Dictionary<string, Func<InventoryItem, string>>
        {
            ["node"] = item => item.NodeId.ToString(CultureInfo.InvariantCulture),
            ["container"] = item => item.ContainerId.ToString(CultureInfo.InvariantCulture),
            ["id"] = Name,
            ["category"] = Category,
            ["hash"] = item => $"0x{item.TweakId:X16}",
            ["quantity"] = item => item.QuantityOffset is { } offset ? BinaryData.UInt32(edited, offset).ToString(CultureInfo.InvariantCulture) : "",
            ["equipped"] = EquipmentState,
            ["flags"] = _details.Flags,
            // Details are searchable without constructing rich cells or registering all fields.
            ["details"] = Identity,
        }, Row, item => CyberpunkReferences.ItemKey(item.TweakId) + " " +
            (_references.Item(item.TweakId)?.Attributes.GetValueOrDefault("internalName") ?? _references.Item(item.TweakId)?.Name ?? _baseline.Find(item.TweakId)?.Name));
    }

    public QueryableCollection Metadata => _collection.Metadata;
    public QueryableCollectionPage Query(QueryableCollectionQuery query) => _collection.Query(query);
    public IReadOnlyList<ViewNode> Parts(int nodeId, int start) => _details.Parts(_items.Single(i => i.NodeId == nodeId), start);

    public IReadOnlyList<ViewNode> Build() => _items.Count == 0
        ? [new TextNode("Inventory schema is unsupported or empty; inspect the raw inventory node. No item replacements are enabled.")]
        : [new TextNode(Metadata.Description!), _items.Count > EagerLimit ? _collection.View()
            : SectionNode.Lazy("Inventory items", "inventory/0/0", _items.Count.ToString(CultureInfo.InvariantCulture))];

    public IReadOnlyList<ViewNode> Page(int start) => [new TableNode(
        Metadata.Columns.Select(column => new TableColumn(column.Key, column.Label)).ToArray(),
        _items.Skip(start).Take(EagerLimit).Select(Row).ToArray()) { Description = Metadata.Description },
        .. (start + EagerLimit < _items.Count ? new ViewNode[] { SectionNode.Lazy("Next inventory page", $"inventory/0/{start + EagerLimit}") } : [])];

    private string Name(InventoryItem item) => _references.ItemDisplayName(item.TweakId) ?? _baseline.Find(item.TweakId)?.Name ?? $"0x{item.TweakId:X16}";
    private string EquipmentState(InventoryItem item) => _equipment?.State(item.NodeId) ?? "Unknown: equipment schema unavailable";
    private string Category(InventoryItem item) => _references.Item(item.TweakId)?.Category ?? _baseline.Find(item.TweakId)?.Kind ?? "Unknown";
    private string Identity(InventoryItem item) => Convert.ToHexString(_container.Payload.AsSpan(_tree.Nodes[item.NodeId].Offset + 4, 16));

    private TableRow Row(InventoryItem item)
    {
        var record = _references.Item(item.TweakId);
        var known = record is not null || _baseline.Find(item.TweakId) is not null || _references.ItemLabel(item.TweakId) is not null;
        var attribution = record is not null || _references.ItemLabel(item.TweakId) is not null ? _references.ItemAttribution(item.TweakId)
            : known ? CyberpunkReferences.BaselineAttribution : "Unknown identifier; preserved exactly.";
        var prefix = $"inventory/{item.NodeId}";
        var node = _tree.Nodes[item.NodeId];
        List<ViewNode> details = [
            _readField(prefix + "/record", "Catalog identity (read-only)", new ReferenceSpec("items") { AllowCustom = true }, CyberpunkReferences.ItemKey(item.TweakId))
                with { Confidence = known ? Confidence.Inferred : Confidence.Unknown, Description = attribution,
                    Warning = "Recognizing a base record does not prove instance tier, equipment state or replacement safety." },
            new TextNode($"Full 16-byte item identity: {Identity(item)}") { Monospace = true },
            new TextNode($"Structure {item.Structure}; itemData size {node.Size}. Extended data remains read-only. {EquipmentState(item)}."),
            .. _details.Build(item),
            new TextNode(record?.Description ?? "No licensed localized description supplied."),
        ];
        if (record?.AssetId is { } asset)
        {
            details.Add(new SectionNode("Reference image", [new ImageNode(asset, record.Name),
                new TextNode(_references.AssetAttribution(asset) ?? attribution)]) { Description = attribution });
        }
        else
        {
            details.Add(new TextNode("No licensed icon supplied."));
        }

        return new TableRow(item.NodeId.ToString(CultureInfo.InvariantCulture), new Dictionary<string, ViewNode>
        {
            ["node"] = SectionNode.Lazy($"itemData {item.NodeId}", $"node/{item.NodeId}"),
            ["container"] = new TextNode(item.ContainerId.ToString(CultureInfo.InvariantCulture)),
            ["id"] = _readField(prefix + "/name", "Item name / identifier", new TextSpec(), Name(item))
                with { Confidence = known ? Confidence.Inferred : Confidence.Unknown, Description = attribution },
            ["category"] = new SectionNode(Category(item), [new TextNode(attribution)]) { Description = attribution },
            ["hash"] = new TextNode($"0x{item.TweakId:X16}") { Monospace = true },
            ["quantity"] = item.QuantityOffset.HasValue ? _quantity(item) : new TextNode("No quantity field"),
            ["equipped"] = new SectionNode(EquipmentState(item), [new TextNode(ScriptableReader.Source)]),
            ["flags"] = new SectionNode(_details.Flags(item), [new TextNode(ExtendedItemReader.Source)]),
            ["details"] = new SectionNode("Inspect instance", details) { Collapsed = true, Description = attribution },
        });
    }
}
