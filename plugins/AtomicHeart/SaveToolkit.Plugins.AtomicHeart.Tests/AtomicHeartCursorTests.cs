using System.Buffers.Binary;
using SaveToolkit.Sdk;

namespace SaveToolkit.Plugins.AtomicHeart.Tests;

public sealed class AtomicHeartCursorTests
{
    [Theory]
    [InlineData("byte", 1)]
    [InlineData("ushort", 2)]
    [InlineData("int", 4)]
    [InlineData("long", 8)]
    [InlineData("ulong", 8)]
    [InlineData("float", 4)]
    [InlineData("double", 8)]
    [InlineData("count", 4)]
    [InlineData("string", 4)]
    public void EveryPrimitivePrefixKeepsAbsoluteTruncationOffsetAndPosition(string kind, int width)
    {
        for (var available = 0; available < width; available++)
        {
            var position = -1;
            var error = Assert.Throws<SaveFormatException>(() =>
            {
                var cursor = new SaveCursor(new byte[16], start: 3, end: 3 + available);
                try { _ = ReadPrimitive(cursor, kind); }
                finally { position = cursor.Position; }
            });
            Assert.Equal("Truncated SaveExtension data at 0x3.", error.Message);
            Assert.Equal(3, position);
        }
    }

    [Fact]
    public void PrimitivesRetainLittleEndianValuesAndExactWidths()
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            writer.Write((byte)0xAB); writer.Write((ushort)0xABCD); writer.Write(-123456789);
            writer.Write(-1234567890123456789L); writer.Write(0xFEDCBA9876543210UL);
            writer.Write(1.25f); writer.Write(-2.5);
        }
        var cursor = new SaveCursor(stream.ToArray());
        Assert.Equal((byte)0xAB, cursor.Byte());
        Assert.Equal((ushort)0xABCD, cursor.UInt16());
        Assert.Equal(-123456789, cursor.Int32());
        Assert.Equal(-1234567890123456789L, cursor.Int64());
        Assert.Equal(0xFEDCBA9876543210UL, cursor.UInt64());
        Assert.Equal(1.25f, cursor.Single());
        Assert.Equal(-2.5, cursor.Double());
        Assert.Equal(0, cursor.Remaining);
    }

    private static object ReadPrimitive(SaveCursor cursor, string kind) => kind switch
    {
        "byte" => cursor.Byte(), "ushort" => cursor.UInt16(), "int" => cursor.Int32(),
        "long" => cursor.Int64(), "ulong" => cursor.UInt64(), "float" => cursor.Single(),
        "double" => cursor.Double(), "count" => cursor.Count(3), "string" => cursor.String(),
        _ => throw new ArgumentException("Unknown test primitive.", nameof(kind)),
    };

    public static TheoryData<byte[], string> ValidStrings => new()
    {
        { [0, 0, 0, 0], "" },
        { [1, 0, 0, 0, 0], "" },
        { [255, 255, 255, 255, 0, 0], "" },
        { [4, 0, 0, 0, 0xC3, 0xA9, 0x21, 0], "é!" },
        { [253, 255, 255, 255, 0x3D, 0xD8, 0, 0xDE, 0, 0], "😀" },
    };

    [Theory]
    [MemberData(nameof(ValidStrings))]
    public void StringsDecodeExactSignedLengthsAndLeaveFollowingBytesUnread(byte[] encoded, string expected)
    {
        byte[] bytes = [.. encoded, 0x7F];
        var cursor = new SaveCursor(bytes);
        Assert.Equal(expected, cursor.String());
        Assert.Equal(encoded.Length, cursor.Position);
        Assert.Equal(1, cursor.Remaining);
        Assert.Equal(0x7F, cursor.Byte());
        Assert.Equal(0, cursor.Remaining);
    }

    public static TheoryData<byte[], string> InvalidStrings => new()
    {
        { [2, 0, 0, 0, 0x41, 0x42], "Unterminated SaveExtension string." },
        { [255, 255, 255, 255, 1, 0], "Unterminated SaveExtension string." },
        { [255, 255, 255, 255, 0, 1], "Unterminated SaveExtension string." },
        { [3, 0, 0, 0, 0xC0, 0x80, 0], "Invalid SaveExtension string encoding." },
        { [2, 0, 0, 0, 0x80, 0], "Invalid SaveExtension string encoding." },
        { [254, 255, 255, 255, 0, 0xD8, 0, 0], "Invalid SaveExtension string encoding." },
        { [254, 255, 255, 255, 0, 0xDC, 0, 0], "Invalid SaveExtension string encoding." },
        { [3, 0, 0, 0, 0x41, 0], "Truncated SaveExtension data at 0x4." },
        { [254, 255, 255, 255, 0x41, 0, 0], "Truncated SaveExtension data at 0x4." },
    };

    [Theory]
    [MemberData(nameof(InvalidStrings))]
    public void MalformedStringsRejectAtTheIntendedValidation(byte[] encoded, string message)
    {
        var error = Assert.Throws<SaveFormatException>(() => new SaveCursor(encoded).String());
        Assert.Equal(message, error.Message);
    }

    [Theory]
    [InlineData(int.MinValue)]
    [InlineData(int.MaxValue)]
    [InlineData(1_048_577)]
    [InlineData(-1_048_577)]
    public void ExcessiveStringLengthsRejectBeforeTakingTheirPayload(int length)
    {
        var bytes = new byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, length);
        var error = Assert.Throws<SaveFormatException>(() => new SaveCursor(bytes).String());
        Assert.Equal("SaveExtension string is too long.", error.Message);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void StringLimitCountsEncodedUnitsIncludingTerminator(bool wide)
    {
        byte[] bytes = wide ? [254, 255, 255, 255, 0x41, 0, 0, 0] : [2, 0, 0, 0, 0x41, 0];
        var cursor = new SaveCursor(bytes);
        Assert.Equal("A", cursor.String(maximumUnits: 2));
        Assert.Equal(0, cursor.Remaining);
        var error = Assert.Throws<SaveFormatException>(() => new SaveCursor(bytes).String(maximumUnits: 1));
        Assert.Equal("SaveExtension string is too long.", error.Message);
    }

    [Fact]
    public void BoundedStringCannotBorrowItsTerminatorFromTheNextRecord()
    {
        byte[] bytes = [2, 0, 0, 0, 0x41, 0];
        var cursor = new SaveCursor(bytes, end: bytes.Length - 1);
        var error = Assert.Throws<SaveFormatException>(() => cursor.String());
        Assert.Equal("Truncated SaveExtension data at 0x4.", error.Message);
        Assert.Equal(sizeof(int), cursor.Position);
    }

    [Fact]
    public void TakingBytesHonorsExactSubrangeAndDoesNotAdvanceOnFailure()
    {
        var cursor = new SaveCursor(new byte[] { 0xEE, 1, 2, 0xFF }, start: 1, end: 3);
        Assert.Equal(new byte[] { 1, 2 }, cursor.Take(2).ToArray());
        Assert.Empty(cursor.Take(0).ToArray());
        Assert.Equal(0, cursor.Remaining);
        foreach (var length in new[] { -1, 1, int.MaxValue })
        {
            var error = Assert.Throws<SaveFormatException>(() => cursor.Take(length));
            Assert.Equal("Truncated SaveExtension data at 0x3.", error.Message);
            Assert.Equal(3, cursor.Position);
        }
    }

    [Theory]
    [InlineData(-1, false)]
    [InlineData(0, true)]
    [InlineData(3, true)]
    [InlineData(4, false)]
    [InlineData(int.MaxValue, false)]
    public void CountEnforcesBothInclusiveBounds(int count, bool valid)
    {
        var bytes = new byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, count);
        var cursor = new SaveCursor(bytes);
        if (valid) Assert.Equal(count, cursor.Count(3));
        else
        {
            var error = Assert.Throws<SaveFormatException>(() => cursor.Count(3));
            Assert.Equal("Invalid SaveExtension count or length.", error.Message);
        }
    }
}
