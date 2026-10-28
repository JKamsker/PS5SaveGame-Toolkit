using System.Globalization;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.Cyberpunk2077;

internal sealed class DevelopmentInspection
{
    public const string CollectionId = "development";
    private readonly PerkGraphInspection _graph;
    public DevelopmentInspection(ScriptableReader? reader, string? warning, ProgressionData? progression = null)
    {
        var rows = new List<InspectionRow>();
        try
        {
            if (reader is not null) Read(reader, rows);
        }
        catch (SaveToolkit.Sdk.SaveFormatException ex) { rows.Clear(); warning = ex.Message; }
        Warning = warning;
        _graph = new PerkGraphInspection(rows, warning);
        Table = new InspectionTable(CollectionId, "Skills, perks and budgets (read-only)", [new("kind", "Record kind"),
            new("group", "Attribute / area"), new("name", "Stored identifier"), new("rank", "Level / rank", IntegerSpec.Int32),
            new("xp", "Current XP", IntegerSpec.Int32), new("spent", "Spent points", IntegerSpec.Int32), new("unspent", "Unspent points", IntegerSpec.Int32),
            new("status", "Stored state / evidence")], rows,
            "All stored proficiencies, modern perk groups, legacy perk areas and point budgets. Omitted enum properties use the researched zero default. " +
            "Legacy area unlock bits are not post-2.0 perk allocations. Stored ranks never establish prerequisites, effects, Relic entitlement or legal budgets. " +
            "Raw Level/Street Cred values can be edited in Player development with warnings; no automatic rewards, perk allocation or respec.", (row, column) =>
            {
                if (column == "unspent" && row.Values["kind"] == "Point budget" &&
                    progression?.Points.SingleOrDefault(point => point.Type == row.Values["group"]) is { } balance)
                    return balance.Value.ToString(CultureInfo.InvariantCulture);
                if (row.Values["kind"] == "Proficiency" && progression?.Proficiencies.SingleOrDefault(p => p.Type == row.Values["name"]) is { } proficiency)
                    return column switch
                    {
                        "rank" => proficiency.Level.ToString(CultureInfo.InvariantCulture),
                        "xp" => proficiency.Experience.ToString(CultureInfo.InvariantCulture),
                        "status" => $"Stored threshold {proficiency.Threshold}; rewards and transitions unknown.",
                        _ => row.Values.GetValueOrDefault(column, ""),
                    };
                return row.Values.GetValueOrDefault(column, "");
            });
    }
    public string? Warning { get; }
    public InspectionTable Table { get; }
    public IReadOnlyList<ViewNode> Build() => [new TextNode(Warning ?? Table.Metadata.Description!)
        { Tone = Warning is null ? Tone.Info : Tone.Warning }, .. _graph.Build(), Table.View()];

    public IReadOnlyList<ViewNode> Expand(string id, Func<string, string, FieldSpec, string, FieldNode> readField) => _graph.Expand(id, readField);

    private static void Read(ScriptableReader reader, List<InspectionRow> rows)
    {
        var player = reader.Player("PlayerDevelopmentSystem", "playerData", "PlayerDevelopmentData");
        void Add(string key, string kind, string group, string name, string rank = "", string xp = "", string spent = "", string status = "", string unspent = "") =>
            rows.Add(new InspectionRow("development/" + key, new Dictionary<string, string>
            { ["kind"] = kind, ["group"] = group, ["name"] = name, ["rank"] = rank, ["xp"] = xp, ["spent"] = spent, ["unspent"] = unspent, ["status"] = status }));
        static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);
        var index = 0;
        foreach (var proficiency in player.StructArray(player.Require("proficiencies", "array:SProficiency")))
        {
            var name = proficiency.Enum("type", "gamedataProficiencyType", "Assault");
            Add($"skill/{index++}", "Proficiency", "", name, Number(proficiency.Int("currentLevel")), Number(proficiency.Int("currentExp")),
                status: $"Stored threshold {proficiency.Int("expToLevel")}; rewards and transitions unknown.");
        }
        index = 0;
        foreach (var point in player.StructArray(player.Require("devPoints", "array:SDevelopmentPoints")))
        {
            var name = point.Enum("type", "gamedataDevelopmentPointType", "Attribute");
            Add($"budget/{index++}", "Point budget", name, name, spent: Number(point.Int("spent")), unspent: Number(point.Int("unspent")), status: "Stored point balances; no budget/prerequisite validation.");
        }
        index = 0;
        foreach (var group in reader.Array(player, "attributesData", "SAttributeData"))
        {
            var name = group.Enum("type", "gamedataAttributeDataType", "BodyAttributeData");
            var perks = reader.Array(group, "unlockedPerks", "SNewPerk");
            var groupIndex = index++;
            Add($"modern/{groupIndex}", "Modern perk group", name, name, status: $"{perks.Count} stored perk records; omitted array means none stored.");
            for (var i = 0; i < perks.Count; i++) Add($"modern/{groupIndex}/{i}", "Modern perk", name,
                perks[i].Enum("type", "gamedataNewPerkType", "Body_Central_Milestone_1"), Number(perks[i].Int("currLevel")),
                status: "Stored rank; allocation effects, prerequisites and rank maximum unverified.");
        }
        index = 0;
        foreach (var area in reader.Array(player, "perkAreas", "SPerkArea"))
        {
            var name = area.Enum("type", "gamedataPerkArea", "Assault_Area_01");
            var areaIndex = index++;
            var unlocked = ScriptableReader.Byte(area, "unlocked", "Bool");
            if (unlocked > 1) throw new SaveToolkit.Sdk.SaveFormatException("Invalid perk-area Boolean.");
            Add($"legacy/{areaIndex}", "Legacy perk area", name, name, status: $"Stored unlocked bit {unlocked}; does not establish modern allocation.");
            var perks = reader.Array(area, "boughtPerks", "SPerk");
            for (var i = 0; i < perks.Count; i++) Add($"legacy/{areaIndex}/{i}", "Legacy perk", name,
                perks[i].Enum("type", "gamedataPerkType", "Assault_Area_01_Perk_1"), Number(perks[i].Int("currLevel")), status: "Legacy stored rank; active effect unknown.");
        }
    }
}
