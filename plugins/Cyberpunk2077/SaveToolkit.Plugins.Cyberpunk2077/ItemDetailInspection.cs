using System.Globalization;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.Cyberpunk2077;

internal sealed class ItemDetailInspection(CsavContainer container, CsavTree tree, CyberpunkReferences references)
{
    private readonly Dictionary<int, (ExtendedItem? Item, string? Warning)> _cache = [];
    private readonly Dictionary<string, ItemPartInspection> _parts = new(StringComparer.Ordinal);

    public IReadOnlyList<QueryableCollection> PartCollections => _parts.Values.Select(parts => parts.Metadata).ToArray();
    public QueryableCollectionPage QueryParts(string collectionId, QueryableCollectionQuery query) =>
        _parts.TryGetValue(collectionId, out var parts) ? parts.Query(query) : throw new KeyNotFoundException($"Unknown part collection '{collectionId}'.");
    private (ExtendedItem? Item, string? Warning) Read(InventoryItem item)
    {
        if (_cache.TryGetValue(item.NodeId, out var cached)) return cached;
        var node = tree.Nodes[item.NodeId];
        try { cached = (ExtendedItemReader.Read(container.Payload.AsSpan(node.Offset, node.Size)), null); }
        catch (SaveFormatException ex) { cached = (null, ex.Message); }
        _cache.Add(item.NodeId, cached);
        return cached;
    }

    public string Flags(InventoryItem item)
    {
        var data = Read(item).Item;
        return data is null ? "Unknown" : $"0x{data.Flags:X2} · quest bit {(data.Flags & 1) != 0} · not-unequippable bit {(data.Flags & 2) != 0}";
    }

    public IReadOnlyList<ViewNode> Build(InventoryItem item)
    {
        var (data, warning) = Read(item);
        var identity = ItemIdentity.FromInventory(item, container, tree);
        if (data is null) return [new TextNode(warning!) { Tone = Tone.Warning }];
        var result = new List<ViewNode>
        {
            new TextNode(identity.ToString()),
            new TextNode($"Item flags: {Flags(item)}; creation time (raw) {data.CreationTime}. Unknown bits retained."),
            new TextNode(ExtendedItemReader.Source + " Read-only inferred semantics."),
        };
        if (data.Additional is { } extra)
        {
            result.Add(new TextNode($"Loot-pool ID 0x{extra.LootPool:X16}; opaque uint32 0x{extra.Unknown:X8}; raw required-level float {extra.RequiredLevel.ToString("R", CultureInfo.InvariantCulture)}. " +
                "Sentinel and derived gameplay requirements are unverified; this is not the displayed item tier."));
            result.Add(SectionNode.Lazy("Attachment / part tree", $"item-parts/{item.NodeId}/0", ExtendedItemReader.Parts(data.Root!).Count().ToString(CultureInfo.InvariantCulture)));
        }
        else result.Add(new TextNode("Quantity-only record; no extended part tree."));
        return result;
    }

    public IReadOnlyList<ViewNode> Parts(InventoryItem item)
    {
        var id = ItemPartInspection.IdFor(item.NodeId);
        if (!_parts.TryGetValue(id, out var parts))
        {
            var root = Read(item).Item?.Root ?? throw new KeyNotFoundException("No decoded item part tree.");
            parts = new ItemPartInspection(item.NodeId, root, references);
            _parts.Add(id, parts);
        }
        return [parts.View()];
    }
}
