using SaveToolkit.Sdk;

namespace SaveToolkit.Plugins.Witcher3;

/// <summary>Independent optional projections; unsupported data stays available in the raw explorer.</summary>
internal sealed class SavedWorldData
{
    public IReadOnlyList<SavedRecipe>? Recipes { get; }
    public IReadOnlyList<SavedFact>? Facts { get; }
    public IReadOnlyList<SavedJournalEntry>? Journal { get; }
    public IReadOnlyList<string> Warnings { get; }

    public SavedWorldData(PlayerData? player, Sav3Tree tree, byte[] data, IReadOnlyList<string> names)
    {
        var warnings = new List<string>();
        try { if (player?.Entity is { } entity) { Recipes = RecipeReader.Read(entity, tree, data, names); } }
        catch (SaveFormatException ex) { warnings.Add($"Recipe reader unavailable: {ex.Message}"); }
        try { Facts = FactsReader.Read(tree, data); }
        catch (SaveFormatException ex) { warnings.Add($"Facts reader unavailable: {ex.Message}"); }
        try { Journal = JournalReader.Read(tree, data, names); }
        catch (SaveFormatException ex) { warnings.Add($"Journal reader unavailable: {ex.Message}"); }
        Warnings = warnings;
    }
}
