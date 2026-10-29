namespace SaveToolkit.Plugins.EldenRing;

// Shared word layout for relationship checks and the independent raw representations.
internal static class EquipmentSlots
{
    public const int AssemblyPrefixWords = 7;
    public const int WeaponSlotCount = 10;
    public const int LegsWord = 15;
    private const int HandSlotCount = 6;
    private const int HeadWord = 12;
    private const int ChestWord = 13;
    private const int ArmsWord = 14;
    private const int FirstTalismanWord = 17;
    private const int LastTalismanWord = 20;

    public static string? GearLabel(int word) => word switch
    {
        < HandSlotCount => $"{(word % 2 == 0 ? "Left" : "Right")} hand {word / 2 + 1}",
        < WeaponSlotCount => $"{(word % 2 == 0 ? "Arrow" : "Bolt")} {(word - HandSlotCount) / 2 + 1}",
        HeadWord => "Head", ChestWord => "Chest", ArmsWord => "Arms", LegsWord => "Legs",
        >= FirstTalismanWord and <= LastTalismanWord => $"Talisman {word - FirstTalismanWord + 1}",
        _ => null,
    };
}
