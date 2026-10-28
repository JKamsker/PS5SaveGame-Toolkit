using System.Globalization;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.Cyberpunk2077;

internal sealed class ItemDetailInspection(CsavContainer container, CsavTree tree, CyberpunkReferences references)
{
    private const int PageSize = 128;
    private readonly Dictionary<int, (ExtendedItem? Item, string? Warning)> _cache = [];
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

    public IReadOnlyList<ViewNode> Parts(InventoryItem item, int start)
    {
        var root = Read(item).Item?.Root ?? throw new KeyNotFoundException("No decoded item part tree.");
        var parts = ExtendedItemReader.Parts(root).ToArray();
        if (start < 0 || start > parts.Length) throw new KeyNotFoundException("Part page exceeds the decoded tree.");
        var rows = parts.Skip(start).Take(PageSize).Select(pair =>
        {
            var part = pair.Part;
            var name = references.ItemDisplayName(part.Identity.TweakId) ?? $"0x{part.Identity.TweakId:X16}";
            var attribution = references.ItemAttribution(part.Identity.TweakId);
            if (attribution.Length == 0) attribution = "Unknown part record; raw identity is preserved exactly.";
            return new TableRow($"item-parts/{item.NodeId}/{pair.Path}", new Dictionary<string, ViewNode>
            {
                ["path"] = new TextNode(pair.Path), ["item"] = new SectionNode(name, [new TextNode(attribution)])
                    { Description = attribution },
                ["identity"] = new TextNode(part.Identity.ToString()), ["appearance"] = new TextNode(part.Appearance),
                ["slot"] = new TextNode($"0x{part.AttachmentSlot:X16}"),
                ["extra"] = new TextNode($"Opaque uint32 0x{part.Unknown:X8}; loot 0x{part.Additional.LootPool:X16}; " +
                    $"additional 0x{part.Additional.Unknown:X8}; raw level {part.Additional.RequiredLevel.ToString("R", CultureInfo.InvariantCulture)}"),
            });
        }).ToArray();
        return [new TableNode([new("path", "Part path"), new("item", "Record identifier"), new("identity", "Identity"),
            new("appearance", "Appearance string"), new("slot", "Attachment slot ID"), new("extra", "Raw extra data")], rows)
            { Description = ExtendedItemReader.Source + " Inferred attachment-tree schema; parts are not independent inventory instances." },
            .. (start + PageSize < parts.Length ? new ViewNode[] { SectionNode.Lazy("Next part page", $"item-parts/{item.NodeId}/{start + PageSize}") } : [])];
    }
}
