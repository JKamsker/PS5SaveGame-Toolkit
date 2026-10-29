using System.Buffers.Binary;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Binary;

namespace SaveToolkit.Core.Tests.SdkHelpers;

public sealed class BinaryCursorTests
{
    [Theory]
    [InlineData(-1, 0)]
    [InlineData(0, -1)]
    [InlineData(1, int.MaxValue)]
    [InlineData(int.MaxValue, 1)]
    [InlineData(4, 1)]
    public void RangesRejectNegativeOverflowingAndTruncatedBounds(int offset, int length)
    {
        var error = Assert.Throws<SaveFormatException>(() => Slice(offset, length));
        Assert.Contains("Test container", error.Message);
    }

    private static void Slice(int offset, int length) => _ = BinaryRange.Slice(new byte[4], offset, length, "Test container");

    [Fact]
    public void RangesAndSubrangesRespectExactBoundaries_AndKeepAbsoluteOffsets()
    {
        byte[] data = [0, 1, 2, 3, 4, 5];
        Assert.Equal([2, 3], BinaryRange.Slice(data, 2, 2, "range").ToArray());
        Assert.Empty(BinaryRange.Slice(data, data.Length, 0, "range").ToArray());
        var parent = new BoundedBinaryCursor(data, "slot", 1, 5);
        var child = parent.Subrange(3);
        Assert.Equal((1, 4, 1, 3), (child.Start, child.End, child.Position, child.Remaining));
        Assert.Equal(4, parent.Position);
        Assert.Equal([1, 2, 3], child.Take(3).ToArray());
        child.Position = child.Start;
        Assert.Equal(1, child.ReadByte());
        Assert.Equal(4, parent.ReadByte());
        Assert.Equal(0, parent.Remaining);
        Assert.Empty(parent.Subrange(0).Take(0).ToArray());
        Assert.Equal([0, 1, 2, 3, 4, 5], data); // No parser writes or copies into the source.
    }

    [Theory]
    [InlineData(-1, 4)]
    [InlineData(3, 2)]
    [InlineData(0, 5)]
    public void InvalidCursorBoundsProduceContextualErrors(int start, int end)
    {
        var error = Assert.Throws<SaveFormatException>(() => Construct(start, end));
        Assert.Contains("slot", error.Message);
    }

    private static void Construct(int start, int end) => _ = new BoundedBinaryCursor(new byte[4], "slot", start, end);

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(4)]
    [InlineData(int.MaxValue)]
    public void CursorCannotSeekOutsideItsContainingRange(int position) =>
        Assert.Throws<SaveFormatException>(() => Seek(position));

    private static void Seek(int position)
    {
        var cursor = new BoundedBinaryCursor(new byte[5], "slot", 1, 3);
        cursor.Position = position;
    }

    [Fact]
    public void FailedTakeOrSubrangeDoesNotAdvanceCursor()
    {
        var cursor = new BoundedBinaryCursor(new byte[4], "slot");
        cursor.Position = 2;
        try { _ = cursor.Take(int.MaxValue); Assert.Fail("Expected a format error"); }
        catch (SaveFormatException ex) { Assert.Contains("0x2", ex.Message); }
        Assert.Equal(2, cursor.Position);
        try { _ = cursor.Subrange(-1); Assert.Fail("Expected a format error"); }
        catch (SaveFormatException) { }
        Assert.Equal(2, cursor.Position);
    }

    [Fact]
    public void LittleEndianPrimitivesReadExactNativeWidths()
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            writer.Write((byte)0xFE);
            writer.Write(short.MinValue);
            writer.Write(ushort.MaxValue);
            writer.Write(int.MinValue);
            writer.Write(uint.MaxValue);
            writer.Write(long.MinValue);
            writer.Write(ulong.MaxValue);
            writer.Write(1.25f);
            writer.Write(-1.25d);
        }
        var cursor = new BoundedBinaryCursor(stream.ToArray(), "primitives");
        Assert.Equal(0xFE, cursor.ReadByte());
        Assert.Equal(short.MinValue, cursor.ReadInt16LE());
        Assert.Equal(ushort.MaxValue, cursor.ReadUInt16LE());
        Assert.Equal(int.MinValue, cursor.ReadInt32LE());
        Assert.Equal(uint.MaxValue, cursor.ReadUInt32LE());
        Assert.Equal(long.MinValue, cursor.ReadInt64LE());
        Assert.Equal(ulong.MaxValue, cursor.ReadUInt64LE());
        Assert.Equal(1.25f, cursor.ReadSingleLE());
        Assert.Equal(-1.25d, cursor.ReadDoubleLE());
        Assert.Equal(0, cursor.Remaining);
    }

    [Fact]
    public void EveryTruncatedPrimitivePrefixFails()
    {
        for (var primitive = 0; primitive < 10; primitive++)
        {
            var width = primitive switch { 0 => 1, 1 or 2 => 2, 3 or 4 or 7 or 9 => 4, _ => 8 };
            for (var length = 0; length < width; length++)
                Assert.Throws<SaveFormatException>(() => ReadPrimitive(new byte[length], primitive));
        }
    }

    private static void ReadPrimitive(byte[] bytes, int primitive)
    {
        var cursor = new BoundedBinaryCursor(bytes, "truncated");
        switch (primitive)
        {
            case 0: _ = cursor.ReadByte(); break;
            case 1: _ = cursor.ReadInt16LE(); break;
            case 2: _ = cursor.ReadUInt16LE(); break;
            case 3: _ = cursor.ReadInt32LE(); break;
            case 4: _ = cursor.ReadUInt32LE(); break;
            case 5: _ = cursor.ReadInt64LE(); break;
            case 6: _ = cursor.ReadUInt64LE(); break;
            case 7: _ = cursor.ReadSingleLE(); break;
            case 8: _ = cursor.ReadDoubleLE(); break;
            default: _ = cursor.ReadCount(100); break;
        }
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    [InlineData(101)]
    [InlineData(int.MaxValue)]
    public void CountRejectsNegativeAndOversizedLengths(int value) =>
        Assert.Throws<SaveFormatException>(() => ReadCount(value));

    [Fact]
    public void CountAcceptsBothBounds()
    {
        Assert.Equal(0, ReadCount(0));
        Assert.Equal(100, ReadCount(100));
    }

    private static int ReadCount(int value)
    {
        var bytes = new byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, value);
        var cursor = new BoundedBinaryCursor(bytes, "count");
        return cursor.ReadCount(100);
    }
}
