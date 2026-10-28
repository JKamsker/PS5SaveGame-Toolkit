using System.Buffers.Binary;
using SaveToolkit.Sdk.Saves;

namespace SaveToolkit.Core.Tests;

public sealed class ParamSfoTests
{
    private const int HeaderSize = 20;
    private const int EntrySize = 16;
    private const int KeyTable = HeaderSize + EntrySize;
    private const int DataTable = KeyTable + 4;

    [Theory]
    [InlineData(8, -1)]
    [InlineData(12, -1)]
    [InlineData(16, int.MaxValue)]
    [InlineData(16, 268435456)]
    [InlineData(HeaderSize + 4, int.MaxValue)]
    [InlineData(HeaderSize + 12, int.MaxValue)]
    [InlineData(HeaderSize + 12, -1)]
    public void TryParse_InvalidOffsetsAndCounts_ReturnsNull(int offset, int value)
    {
        var bytes = ValidImage();
        Write(bytes, offset, value);

        Assert.Null(ParamSfo.TryParse(bytes));
    }

    [Fact]
    public void TryParse_TruncatedEntryTable_ReturnsNull()
    {
        var bytes = ValidImage();
        Write(bytes, 16, 2);

        Assert.Null(ParamSfo.TryParse(bytes));
    }

    [Fact]
    public void TryParse_ValidInteger_PreservesValue()
    {
        var parsed = ParamSfo.TryParse(ValidImage());

        Assert.NotNull(parsed);
        Assert.Equal(42, parsed.Entries["KEY"]);
    }

    private static byte[] ValidImage()
    {
        var bytes = new byte[DataTable + sizeof(int)];
        "\0PSF"u8.CopyTo(bytes);
        Write(bytes, 8, KeyTable);
        Write(bytes, 12, DataTable);
        Write(bytes, 16, 1);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(HeaderSize + 2), 0x0404);
        Write(bytes, HeaderSize + 4, sizeof(int));
        Write(bytes, HeaderSize + 8, sizeof(int));
        "KEY\0"u8.CopyTo(bytes.AsSpan(KeyTable));
        Write(bytes, DataTable, 42);
        return bytes;
    }

    private static void Write(byte[] bytes, int offset, int value) =>
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(offset), value);
}
