using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.XorJson;

internal static class CharacterViews
{
    public static IReadOnlyList<Tab> Build(JsonValueToken root, Func<JsonValueToken, ViewNode> view)
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
        var known = groups.SelectMany(g => g.Members).ToHashSet(StringComparer.Ordinal);
        tabs.Add(new("Raw / Unknown", root.Children.Where(n => n != hero).Concat(hero.Children.Where(n => !known.Contains(n.Label))).Select(view).ToArray()));
        return tabs;
    }

    public static string? CatalogFor(JsonValueToken node)
    {
        if (node.Scalar.Kind != FieldValueKind.Text) return null;
        var value = node.Scalar.AsString();
        if (value.StartsWith("SW.Item.", StringComparison.Ordinal) && node.Label == "TypeTag") return "xorjson-items";
        if (value.StartsWith("SW.Effect.", StringComparison.Ordinal)) return "xorjson-effects";
        return value.StartsWith("SW.MinecartStation.", StringComparison.Ordinal) ||
            value.StartsWith("SW.Doorway.", StringComparison.Ordinal) || value.StartsWith("SW.Area.", StringComparison.Ordinal)
                ? "xorjson-world" : null;
    }
}
