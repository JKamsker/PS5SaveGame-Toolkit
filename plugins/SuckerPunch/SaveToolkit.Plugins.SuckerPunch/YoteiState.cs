using SaveToolkit.Sdk;

namespace SaveToolkit.Plugins.SuckerPunch;

internal sealed record YoteiState(
    IReadOnlyList<YoteiRecord> Inventory,
    IReadOnlyList<YoteiRecord> Registry,
    IReadOnlyList<YoteiRecord> Statistics,
    IReadOnlyList<YoteiRecord> Gear,
    int InventoryStart,
    int RegistryStart)
{
    public static YoteiState Parse(ReadOnlySpan<byte> bytes, SaveContainer container)
    {
        var content = bytes[..(int)container.Word2];
        ReadOnlySpan<byte> inventoryAnchor = "\x30\0\0\0\x0b\0\0\0ArrowNormal"u8;
        var first = content.IndexOf(inventoryAnchor);
        if (first < 0) throw new SaveFormatException("Yōtei inventory table is missing.");
        var next = content[(first + inventoryAnchor.Length)..].IndexOf(inventoryAnchor);
        if (next < 0) throw new SaveFormatException("Yōtei secondary inventory table is missing.");
        var second = first + inventoryAnchor.Length + next;
        if (content[(second + inventoryAnchor.Length)..].IndexOf(inventoryAnchor) >= 0)
            throw new SaveFormatException("Yōtei inventory table is ambiguous.");
        var inventory = YoteiFormat.Table(content, first, YoteiFormat.InventoryCount, YoteiFormat.InventoryTrailer);
        var registry = YoteiFormat.Table(content, second, YoteiFormat.InventoryCount, YoteiFormat.RegistryTrailer);
        if (!inventory.Select(row => row.Name).SequenceEqual(registry.Select(row => row.Name)) ||
            inventory[^1].Name != "SurvivalCurrency" || !inventory.Any(row => row.Name == "Mon"))
            throw new SaveFormatException("Unrecognised Yōtei inventory layout.");
        if (inventory[^1].DataOffset + YoteiFormat.InventoryTrailer > second)
            throw new SaveFormatException("Yōtei inventory tables overlap.");
        var statsStart = YoteiFormat.FindUnique(content, "\x21\0\0\0\x0b\0\0\0FishSpeared"u8, "statistics table");
        var stats = YoteiFormat.Table(content, statsStart, YoteiFormat.StatisticsCount, sizeof(uint));
        var gearStart = YoteiFormat.FindUnique(content, "\x0d\x04\0\0\x0b\0\0\0HalfShog101"u8, "gear registry");
        var gear = YoteiFormat.Table(content, gearStart, YoteiFormat.GearCount, 2);
        if (stats[^1].Name != "CrTriggerMarkedPerkEffects" || gear[^1].Name != "SwordSwingFxOnryo" ||
            stats[^1].DataOffset + sizeof(uint) > gearStart || gear[^1].DataOffset + 2 > first)
            throw new SaveFormatException("Unrecognised Yōtei section boundaries.");
        return new YoteiState(inventory, registry, stats, gear, first, second);
    }
}
