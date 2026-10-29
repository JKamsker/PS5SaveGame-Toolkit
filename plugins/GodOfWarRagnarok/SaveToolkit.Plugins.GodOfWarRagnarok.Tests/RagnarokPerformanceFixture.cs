using System.Buffers.Binary;
using SaveToolkit.Sdk.Saves;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.GodOfWarRagnarok.Tests;

/// <summary>Legacy framing with duplicate IDs, numeric ties and the complete u32 quantity range.</summary>
internal static class RagnarokPerformanceFixture
{
    public const int RowCount = 1024;
    public const int RowsOffset = 0x514;
    private static readonly uint[] Quantities = [0, 2, 10, 100, uint.MaxValue];
    public static uint Quantity(int row) => Quantities[row % Quantities.Length];
    public static ulong Id(int row) => row % 2 == 0 ? 0x5AE80674D240529Eul : 0x90000000ul + (ulong)(row % 3);

    public static byte[] Bytes()
    {
        var bytes = new byte[RowsOffset + RowCount * 16 + sizeof(uint)];
        RagnarokTestSupport.LegacyGameplay().AsSpan(0, RowsOffset).CopyTo(bytes);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(0x508), RowCount);
        for (var row = 0; row < RowCount; row++)
        {
            var offset = RowsOffset + row * 16;
            BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(offset), Id(row));
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset + 8), Quantity(row));
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset + 12), (uint)(row % 3));
        }
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(bytes.Length - 4), uint.MaxValue);
        return bytes;
    }

    public static ISaveFile File() => MemorySaveSet.SingleFile("GOWRSAVE0", Bytes(), "PPSA08330");
    public static RagnarokDocument Open() => (RagnarokDocument)new GodOfWarRagnarokPlugin().Open(File());
}
