using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.UnrealGvas.Hogwarts;

/// <summary>The vacant inventory shape observed in all 30 supplied PS5 database images.</summary>
internal static class HogwartsInventorySlots
{
    internal const string SourceUrl = "https://github.com/falker47/HogwartsLegacy-SaveEditor/blob/349c3f0b75e4d158e5b7d7920bded999b518de36/HLSE-src/src/client/resources/saveGameDB.ts";
    internal const string Evidence = "Empty-slot presentation: PS5 Save Game Toolkit contributors, GPL-3.0-or-later; observed NULL item/variation and integer zero count in all 30 supplied PS5 images. HLSGE contributors / falker47 inventory-query research: " + SourceUrl + ". No licence published; reused with attribution per owner decision 2026-10-28. No upstream implementation copied; gameplay meaning remains Inferred.";
    internal const string CountColumn = "Count";
    private const string InventoryTable = "InventoryDynamic";
    private const string ItemColumn = "ItemID";
    private const string VariationColumn = "Variation";
    private static readonly string[] RequiredColumns = ["CharacterID", "HolderID", "SlotNumber", ItemColumn, VariationColumn, CountColumn];

    public static EmptyRowPresentation? Presentation(HogwartsTable table) => Supports(table)
        ? new EmptyRowPresentation(ItemColumn) : null;

    public static string? Predicate(HogwartsTable table) => Supports(table)
        ? $"\"{ItemColumn}\" IS NULL AND \"{VariationColumn}\" IS NULL AND typeof(\"{CountColumn}\") = 'integer' AND \"{CountColumn}\" = 0"
        : null;

    public static bool IsEmpty(HogwartsTable table, IReadOnlyList<object?> row) => Supports(table) &&
        Cell(table, row, ItemColumn) is null && Cell(table, row, VariationColumn) is null &&
        Cell(table, row, CountColumn) is 0L;

    public static bool IsCountField(HogwartsTable table, string valueId) => Supports(table) &&
        valueId.StartsWith(table.Id + "/", StringComparison.Ordinal) &&
        valueId.EndsWith("/" + CountColumn, StringComparison.Ordinal);

    private static bool Supports(HogwartsTable table) => table.Name == InventoryTable &&
        RequiredColumns.All(column => table.Columns.Contains(column, StringComparer.Ordinal));

    private static object? Cell(HogwartsTable table, IReadOnlyList<object?> row, string column) =>
        row[table.Columns.ToList().IndexOf(column) + 1];
}
