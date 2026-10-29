using System.Buffers.Binary;

namespace SaveToolkit.Plugins.FinalFantasy16.Tests;

internal static class FaithPerformanceData
{
    public static byte[] CapacitySave(bool mapped = false, int opaqueBytes = 0)
    {
        const int RecordSize = 16;
        const int Metadata = FaithInventory.Capacity * 20;
        const int Keys = Metadata + 16;
        var payload = new byte[FaithSchema.ItemLength];
        for (var slot = 0; slot < FaithInventory.Capacity; slot++)
        {
            BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(slot * RecordSize), mapped ? 5 : 60000 + slot);
            BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(slot * RecordSize + sizeof(int)), slot + 2);
            BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(slot * RecordSize + 2 * sizeof(int)), slot);
            BinaryPrimitives.WriteUInt64LittleEndian(payload.AsSpan(Keys + slot * sizeof(ulong)), (ulong)slot << 32 | (uint)slot);
        }
        BinaryPrimitives.WriteUInt64LittleEndian(payload.AsSpan(Metadata), FaithInventory.Capacity);
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(Metadata + sizeof(ulong)), FaithInventory.Capacity);
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(Keys + FaithInventory.Capacity * sizeof(ulong)), FaithInventory.Capacity);
        return FaithTestData.Container((FaithSchema.ItemType, FaithSchema.ItemVersion, payload), (99, 1, new byte[opaqueBytes]));
    }
}
