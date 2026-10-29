using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SaveToolkit.Plugins.UnrealGvas.Hogwarts;

/// <summary>Observed schemas only. Named groups are navigation aids, not claims of safe game effects.</summary>
public sealed record HogwartsTable(string Image, string Name, IReadOnlyList<string> Columns,
    IReadOnlyList<string> IdentityColumns, long Count, string SchemaHash)
{
    public string Id => $"hogwarts/{Image}/{Name}";
    public string Group => Name switch
    {
        "MiscDataDynamic" or "CharactersDynamic" or "CharacterLoadoutsDynamic" or "SpellsDynamic" => "Character",
        "InventoryDynamic" or "GearItemsDynamic" or "EquippedGearDynamic" or "ForcedGearAppearanceDynamic" => "Inventory",
        "AchievementDynamic" or "PerkDynamic" or "LocksDynamic" or "MissionDynamic" or "StatsDynamic" or
            "PlayerStatsDynamic" or "RPGAbilityDynamic" => "Progression",
        "CollectionDynamic" or "MapLocationDataDynamic" or "SphinxPuzzleDynamic" or
            "LootDropComponentDynamic" or "EconomicExpiryDynamic" => "Collections",
        _ => "Database tables",
    };

    public string Title => Name switch
    {
        "MiscDataDynamic" => "Character, XP and miscellaneous values",
        "CharactersDynamic" => "Character records",
        "CharacterLoadoutsDynamic" => "Spell loadouts",
        "SpellsDynamic" => "Assigned spells",
        "InventoryDynamic" => "Inventory stacks and holders",
        "GearItemsDynamic" => "Gear instances and stats",
        "EquippedGearDynamic" => "Equipped gear by actor and slot",
        "ForcedGearAppearanceDynamic" => "Gear appearance overrides",
        "AchievementDynamic" => "Challenge counters and reward state",
        "PerkDynamic" => "Learned talents",
        "LocksDynamic" => "Locks and unlock state",
        "MissionDynamic" => "Mission records",
        "StatsDynamic" => "Persistent statistics",
        "PlayerStatsDynamic" => "Player activity records",
        "RPGAbilityDynamic" => "Ability instances",
        "CollectionDynamic" => "Collection entries and saved state",
        "MapLocationDataDynamic" => "Map locations and saved state",
        "SphinxPuzzleDynamic" => "Merlin trial state",
        "LootDropComponentDynamic" => "Loot groups",
        "EconomicExpiryDynamic" => "Expiry records",
        _ => Name,
    };

    // These tuples are confirmed against the PS5 columns, and checked for uniqueness in each image.
    public static IReadOnlyList<string> CandidateIdentity(string name) => name switch
    {
        "MiscDataDynamic" => ["DataOwner", "DataName"],
        "InventoryDynamic" => ["CharacterID", "HolderID", "SlotNumber"],
        "GearItemsDynamic" => ["GearID", "GearVariation"],
        "EquippedGearDynamic" => ["ActorID", "SlotID"],
        "ForcedGearAppearanceDynamic" => ["GearSlotType"],
        "CollectionDynamic" => ["CategoryID", "SubcategoryID", "ItemID"],
        "CharactersDynamic" => ["CharacterID"],
        "CharacterLoadoutsDynamic" => ["Name"],
        "SpellsDynamic" => ["LoadoutIndex"],
        "AchievementDynamic" => ["AchievementID"],
        "PerkDynamic" => ["PerkID"],
        "LocksDynamic" => ["LockID"],
        "MissionDynamic" => ["MissionID"],
        "StatsDynamic" => ["StatID"],
        "PlayerStatsDynamic" => ["ActivityType", "ActivityName", "CompanionName"],
        "RPGAbilityDynamic" => ["AbilityID"],
        "MapLocationDataDynamic" => ["MapLocationID"],
        "SphinxPuzzleDynamic" => ["SphinxPuzzleGUID"],
        "LootDropComponentDynamic" => ["UniqueID"],
        "EconomicExpiryDynamic" => ["UniqueID"],
        _ => [],
    };

    public string RowKey(IReadOnlyList<object?> row)
    {
        // Column zero is SQLite rowid. Unknown/duplicate logical keys retain rowid, which is stable
        // across existing-cell edits but is explicitly not a cross-generation semantic identity.
        if (IdentityColumns.Count == 0) return "rowid:" + Text(row[0]);
        var identity = IdentityColumns.Select(column => row[Columns.ToList().IndexOf(column) + 1]).ToArray();
        return "key:" + Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(identity)));
    }

    public static string Quote(string name) => "\"" + name.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";

    public static string Text(object? value) => value switch
    {
        null => "NULL",
        byte[] bytes => $"BLOB ({bytes.Length} bytes; SHA256 {Convert.ToHexStringLower(SHA256.HashData(bytes))})",
        double number => number.ToString("R", CultureInfo.InvariantCulture),
        IFormattable number => number.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? "",
    };

    public static string Hash(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
}
