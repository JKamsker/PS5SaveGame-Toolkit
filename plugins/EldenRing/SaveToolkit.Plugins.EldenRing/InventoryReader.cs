using SaveToolkit.Sdk;

namespace SaveToolkit.Plugins.EldenRing;

internal sealed record InventoryEntry(string Container, string Group, int Record, int Offset,
    uint Handle, uint Quantity, uint InventoryIndex, uint? ItemId, string Category, string Resolution)
{
    public string Key(int slot) => $"slots/{slot}/{Container}/{Group}/{Record}/{Handle:X8}/{InventoryIndex}";
    public string RawId => ItemId is { } id ? $"0x{id:X8}" : $"handle:0x{Handle:X8}";
}

internal sealed record InventoryContainer(string Id, SaveRange Range, uint CommonCount, uint KeyCount,
    int Capacity, IReadOnlyList<InventoryEntry> Entries, IReadOnlyList<string> Warnings);

internal static class InventoryReader
{
    public static InventoryContainer Read(ref SlotCursor cursor, int absoluteStart, string container,
        int commonCapacity, int keyCapacity, IReadOnlyDictionary<uint, ItemInstance?> instances)
    {
        var start = cursor.Position;
        var entries = new List<InventoryEntry>();
        var warnings = new List<string>();
        var common = Group(ref cursor, absoluteStart, container, "common", commonCapacity, entries, instances, warnings);
        var key = Group(ref cursor, absoluteStart, container, "key", keyCapacity, entries, instances, warnings);
        cursor.Take(2 * sizeof(uint)); // next equipment/acquisition indices: preserved, never rewritten.
        return new InventoryContainer(container, new SaveRange(container, absoluteStart + start, cursor.Position - start),
            common, key, commonCapacity + keyCapacity, entries, warnings);
    }

    private static uint Group(ref SlotCursor cursor, int absoluteStart, string container, string group,
        int capacity, List<InventoryEntry> entries, IReadOnlyDictionary<uint, ItemInstance?> instances, List<string> warnings)
    {
        var declared = cursor.UInt32();
        if (declared > capacity)
            throw new SaveFormatException($"{container}/{group}: distinct count exceeds the fixed array capacity.");
        var occupied = 0;
        for (var record = 0; record < capacity; record++)
        {
            var offset = absoluteStart + cursor.Position;
            var handle = cursor.UInt32();
            var quantity = cursor.UInt32();
            var index = cursor.UInt32();
            if (handle == 0 && quantity == 0 && index == 0)
                continue;
            occupied++;
            var (id, category, resolution) = Resolve(handle, instances);
            entries.Add(new InventoryEntry(container, group, record, offset, handle, quantity, index, id, category, resolution));
        }
        if (declared != occupied)
            warnings.Add($"{container}/{group}: declared {declared}, observed {occupied} nonzero rows; residue is retained without repair.");
        return declared;
    }

    private static (uint? Id, string Category, string Resolution) Resolve(uint handle, IReadOnlyDictionary<uint, ItemInstance?> instances)
    {
        var type = handle & SemanticLayout.HandleMask;
        if (type == SemanticLayout.GoodsHandle)
            return ((handle & SemanticLayout.IdentityMask) | SemanticLayout.GoodsId, "goods", "direct");
        if (type == SemanticLayout.TalismanHandle)
            return ((handle & SemanticLayout.IdentityMask) | SemanticLayout.TalismanId, "talisman", "direct");
        if (type is not (SemanticLayout.WeaponHandle or SemanticLayout.ArmorHandle or SemanticLayout.AshHandle))
            return (null, "unknown", "unknown handle category/sentinel");
        if (!instances.TryGetValue(handle, out var instance) || instance is null)
            return (null, "unknown", "missing or ambiguous instance handle");
        var expected = type switch
        {
            SemanticLayout.ArmorHandle => EldenRingLayout.ArmorType,
            SemanticLayout.AshHandle => SemanticLayout.AshId,
            _ => 0U,
        };
        if ((instance.ItemId & EldenRingLayout.ItemTypeMask) != expected)
            return (null, "unknown", "instance category disagrees with handle");
        return (instance.ItemId, type == SemanticLayout.WeaponHandle ? "weapon" : type == SemanticLayout.ArmorHandle ? "armor" : "ash",
            "instance map");
    }
}
