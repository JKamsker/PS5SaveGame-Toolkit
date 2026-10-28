using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.XorJson;

internal static class CharacterViews
{
    public static IReadOnlyList<Tab> Build(JsonValueToken root, Func<JsonValueToken, ViewNode> view,
        IReadOnlyList<CharacterCollection> collections)
    {
        var hero = root.Member("CharacterSaveV1")!;
        var groups = new (string Title, string[] Members)[]
        {
            ("Hero", ["MetaData", "Ability"]), ("Inventory", ["Inventory"]),
            ("Quests", ["quest"]), ("World", ["WorldExploration"]),
            ("Collections", ["Achievements", "CollectionsStats"]),
        };
        var tabs = groups.Select(g => new Tab(g.Title,
            hero.Children.Where(n => g.Members.Contains(n.Label, StringComparer.Ordinal)).Select(view).ToArray())).ToList();
        var metadata = hero.Member("MetaData");
        tabs[0] = tabs[0] with { Children = [new StatsNode([
            new Stat("Stored Level", metadata?.Member("Level")?.Scalar.ToString() ?? "Unknown") { Hint = "CharacterSaveV1.MetaData.Level; gameplay limits unverified." },
            new Stat("Stored PowerLevel", metadata?.Member("PowerLevel")?.Scalar.ToString() ?? "Unknown") { Hint = "CharacterSaveV1.MetaData.PowerLevel; dependencies unverified." },
        ]), .. tabs[0].Children] };
        foreach (var collection in collections)
        {
            var index = tabs.FindIndex(tab => tab.Title == collection.TabTitle);
            tabs[index] = tabs[index] with { Children = [.. tabs[index].Children, collection.View()] };
        }
        var known = groups.SelectMany(g => g.Members).ToHashSet(StringComparer.Ordinal);
        tabs.Add(new("Raw / Unknown", root.Children.Where(n => n != hero).Concat(hero.Children.Where(n => !known.Contains(n.Label))).Select(view).ToArray()));
        return tabs;
    }

    public static string? CatalogFor(JsonValueToken node, FieldValue? scalar = null)
    {
        var current = scalar ?? node.Scalar;
        if (current.Kind == FieldValueKind.Union) current = current.AsUnion().Value;
        if (current.Kind != FieldValueKind.Text) return null;
        var value = current.AsString();
        if (node.Label == "AttributeName") return "xorjson-attributes";
        if (node.Label == "RarityTag") return "xorjson-rarities";
        if (node.Label == "FocusedQuestId") return "xorjson-world";
        if (node.Label == "TypeTag" && node.Parent?.Label == "ItemData") return "xorjson-items";
        if (node.Label is "LastMinecartStation" or "CurrentLocation") return "xorjson-world";
        if (value.StartsWith("SW.Item.", StringComparison.Ordinal) && node.Label == "TypeTag") return "xorjson-items";
        if (value.StartsWith("SW.Effect.", StringComparison.Ordinal)) return "xorjson-effects";
        if (value.StartsWith("SW.Enchantment.", StringComparison.Ordinal)) return "xorjson-enchantments";
        if (node.Label is "QuestName" or "TaskName") return "xorjson-world";
        return value.StartsWith("SW.MinecartStation.", StringComparison.Ordinal) ||
            value.StartsWith("SW.Doorway.", StringComparison.Ordinal) || value.StartsWith("SW.Area.", StringComparison.Ordinal) ||
            value.StartsWith("SW.Region.", StringComparison.Ordinal) || value.StartsWith("SW.UI.Cutscene.", StringComparison.Ordinal)
                ? "xorjson-world" : null;
    }
}
