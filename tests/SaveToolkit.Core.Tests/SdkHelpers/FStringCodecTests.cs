using System.Buffers.Binary;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Binary;

namespace SaveToolkit.Core.Tests.SdkHelpers;

public sealed class FStringCodecTests
{
    private static readonly FStringLimits Limits = new(64, 32);

    [Theory]
    [InlineData("00000000", "")]
    [InlineData("0100000000", "")]
    [InlineData("FFFFFFFF0000", "")]
    [InlineData("03000000C3A900", "é")]
    [InlineData("FEFFFFFFE9000000", "é")]
    [InlineData("FDFFFFFF3DD800DE0000", "😀")]
    public void ReadsKnownNarrowWideEmptyAndSurrogatePairFixtures(string hex, string expected)
    {
        var bytes = Convert.FromHexString(hex);
        Assert.Equal(expected, Read(bytes));
        using var stream = new MemoryStream(bytes);
        using var reader = new BinaryReader(stream);
        Assert.Equal(expected, FStringCodec.Read(reader, Limits, "fixture"));
        Assert.Equal(bytes.Length, stream.Position);
        var cursor = new BoundedBinaryCursor([.. bytes, 0xAB], "fixture");
        Assert.Equal(expected, FStringCodec.Read(ref cursor, Limits));
        Assert.Equal(0xAB, cursor.ReadByte());
    }

    [Theory]
    [InlineData("03000000C3A900")]
    [InlineData("FDFFFFFF3DD800DE0000")]
    public void EveryTruncatedPrefixFailsWithContext_ForBothAdapters(string hex)
    {
        var bytes = Convert.FromHexString(hex);
        for (var length = 0; length < bytes.Length; length++)
        {
            var prefix = bytes[..length];
            Assert.Contains("fixture", Assert.Throws<SaveFormatException>(() => Read(prefix)).Message);
            using var stream = new NonSeekingStream(prefix);
            using var reader = new BinaryReader(stream);
            Assert.Contains("fixture", Assert.Throws<SaveFormatException>(() => FStringCodec.Read(reader, Limits, "fixture")).Message);
        }
    }

    [Theory]
    [InlineData("020000004142")]
    [InlineData("FEFFFFFF41000001")]
    [InlineData("03000000C08000")]
    [InlineData("02000000FF00")]
    [InlineData("FEFFFFFF00D80000")]
    [InlineData("FEFFFFFF00DC0000")]
    public void MissingTerminatorsAndInvalidUtf8Utf16FailStrictly(string hex) =>
        Assert.Throws<SaveFormatException>(() => Read(Convert.FromHexString(hex)));

    [Theory]
    [InlineData(int.MinValue)]
    [InlineData(int.MaxValue)]
    [InlineData(-1_073_741_824)]
    public void NegativeAndOverflowingLengthsFailBeforeAllocation(int length)
    {
        var bytes = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, length);
        Assert.Throws<SaveFormatException>(() => Read(bytes));
        if (length < 0)
            Assert.Throws<SaveFormatException>(() => FStringCodec.ByteLength(length, new(int.MaxValue, int.MaxValue), "large"));
        else
            Assert.Equal(length, FStringCodec.ByteLength(length, new(int.MaxValue, int.MaxValue), "large"));
    }

    [Fact]
    public void ByteAndUtf16UnitLimitsAreIndependent_AndIncludeTerminators()
    {
        Assert.Equal(6, FStringCodec.ByteLength(-3, new(6, 3), "wide"));
        Assert.Throws<SaveFormatException>(() => FStringCodec.ByteLength(-3, new(5, 3), "wide"));
        Assert.Throws<SaveFormatException>(() => FStringCodec.ByteLength(-3, new(6, 2), "wide"));
        Assert.Equal(3, FStringCodec.ByteLength(3, new(3, 0), "narrow"));
        Assert.Throws<SaveFormatException>(() => FStringCodec.ByteLength(3, new(6, 3) { MaxUtf8Bytes = 2 }, "narrow"));
        Assert.Equal(0, FStringCodec.ByteLength(0, new(0, 0), "empty"));
        Assert.Throws<ArgumentOutOfRangeException>(() => FStringCodec.ByteLength(0, new(-1, 0), "invalid config"));
        Assert.Throws<ArgumentOutOfRangeException>(() => FStringCodec.ByteLength(0, new(0, -1), "invalid config"));
        Assert.Throws<SaveFormatException>(() => FStringCodec.Decode(-2, new byte[2], Limits, "short"));
        Assert.Throws<SaveFormatException>(() => FStringCodec.Decode(0, new byte[1], Limits, "extra"));
    }

    [Theory]
    [InlineData("é", false, "03000000C3A900")]
    [InlineData("é", true, "FEFFFFFFE9000000")]
    [InlineData("😀", true, "FDFFFFFF3DD800DE0000")]
    [InlineData("", false, "0100000000")]
    [InlineData("", true, "FFFFFFFF0000")]
    public void EncodingKeepsExplicitWidth_AndMatchesIndependentFixtures(string text, bool wide, string hex)
    {
        Assert.Equal(Convert.FromHexString(hex), FStringCodec.Encode(text, wide, Limits));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EncodingRejectsNulAndUnpairedSurrogates(bool wide)
    {
        Assert.Throws<ArgumentException>(() => FStringCodec.Encode("a\0b", wide, Limits));
        Assert.Throws<ArgumentException>(() => FStringCodec.Encode("\uD800", wide, Limits));
        Assert.Throws<SaveFormatException>(() => FStringCodec.Encode("é", wide, new(2, 1)));
    }

    private static string Read(byte[] bytes)
    {
        var cursor = new BoundedBinaryCursor(bytes, "fixture");
        return FStringCodec.Read(ref cursor, Limits);
    }

    private sealed class NonSeekingStream(byte[] bytes) : MemoryStream(bytes)
    {
        public override bool CanSeek => false;
    }
}
