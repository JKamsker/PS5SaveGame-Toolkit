using System.Buffers.Binary;
using System.Text;
using SaveToolkit.Sdk.Saves;

namespace SaveToolkit.Core.Tests;

public sealed class ParamSfoMalformedStressTests
{
    private const int HeaderSize = 20;
    private const int IndexSize = 16;
    private const ushort Opaque = 0x9999;

    [Fact]
    public void LargeOverlappingReservedSlotsRejectWithoutPayloadMaterialization()
    {
        var source = BuildSharedSlots(15_000, 65_536);
        var before = source.ToArray();
        var limits = new ParamSfoPatchLimits(1024 * 1024, 65_536, 8, 32);

        var error = Assert.Throws<ParamSfoPatchException>(() => ParamSfoDocument.Parse(source, limits));

        Assert.Contains("data slot", error.Message, StringComparison.Ordinal);
        Assert.Equal(before, source);
        Assert.True(source.Length < limits.MaxFileBytes);
    }

    [Fact]
    public void LongOverlappingKeysUseOneTerminatorPassBeforeDecoding()
    {
        var source = BuildSharedKeyTable(40_000, 65_536);
        var before = source.ToArray();
        var limits = new ParamSfoPatchLimits(1024 * 1024, 1, 65_535, 1);

        var error = Assert.Throws<ParamSfoPatchException>(() => ParamSfoDocument.Parse(source, limits));

        Assert.Contains("key ranges", error.Message, StringComparison.Ordinal);
        Assert.Equal(before, source);
        Assert.True(source.Length < limits.MaxFileBytes);
    }

    [Fact]
    public void RangeSweepKeepsAdjacentAndZeroSlotsValidAndReportsNestedIndexesDeterministically()
    {
        Assert.Equal(2, ParamSfoDocument.Parse(BuildSlots([0, 4], 4)).Entries.Count);
        Assert.Equal(2, ParamSfoDocument.Parse(BuildSlots([0, 0], 0)).Entries.Count);

        var error = Assert.Throws<ParamSfoPatchException>(() => ParamSfoDocument.Parse(BuildSlots([0, 1], 4)));

        Assert.Contains("entries 0 and 1", error.Message, StringComparison.Ordinal);
    }

    private static byte[] BuildSharedSlots(int count, int maximumLength)
    {
        var keyBytes = Enumerable.Range(0, count)
            .SelectMany(index => Encoding.ASCII.GetBytes(KeyFor(index) + "\0"))
            .ToArray();
        var keyTable = Align(HeaderSize + count * IndexSize);
        var dataTable = Align(keyTable + keyBytes.Length);
        var bytes = new byte[dataTable + maximumLength];
        WriteHeader(bytes, keyTable, dataTable, count);
        keyBytes.CopyTo(bytes, keyTable);
        for (var index = 0; index < count; index++)
            WriteEntry(bytes, index, index * 4, Opaque, 0, maximumLength, 0);
        return bytes;
    }

    private static byte[] BuildSharedKeyTable(int count, int keyTableLength)
    {
        var keyTable = Align(HeaderSize + count * IndexSize);
        var dataTable = Align(keyTable + keyTableLength);
        var bytes = new byte[dataTable];
        WriteHeader(bytes, keyTable, dataTable, count);
        bytes.AsSpan(keyTable, keyTableLength).Fill((byte)'A');
        bytes[keyTable + keyTableLength - 1] = 0;
        for (var index = 0; index < count; index++)
            WriteEntry(bytes, index, index, Opaque, 0, 0, 0);
        return bytes;
    }

    private static byte[] BuildSlots(int[] offsets, int maximumLength)
    {
        var keyTable = Align(HeaderSize + offsets.Length * IndexSize);
        var dataTable = Align(keyTable + offsets.Length * 2);
        var dataLength = offsets.Length == 0 ? 0 : offsets.Max() + maximumLength;
        var bytes = new byte[dataTable + dataLength];
        WriteHeader(bytes, keyTable, dataTable, offsets.Length);
        for (var index = 0; index < offsets.Length; index++)
        {
            var key = new[] { (byte)('A' + index), (byte)0 };
            key.CopyTo(bytes, keyTable + index * 2);
            WriteEntry(bytes, index, index * 2, Opaque, 0, maximumLength, offsets[index]);
        }
        return bytes;
    }

    private static void WriteHeader(byte[] bytes, int keyTable, int dataTable, int count)
    {
        "\0PSF"u8.CopyTo(bytes);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(4), 0x00000101);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(8), keyTable);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(12), dataTable);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(16), count);
    }

    private static void WriteEntry(byte[] bytes, int index, int keyOffset, ushort format, int length, int maximumLength, int dataOffset)
    {
        var start = HeaderSize + index * IndexSize;
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(start), checked((ushort)keyOffset));
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(start + 2), format);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(start + 4), length);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(start + 8), maximumLength);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(start + 12), dataOffset);
    }

    private static string KeyFor(int index)
    {
        Span<char> chars = stackalloc char[3];
        chars[2] = (char)('A' + (index % 26));
        chars[1] = (char)('A' + (index / 26 % 26));
        chars[0] = (char)('A' + (index / (26 * 26) % 26));
        return new string(chars);
    }

    private static int Align(int value) => (value + 3) & ~3;
}
