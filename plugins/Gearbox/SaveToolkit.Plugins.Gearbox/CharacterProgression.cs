namespace SaveToolkit.Plugins.Gearbox;

internal static class CharacterProgression
{
    public static string LevelStat(bool wonderlands) => wonderlands
        ? "/Game/GameData/Stats/Progression/Stat_Character_Level.Stat_Character_Level"
        : "/Game/PlayerCharacters/_Shared/_Design/Stats/Character/Stat_Character_Level.Stat_Character_Level";

    public static void SetExperience(ProtoMessage root, int experience)
    {
        var schema = root.Schema;
        var oldLevel = schema.Level(checked((int)root.Integer("experience_points")));
        var newLevel = schema.Level(experience);
        var ability = root.Child("ability_data");
        if (schema.Wonderlands && newLevel > oldLevel && ability is not null)
        {
            var additional = Enumerable.Range(oldLevel + 1, newLevel - oldLevel).Sum(l => l == 20 ? 2 : l == 40 ? 3 : 1);
            ability.SetInteger("ability_points", checked((int)ability.Integer("ability_points") + additional));
        }
        if (!schema.Wonderlands && newLevel > 1 && ability?.Integer("tree_grade") == 0)
            ability.SetInteger("tree_grade", 1);
        root.SetInteger("experience_points", experience);
        foreach (var stat in root.Children("game_stats_data").Where(s => s.Text("stat_path") == LevelStat(schema.Wonderlands)))
            stat.SetInteger("stat_value", newLevel);
    }
}
