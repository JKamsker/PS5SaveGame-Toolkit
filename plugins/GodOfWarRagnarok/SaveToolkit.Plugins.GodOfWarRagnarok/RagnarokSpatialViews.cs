using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.GodOfWarRagnarok;

/// <summary>Reference diagrams, never inferred saved unlocks or geographic coordinates.</summary>
internal static class RagnarokSpatialViews
{
    internal static readonly (string Id, string Name)[] Weapons =
        [("axe", "Leviathan Axe"), ("blades", "Blades of Chaos"), ("spear", "Draupnir Spear")];
    private static readonly string[] Realms =
        ["Midgard", "Svartalfheim", "Alfheim", "Vanaheim", "Muspelheim", "Niflheim", "Helheim", "Jotunheim", "Asgard"];
    internal const string SkillWarning = "Reference only: saved skill hashes, unlocks, eligibility and XP ownership are Unknown. " +
        "Only explicitly sourced prerequisite arrows are shown; missing arrows/costs do not mean no requirement. Base-game reference, not Valhalla or NG+ rules.";
    private const string MapAttribution = "MapGenie community interactive maps; licence unverified, external link only. No art or coordinates imported.";

    public static IReadOnlyList<ViewNode> SkillRoots() => Weapons.Select(weapon =>
        (ViewNode)(SectionNode.Lazy(weapon.Name, $"skill-tree/{weapon.Id}") with { Description = SkillWarning })).ToArray();

    public static IReadOnlyList<ViewNode> SkillTree(string weapon, RagnarokSkillReferences references)
    {
        var records = references.Weapon(weapon);
        var name = Weapons.Single(w => w.Id == weapon).Name;
        var keys = records.Select(record => record.Key).ToHashSet(StringComparer.Ordinal);
        var edges = records.SelectMany(record => record.Attributes.GetValueOrDefault("prerequisites", "")
            .Split(';', StringSplitOptions.RemoveEmptyEntries).Where(keys.Contains)
            .Select(prerequisite => new GraphEdge(prerequisite, record.Key))).Distinct().ToArray();
        var graph = new GraphNode($"{name} · skill reference", records.Select(record => new GraphItem(record.Key, record.Name, GraphState.Unknown)
        {
            Branch = record.Attributes.GetValueOrDefault("branch", "Unknown"),
            Cost = Requirements(record),
            Description = $"{SkillWarning} {record.Description} {record.AttributionText}",
            Target = new(ViewTargetKind.Field, SkillValueId(record.Key))
            { Path = ["tab:Skills", $"section:skill-tree/{weapon}", $"section:skill-details/{weapon}"] },
        }).ToArray(), edges, $"{name}: {records.Count} reference skills in Technique, Ranged and Melee branches. {SkillWarning}");
        SpatialViewValidation.Validate(graph);
        return [graph, SectionNode.Lazy($"{name} · reference details", $"skill-details/{weapon}")];
    }

    public static TableNode SkillDetails(RagnarokDocument document, string weapon, RagnarokSkillReferences references) => new(
        [new("skill", "Skill reference"), new("requirements", "Sourced requirements"), new("source", "Attribution")],
        references.Weapon(weapon).Select(record => new TableRow(record.Key, new Dictionary<string, ViewNode>
        {
            ["skill"] = document.SkillReference(record),
            ["requirements"] = new TextNode(Requirements(record)),
            ["source"] = new TextNode(record.AttributionText),
        })).ToArray()) { Title = "Skill reference details", Description = SkillWarning };

    public static MapNode RealmMap()
    {
        var map = new MapNode("Nine realms · external guides", new(0, 0, 1, 1), Realms.Select(realm =>
            new MapMarker($"realm/{realm.ToLowerInvariant()}", realm, MapMarkerStatus.Unknown, "Realm guide")
            {
                Confidence = Confidence.Unknown,
                Description = "Reference destination only. Saved travel access, discovered locations and completion are Unknown. " +
                    "No geographic position or save-to-realm ID mapping is asserted.",
                ExternalLink = new($"https://godofwar.fandom.com/wiki/{realm}", $"{realm} wiki guide",
                    "God of War Wiki contributors; external link only. Linked pages may describe multiple games."),
                Target = new(ViewTargetKind.Field, $"realm-reference/{realm}")
                { Path = ["tab:Realm map", "section:realm-details"] },
            }).ToArray(), "Nine external realm guides. All save progress is Unknown; no map art or coordinates are embedded.")
        {
            CoordinateSpace = "External links only; no coordinate mapping",
            ExternalSource = new("https://mapgenie.io/god-of-war-ragnarok", "Open community interactive maps", MapAttribution),
        };
        SpatialViewValidation.Validate(map);
        return map;
    }

    public static TableNode RealmDetails(RagnarokDocument document) => new([new("realm", "Realm"), new("state", "Saved access / completion")],
        Realms.Select(realm => new TableRow($"realm/{realm.ToLowerInvariant()}", new Dictionary<string, ViewNode>
        {
            ["realm"] = document.RealmReference(realm), ["state"] = new TextNode("Unknown; no saved-state mapping"),
        })).ToArray()) { Title = "Realm reference destinations" };

    internal static string SkillValueId(string key) => $"skill-reference/{key}";

    private static string Requirements(ReferenceRecord record) =>
        $"XP: {record.Attributes.GetValueOrDefault("xp", "Unknown")}; weapon level: {record.Attributes.GetValueOrDefault("weaponLevel", "Unknown")}";
}
