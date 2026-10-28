namespace SaveToolkit.Plugins.EldenRing;

internal sealed record ItemInstance(int Record, int Offset, uint Handle, uint ItemId, uint? AshHandle);
internal sealed record ItemMap(int PlayerOffset, IReadOnlyList<ItemInstance> Entries)
{
    // Duplicate handles are ambiguous even when two entries happen to contain the same id.
    public IReadOnlyDictionary<uint, ItemInstance?> Resolve() => Entries
        .Where(entry => entry.Handle != 0 && entry.ItemId != 0)
        .GroupBy(entry => entry.Handle)
        .ToDictionary(group => group.Key, group => group.Count() == 1 ? group.Single() : null);
}

internal static class ItemMapReader
{
    public static ItemMap Read(ReadOnlySpan<byte> slot, uint version)
    {
        var count = version <= EldenRingLayout.LastLegacyVersion
            ? EldenRingLayout.LegacyItemCount : EldenRingLayout.ItemCount;
        var cursor = new SlotCursor(slot, EldenRingLayout.ItemMapOffset);
        var entries = new List<ItemInstance>();
        for (var index = 0; index < count; index++)
        {
            var offset = cursor.Position;
            var handle = cursor.UInt32();
            var id = cursor.UInt32();
            uint? ash = null;
            if (id != 0)
            {
                switch (id & EldenRingLayout.ItemTypeMask)
                {
                    case 0:
                        cursor.Take(2 * sizeof(uint));
                        ash = cursor.UInt32();
                        cursor.Take(1);
                        break;
                    case EldenRingLayout.ArmorType:
                        cursor.Take(EldenRingLayout.ArmorExtraSize);
                        break;
                }
            }
            // Retain nonempty headers, including unknown categories and orphan handles.
            if (id != 0 || handle != 0)
                entries.Add(new ItemInstance(index, offset, handle, id, ash));
        }
        return new ItemMap(cursor.Position, entries);
    }
}
