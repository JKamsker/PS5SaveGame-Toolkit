using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.Witcher3;

internal sealed class PlayerViews(PlayerData? player, FieldBinding bind, Func<InventoryItem, FieldNode> quantity)
{
    public IReadOnlyList<ViewNode> Build()
    {
        if (player is null)
        {
            return [new TextNode("Player identity and level manager could not be established for this schema.")];
        }

        var progress = player.Progress;
        var crowns = player.Inventory?.Items.Where(i => i.Name == "Crowns").ToArray() ?? [];
        return [new StatsNode([new Stat("Level", progress.Level.ToString()),
                new Stat("Total XP", ((long)progress.Experience + progress.UsedExperience).ToString()),
                new Stat("Available ability points", progress.AbilityPoints.ToString())]),
            bind("player/level", "Character level", new IntegerSpec(1, progress.MaximumDefinedLevel),
                () => FieldValue.Of(progress.Level), v => progress.Level = v.AsInt32(), v => IntegerError(v, 1, progress.MaximumDefinedLevel)) with {
                Confidence = Confidence.Inferred, Description = "Player IdTag → Entity → levelManager → level. " + WriteFields.ConsoleWarning,
                Warning = "Changes the stored level only. XP, level rewards, skill slots and scaled stats are not recalculated." },
            bind("player/xp", "Unspent XP", new IntegerSpec(0, int.MaxValue - progress.UsedExperience),
                () => FieldValue.Of(progress.Experience), v => progress.Experience = v.AsInt32(), v => IntegerError(v, 0, int.MaxValue - progress.UsedExperience)) with {
                Confidence = Confidence.Inferred, Description = "levelManager.points[1].free; total XP includes used XP. " + WriteFields.ConsoleWarning,
                Warning = "Does not run the game's level-up logic. Keep XP consistent with the character level." },
            bind("player/abilityPoints", "Available ability points", new IntegerSpec(0, int.MaxValue - progress.UsedAbilityPoints),
                () => FieldValue.Of(progress.AbilityPoints), v => progress.AbilityPoints = v.AsInt32(), v => IntegerError(v, 0, int.MaxValue - progress.UsedAbilityPoints)) with {
                Confidence = Confidence.Inferred, Description = "levelManager.points[0].free; omitted defaults are decoded as zero. " + WriteFields.ConsoleWarning,
                Warning = WriteFields.ConsoleWarning + " Costs, spent points, prerequisites and level rewards are not recalculated; changes affect the stored free points only." },
            bind("player/usedXp", "Used XP", IntegerSpec.Int32, () => FieldValue.Of(progress.UsedExperience)) with { Confidence = Confidence.Inferred },
            bind("player/usedAbilityPoints", "Spent ability points", IntegerSpec.Int32, () => FieldValue.Of(progress.UsedAbilityPoints)) with { Confidence = Confidence.Inferred },
            .. (crowns.Length == 1 ? new ViewNode[] { quantity(crowns[0]) } : [new TextNode("No unique Crowns stack was decoded.")]),
            new TextNode("Crowns are money. Orens and Florens are separate inventory items if present. Values are inferred from stored names and published layouts."),
            new TextNode("Raw views show the input image; pending edits appear in curated fields.")];
    }

    private static string? IntegerError(FieldValue value, int minimum, int maximum) =>
        value.Kind != FieldValueKind.Integer || value.AsInteger() < minimum || value.AsInteger() > maximum
            ? $"Choose a whole value in {minimum}..{maximum}." : null;
}
