using System.Buffers.Binary;
using SaveToolkit.Sdk.Binary;
using SaveToolkit.Sdk.Saves;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.FinalFantasy16.Tests;

internal static class FaithTestData
{
    public static byte[] Container(params (uint Type, ulong Version, byte[] Payload)[] blocks)
    {
        var bytes = new byte[FaithContainer.HeaderSize + blocks.Sum(block => FaithBlock.HeaderSize + block.Payload.Length) + sizeof(uint)];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, FaithContainer.HeaderSize);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(8), FaithContainer.GameVersion);
        var position = FaithContainer.HeaderSize;
        foreach (var block in blocks)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(position), block.Type);
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(position + 4), block.Payload.Length + 24);
            BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(position + 8), block.Version);
            block.Payload.CopyTo(bytes, position + FaithBlock.HeaderSize);
            position += FaithBlock.HeaderSize + block.Payload.Length;
        }

        FixCrc(bytes);
        return bytes;
    }

    public static byte[] Items()
    {
        var payload = new byte[FaithSchema.ItemLength];
        BinaryPrimitives.WriteInt32LittleEndian(payload, 1);
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(4), 1234);
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(16), 3);
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(20), 567);
        const int MetadataOffset = FaithInventory.Capacity * 20;
        const int KeysOffset = MetadataOffset + 16;
        BinaryPrimitives.WriteUInt64LittleEndian(payload.AsSpan(MetadataOffset), 2);
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(MetadataOffset + 8), 2);
        BinaryPrimitives.WriteUInt64LittleEndian(payload.AsSpan(KeysOffset + 8), 0x0000000100000001);
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(KeysOffset + FaithInventory.Capacity * 8), 2);
        return payload;
    }

    public static ISaveFile File(byte[] bytes, string title = "PPSA10665", string name = "game-save-data") =>
        MemorySaveSet.SingleFile(name, bytes, title, "FAITHDATA0001");

    public static void FixCrc(byte[] bytes) => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4),
        Crc32Ieee.Compute(bytes.AsSpan(FaithContainer.HeaderSize)));
}
