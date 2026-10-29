namespace SaveToolkit.Plugins.Gearbox;

/// <summary>Altered C# adaptation of pinned zlib __init__.py SDU definitions; capacities are editor policies.</summary>
// Copyright (c) 2020-2022 CJ Kucera, Abram Hindle. Retained zlib notices: Notices/*-COPYING.txt.
internal static class GearboxWritePolicies
{
    private const string SduPrefix = "/Game/Pickups/SDU/SDU_";
    public static int? SduMaximum(string path, bool wonderlands)
    {
        if (!path.StartsWith(SduPrefix, StringComparison.Ordinal)) return null;
        var name = path[SduPrefix.Length..].Split('.')[0];
        if (path != SduPrefix + name + ".SDU_" + name) return null;
        return name switch
        {
            "Backpack" or "SniperRifle" or "Heavy" => 13,
            "AssaultRifle" or "Pistol" or "Shotgun" or "Grenade" or "SMG" => 10,
            "Bank" => wonderlands ? 23 : 28,
            "LostLoot" => wonderlands ? 8 : 10,
            _ => null,
        };
    }

    public static long Capacity(string list, long tier) => list switch
    {
        GearboxInventory.Backpack => tier <= 8 ? 16 + 3 * tier : 40 + 2 * (tier - 8),
        GearboxInventory.LostLoot => 6 + tier,
        GearboxInventory.Bank => tier <= 5 ? 10 + 5 * tier : 40 + 20 * (tier - 6),
        _ => 0,
    };

    public static string? StorageList(string path) => path.Contains("SDU_Backpack.", StringComparison.Ordinal)
        ? GearboxInventory.Backpack
        : path.Contains("SDU_Bank.", StringComparison.Ordinal)
            ? GearboxInventory.Bank
            : path.Contains("SDU_LostLoot.", StringComparison.Ordinal)
                ? GearboxInventory.LostLoot
                : null;
}
