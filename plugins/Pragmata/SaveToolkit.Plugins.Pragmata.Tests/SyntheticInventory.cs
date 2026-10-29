using SaveToolkit.Plugins.Pragmata.Format;

namespace SaveToolkit.Plugins.Pragmata.Tests;

/// <summary>Authored test records and envelope; no private samples or structural writer claims.</summary>
internal static class SyntheticInventory
{
    internal const uint FloatHash = 0x11223344;

    public static byte[] Create(int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        using var payload = new TypedPayloadFixture();
        payload.Root(KnownHashes.DetailRoot, 1);
        payload.Primitive(KnownHashes.Slot, FieldTags.Int32, BitConverter.GetBytes(0));
        payload.Root(KnownHashes.HeaderRoot, 4);
        payload.Primitive(KnownHashes.SaveDate, FieldTags.Int64, BitConverter.GetBytes(0L));
        payload.Primitive(KnownHashes.Playtime, FieldTags.UInt64, BitConverter.GetBytes(100UL));
        payload.Primitive(KnownHashes.Cleared, FieldTags.Boolean, BitConverter.GetBytes(false));
        payload.Primitive(KnownHashes.Difficulty, FieldTags.UInt32, BitConverter.GetBytes(0U));
        payload.Root(KnownHashes.GameRoot, 1);
        payload.Array(KnownHashes.GameSections, FieldTags.Class, 0, 2, 1, marker: true);
        payload.Class(KnownHashes.InventoryClass, 2);
        payload.Array(KnownHashes.Items, FieldTags.Class, 0, count, 1, marker: true);
        for (var index = 0; index < count; index++)
        {
            payload.Class(0x11111111, 3);
            payload.Primitive(KnownHashes.ItemId, FieldTags.UInt32, BitConverter.GetBytes((uint)index + 1));
            payload.Primitive(KnownHashes.Quantity, FieldTags.Int32, BitConverter.GetBytes(index % 11));
            payload.Primitive(FloatHash, FieldTags.Float32, BitConverter.GetBytes(index % 2 == 0 ? -2.5f : 4.75f));
        }
        payload.Primitive(KnownHashes.EquippedItem, FieldTags.UInt32, BitConverter.GetBytes(1U));
        payload.Class(KnownHashes.ClockClass, 3);
        payload.Primitive(KnownHashes.Elapsed, FieldTags.UInt64, BitConverter.GetBytes(100UL));
        payload.Primitive(KnownHashes.Demo, FieldTags.UInt64, BitConverter.GetBytes(0UL));
        payload.Primitive(KnownHashes.Pause, FieldTags.UInt64, BitConverter.GetBytes(0UL));
        return SyntheticDsss.Create(payload.Bytes);
    }
}
