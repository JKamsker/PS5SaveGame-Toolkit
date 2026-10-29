namespace SaveToolkit.Plugins.UnrealGvas.Hogwarts;

/// <summary>Read-only interpretations; no reward, player ownership or cross-image merge is inferred.</summary>
internal static class HogwartsDiscoveryFields
{
    public const string EditorSource = HogwartsInventorySlots.SourceUrl;
    public const string FinderSource = "https://github.com/Malin001/Legilimens-Hogwarts-Legacy-Collectible-Finder/blob/02525dde0fd9bc0be72982478680377dccb41246/legilimens.py";
    public const string Attribution = "Storage interpretation: PS5 Save Game Toolkit contributors, GPL-3.0-or-later; " +
        "HLSGE contributors / falker47 (No licence published; reused with attribution per owner decision 2026-10-28), " +
        EditorSource + "; Malin001 / Legilimens (MIT), " + FinderSource + ". Accessed 2026-10-29. Independently implemented; no upstream code copied.";
    public const string Warning = "Read-only research. Conflicting collection states are retained; player ownership, image authority and reward dependencies are unverified.";
    private const string PageFlagPrefix = "PageCollected";

    public static string? Label(HogwartsTable table, IReadOnlyList<object?> row, string column)
    {
        if (table.Name == "MiscDataDynamic" && column == "DataValue")
        {
            string? name = Cell(table, row, "DataName") as string;
            if (name == "PerkPoints")
                return Cell(table, row, "DataOwner") is "Player0" ? "Unspent talent points (stored value)" : "PerkPoints (unmapped owner)";
            if (name?.StartsWith(PageFlagPrefix, StringComparison.Ordinal) == true)
                return "Field Guide page flag (stored value)";
        }
        return table.Name switch
        {
            "PerkDynamic" when column is "PerkID" or "Level" => column == "PerkID" ? "Learned talent ID" : "Learned talent level",
            "CollectionDynamic" when column is "CategoryID" or "SubcategoryID" or "ItemID" or "ItemState" or "UpdateTime" => column,
            "MapLocationDataDynamic" when column == "State" => "Map collection state (raw)",
            "AchievementDynamic" when Cell(table, row, "AchievementID") is string id && id.StartsWith("FGC_", StringComparison.Ordinal) => column,
            _ => null,
        };
    }

    public static string Description(HogwartsTable table, string label) =>
        $"{table.Image}/{table.Name}. {label}. " + (table.Name switch
        {
            "MiscDataDynamic" => "Owner/name are retained exactly; only Player0/PerkPoints is the observed talent-point tuple. Page flags do not establish challenge rewards. ",
            "PerkDynamic" => "No player column exists in the observed table. Level is stored, not a proven talent cost or applied effect. ",
            "CollectionDynamic" => "Category/subcategory/item tuples repeat with conflicting states. No latest-row, ownership, completion or reward rule is assumed. ",
            "AchievementDynamic" => "Field Guide counter/completion/delayed-reward storage is separate from collection ownership and claimed rewards. ",
            _ => "Raw map state; the source uses 11 for collection, but this is not a reward grant. ",
        }) + Attribution;

    private static object? Cell(HogwartsTable table, IReadOnlyList<object?> row, string column)
    {
        for (int index = 0; index < table.Columns.Count; index++)
            if (table.Columns[index] == column) return row[index + 1]; // Query prepends rowid.
        return null;
    }
}
