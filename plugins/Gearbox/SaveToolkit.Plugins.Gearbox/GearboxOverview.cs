using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.Gearbox;

internal sealed class GearboxOverview(GearboxDocument document)
{
    private const uint MoneyHash = 618814354;
    private const uint EridiumHash = 3679636065;
    private const int MaximumCurrency = 2_000_000_000;
    private const int MaximumMoonOrbs = 16_000;
    private const int DefaultNameLimit = 32;
    private const int MaximumStoredNameLimit = 128;
    // inventory_path_hash of /Game/Gear/_Shared/_Design/InventoryCategories/InventoryCategory_GoldenKey.
    internal const uint GoldenKeyHash = 2420693941;

    public IReadOnlyList<ViewNode> Build()
    {
        List<ViewNode> nodes = [new TextNode(GearboxWriteWarnings.ConsoleValidation +
            " Experimental PS5 support. The game may clamp or remove edited content.") { Tone = Tone.Warning }];
        if (document.Profile) BuildProfile(nodes);
        else BuildCharacter(nodes);
        return nodes;
    }

    private void BuildCharacter(List<ViewNode> nodes)
    {
        var root = document.Root;
        nodes.Add(new StatsNode([
            new Stat("Inventory items", root.All("inventory_items").Count().ToString()),
            new Stat("Play time", TimeSpan.FromSeconds(root.Integer("time_played_seconds")).ToString()),
        ]));
        var nameLimit = root.Integer("name_character_limit");
        nodes.Add(document.Text("character/name", "Character name", () => document.Root, "preferred_character_name", true,
            nameLimit is > 0 and <= MaximumStoredNameLimit ? (int)nameLimit : DefaultNameLimit));
        nodes.Add(document.World.Label("character/class", "Class", root.Child("player_class_data")?.Text("player_class_path") ?? string.Empty));
        if (root.Schema.Wonderlands && root.Child("ability_data")?.Child("dual_class_save_data") is not null)
        {
            Func<ProtoMessage> dual = () => document.Root.Child("ability_data")!.Child("dual_class_save_data")!;
            nodes.Add(document.World.Label("character/primary-class", "Primary class branch", dual().Text("primary_branch_path")));
            nodes.Add(document.World.Label("character/secondary-class", "Secondary class branch", dual().Text("slotted_secondary_branch_path")));
        }
        nodes.Add(document.Level());
        nodes.Add(document.Experience());
        if (root.Child("ability_data") is not null)
        {
            Func<ProtoMessage> ability = () => document.Root.Child("ability_data")!;
            nodes.Add(document.Integer("character/skill-points", "Unspent skill points", ability, "ability_points", warning: "Spent points are preserved; levelling down does not refund them."));
            nodes.Add(document.Integer("character/tree-grade", "Skill tree grade", ability, "tree_grade", warning: "Advanced progression value; keep the existing value unless repairing a level-one character."));
        }
        var statIndex = root.Children("game_stats_data").ToList().FindIndex(s => s.Text("stat_path") == CharacterProgression.LevelStat(root.Schema.Wonderlands));
        if (statIndex >= 0)
            nodes.Add(document.Integer("character/recorded-level", "Recorded level statistic", () => document.Root.Children("game_stats_data").ElementAt(statIndex), "stat_value",
                warning: "Updated with XP; this statistic alone does not change level."));
        nodes.AddRange(Currencies("inventory_category_list", "currency", false));
    }

    private void BuildProfile(List<ViewNode> nodes)
    {
        nodes.Add(new StatsNode([
            new Stat("Bank items", document.Root.All("bank_inventory_list").Count().ToString()),
            new Stat("Lost loot items", document.Root.All("lost_loot_inventory_list").Count().ToString()),
        ]));
        if (!document.Root.Schema.Wonderlands && document.Root.Child("guardian_rank") is not null)
        {
            Func<ProtoMessage> rank = () => document.Root.Child("guardian_rank")!;
            nodes.Add(document.Integer("profile/guardian-rank", "Guardian rank", rank, "guardian_rank", writable: false));
            nodes.Add(document.Integer("profile/guardian-xp", "Guardian experience", rank, "new_guardian_experience", long.MaxValue, false));
        }
        if (document.Root.Schema.Wonderlands && document.Root.Child("player_prestige") is not null)
            nodes.Add(document.Integer("profile/myth-xp", "Myth experience", () => document.Root.Child("player_prestige")!, "prestige_experience", long.MaxValue, false));
        nodes.AddRange(Currencies("bank_inventory_category_list", "keys", true));
    }

    private IEnumerable<ViewNode> Currencies(string list, string prefix, bool profile)
    {
        uint[] hashes = profile ? [GoldenKeyHash] : [MoneyHash, EridiumHash];
        foreach (var hash in hashes)
        {
            var label = (profile, hash) switch
            {
                (false, MoneyHash) => "Money", (false, EridiumHash) => document.Root.Schema.Wonderlands ? "Moon orbs" : "Eridium",
                (true, GoldenKeyHash) => document.Root.Schema.Wonderlands ? "Skeleton keys" : "Golden keys", _ => null,
            };
            if (label is null) continue;
            var max = hash == EridiumHash && document.Root.Schema.Wonderlands ? MaximumMoonOrbs : MaximumCurrency;
            yield return document.Category(list, hash, label, max, $"{prefix}/{hash}");
        }
    }
}
