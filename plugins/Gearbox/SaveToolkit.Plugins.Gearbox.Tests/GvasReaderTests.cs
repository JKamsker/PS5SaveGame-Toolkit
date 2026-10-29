using System.Buffers.Binary;
using System.Text;
using SaveToolkit.Sdk;

namespace SaveToolkit.Plugins.Gearbox.Tests;

public sealed class GvasReaderTests
{
    private const int MaximumStringBytes = 16_384;

    [Theory]
    [InlineData("00000000", "")]
    [InlineData("0100000000", "")]
    [InlineData("FFFFFFFF0000", "")]
    [InlineData("04000000E99BAA00", "雪")]
    [InlineData("FDFFFFFF3DD800DE0000", "😀")]
    [InlineData("0400000061006200", "a\0b")]
    public void Strings_PreserveEmptyWideMultibyteAndEmbeddedNulForms(string hex, string expected)
    {
        var bytes = Convert.FromHexString(hex);
        var reader = new GvasReader(bytes);
        Assert.Equal(expected, reader.String());
        Assert.Equal(bytes.Length, reader.Position);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ByteLimit_IncludesTerminatorAndAcceptsExactBoundary(bool wide)
    {
        var units = wide ? MaximumStringBytes / sizeof(char) : MaximumStringBytes;
        var expected = new string('a', units - 1);
        var bytes = StringBytes(expected, wide);
        var reader = new GvasReader(bytes);
        Assert.Equal(expected, reader.String());
        Assert.Equal(sizeof(int) + MaximumStringBytes, reader.Position);
    }

    [Theory]
    [InlineData(int.MinValue)]
    [InlineData(int.MaxValue)]
    [InlineData(MaximumStringBytes + 1)]
    [InlineData(-MaximumStringBytes / 2 - 1)]
    public void OversizedLengths_KeepExactDiagnosticBeforeBodyRead(int length)
    {
        var bytes = new byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, length);
        Assert.Equal("Invalid GVAS string length.", Assert.Throws<SaveFormatException>(() => ReadString(bytes)).Message);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EveryIncompleteStringPrefix_KeepExactTruncationDiagnostic(bool wide)
    {
        var bytes = StringBytes("a雪😀", wide);
        for (var length = 0; length < bytes.Length; length++)
        {
            var prefix = bytes[..length];
            Assert.Equal("Truncated Gearbox GVAS header.", Assert.Throws<SaveFormatException>(() => ReadString(prefix)).Message);
        }
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(3)]
    [InlineData(int.MaxValue)]
    public void InvalidTake_KeepExactDiagnostic(int length) =>
        Assert.Equal("Truncated Gearbox GVAS header.", Assert.Throws<SaveFormatException>(() => Take(length)).Message);

    [Fact]
    public void PrimitivesAndEmptyTake_AdvanceOnlyByConsumedBytes()
    {
        var reader = new GvasReader([0xFE, 0xFF, 0xFF, 0xFF, 0x34, 0x12]);
        Assert.Equal(-2, reader.Int32());
        Assert.Equal(4, reader.Position);
        Assert.Equal(0x1234, reader.UInt16());
        Assert.Empty(reader.Take(0).ToArray());
        Assert.Equal(6, reader.Position);
    }

    private static byte[] StringBytes(string value, bool wide)
    {
        var body = (wide ? Encoding.Unicode : Encoding.UTF8).GetBytes(value + '\0');
        var bytes = new byte[sizeof(int) + body.Length];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, wide ? -body.Length / sizeof(char) : body.Length);
        body.CopyTo(bytes, sizeof(int));
        return bytes;
    }

    private static string ReadString(byte[] bytes)
    {
        var reader = new GvasReader(bytes);
        return reader.String();
    }

    private static void Take(int length)
    {
        var reader = new GvasReader([1, 2]);
        _ = reader.Take(length);
    }
}
