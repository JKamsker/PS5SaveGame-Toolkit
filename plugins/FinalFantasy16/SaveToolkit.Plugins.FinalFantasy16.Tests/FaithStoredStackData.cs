using System.Buffers.Binary;

namespace SaveToolkit.Plugins.FinalFantasy16.Tests;

internal static class FaithStoredStackData
{
    public const string ColumnKey = "stackState";
    public const int CycleLength = 5;
    private const int RecordSize = 16;
    private const int MainPayload = FaithContainer.HeaderSize + FaithBlock.HeaderSize;
    private const int SecondaryPayload = MainPayload + 33624;
    private const int SecondaryCapacity = 100;
    private const int MetadataSize = 16;
    private const int KeyShift = 32;

    public static int Quantity(int slot) => (slot % CycleLength) switch
    {
        0 => int.MinValue, 1 => -1, 2 => 0, 3 => 1, _ => int.MaxValue,
    };

    public static byte[] Save(bool eagerWithSecondary = false)
    {
        var bytes = FaithPerformanceData.CapacitySave();
        for (var slot = 0; slot < FaithInventory.Capacity; slot++)
            WriteQuantity(bytes, slot, Quantity(slot));
        if (eagerWithSecondary)
        {
            SetCount(bytes, MainPayload, FaithInventory.Capacity, CycleLength);
            SetCount(bytes, SecondaryPayload, SecondaryCapacity, CycleLength);
            var keys = SecondaryPayload + SecondaryCapacity * (RecordSize + sizeof(int)) + MetadataSize;
            for (var slot = 0; slot < CycleLength; slot++)
            {
                var offset = SecondaryPayload + slot * RecordSize;
                // Secondary ID zero is deliberately retained without assigning sentinel semantics.
                BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(offset), slot);
                BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(offset + sizeof(int)), Quantity(slot));
                BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(keys + slot * sizeof(ulong)),
                    (ulong)slot << KeyShift | (uint)slot);
            }
        }
        bytes = [.. bytes, 0xA5, 0x5A]; // Unknown trailer participates in all exact-byte assertions.
        FaithTestData.FixCrc(bytes);
        return bytes;
    }

    public static void WriteQuantity(byte[] bytes, int slot, int quantity) =>
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(MainPayload + slot * RecordSize + sizeof(int)), quantity);

    private static void SetCount(byte[] bytes, int payload, int capacity, int count)
    {
        var metadata = payload + capacity * (RecordSize + sizeof(int));
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(metadata), (ulong)count);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(metadata + sizeof(ulong)), count);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(metadata + MetadataSize + capacity * sizeof(ulong)), count);
    }
}
