namespace SaveToolkit.Plugins.EldenRing;

internal sealed record SemanticCharacter(int Slot, IReadOnlyList<InventoryContainer> Containers,
    IReadOnlyList<SaveRange> Ranges, IReadOnlyList<ItemInstance> Instances);

internal static class SemanticReader
{
    public static SemanticCharacter Read(ReadOnlySpan<byte> bytes, CharacterSlot slot)
    {
        var data = bytes.Slice(slot.Offset, EldenRingLayout.SlotSize);
        var map = ItemMapReader.Read(data, slot.Version);
        var instances = map.Resolve();
        var cursor = new SlotCursor(data, map.PlayerOffset);
        var ranges = new List<SaveRange>();
        Add(ref cursor, ranges, slot.Offset, "PlayerGameData", SemanticLayout.PlayerSize);
        cursor.Take(SemanticLayout.PlayerPadding);
        Add(ref cursor, ranges, slot.Offset, "EquipData", SemanticLayout.EquipmentSize);
        Add(ref cursor, ranges, slot.Offset, "ChrAsm", SemanticLayout.AssemblySize);
        Add(ref cursor, ranges, slot.Offset, "ChrAsm2", SemanticLayout.EquipmentSize);
        var held = InventoryReader.Read(ref cursor, slot.Offset, "held", SemanticLayout.HeldCommonCapacity,
            SemanticLayout.HeldKeyCapacity, instances);
        Add(ref cursor, ranges, slot.Offset, "Magic", SemanticLayout.MagicSize);
        Add(ref cursor, ranges, slot.Offset, "QuickItems", SemanticLayout.QuickItemsSize);
        cursor.Take(SemanticLayout.EquippedGesturesSize);
        var projectileCount = cursor.UInt32();
        cursor.SkipRecords(projectileCount, SemanticLayout.ProjectileSize);
        Add(ref cursor, ranges, slot.Offset, "EquippedItems", SemanticLayout.EquippedItemsSize);
        cursor.Take(SemanticLayout.PhysicsSize + sizeof(uint) + SemanticLayout.FaceSize);
        var storage = InventoryReader.Read(ref cursor, slot.Offset, "storage", SemanticLayout.StorageCommonCapacity,
            SemanticLayout.StorageKeyCapacity, instances);
        cursor.Take(SemanticLayout.GesturesSize);
        var regions = cursor.UInt32();
        cursor.SkipRecords(regions, sizeof(uint));
        cursor.Take(SemanticLayout.RideSize + SemanticLayout.RegionTailSize);
        var acquisitionStart = cursor.Position;
        var acquisitionCount = cursor.UInt32();
        if (acquisitionCount > SemanticLayout.AcquisitionCapacity)
            throw new SaveToolkit.Sdk.SaveFormatException("Acquisition count exceeds its fixed history array capacity.");
        cursor.Take(SemanticLayout.AcquisitionSize - sizeof(uint));
        ranges.Add(new(SemanticLayout.AcquisitionRangeName, slot.Offset + acquisitionStart, SemanticLayout.AcquisitionSize));
        cursor.Take(SemanticLayout.TutorialTailSize);
        Add(ref cursor, ranges, slot.Offset, SemanticLayout.EventRangeName, SemanticLayout.EventSize);
        ranges.Add(held.Range);
        ranges.Add(storage.Range);
        return new SemanticCharacter(slot.Index, [held, storage], ranges, map.Entries);
    }

    private static void Add(ref SlotCursor cursor, List<SaveRange> ranges, int slot, string name, int size) =>
        ranges.Add(new SaveRange(name, slot + cursor.Take(size), size));
}
