using System.Text;

namespace SaveToolkit.Plugins.AtomicHeart;

/// <summary>Identifier formatting only: never claims to be localized game text.</summary>
internal static class InventoryLabels
{
    internal static string Name(string id)
    {
        if (id is "" or "None") return "Empty slot";
        var text = id.StartsWith("DA_", StringComparison.Ordinal) ? id[3..] : id;
        if (text.StartsWith("Item_", StringComparison.Ordinal)) text = text[5..];
        var result = new StringBuilder();
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c == '_') { result.Append(' '); continue; }
            if (i > 0 && char.IsUpper(c) && char.IsLower(text[i - 1])) result.Append(' ');
            result.Append(c);
        }
        return result.ToString();
    }

    internal static string Category(InventoryItem item, InventoryState state)
    {
        var asset = state.Asset(item);
        return Category(item, asset, state.Catalog?.Find(InventoryState.ShortId(asset))?.Category);
    }

    internal static string Category(InventoryItem item, string asset, string? catalogCategory)
    {
        var id = InventoryState.ShortId(asset);
        if (id is "" or "None") return "empty";
        if (catalogCategory is not null) return catalogCategory;
        if (asset.Contains("/Consumables/", StringComparison.Ordinal)) return "consumable (inferred)";
        return item.Container[(item.Container.LastIndexOf('/') + 1)..] switch {
            "ResourcesList" => "resource (inferred)", "SpecialResourcesList" => "special resource (inferred)",
            "AmmoList" => "ammunition (inferred)", "AuxList" => "auxiliary (inferred)",
            "QuestItemsList" => "quest item (inferred)", _ => "unknown",
        };
    }
}
