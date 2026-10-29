using System.Buffers.Binary;
using SaveToolkit.Plugins.Pragmata.Format;
using SaveToolkit.Sdk;

namespace SaveToolkit.Plugins.Pragmata.Tests;

public sealed class TypedCorruptionTests
{
    [Theory]
    [InlineData(FieldTags.Enum, 1)]
    [InlineData(FieldTags.Enum, 2)]
    [InlineData(FieldTags.Enum, 4)]
    [InlineData(FieldTags.Enum, 8)]
    [InlineData(FieldTags.Boolean, 1)]
    [InlineData(FieldTags.Int8, 1)]
    [InlineData(FieldTags.UInt8, 1)]
    [InlineData(FieldTags.Int16, 2)]
    [InlineData(FieldTags.UInt16, 2)]
    [InlineData(FieldTags.Int32, 4)]
    [InlineData(FieldTags.UInt32, 4)]
    [InlineData(FieldTags.Int64, 8)]
    [InlineData(FieldTags.UInt64, 8)]
    [InlineData(FieldTags.Float32, 4)]
    [InlineData(FieldTags.Float64, 8)]
    [InlineData(FieldTags.Char8, 1)]
    [InlineData(FieldTags.Char16, 2)]
    [InlineData(FieldTags.Struct, 24)]
    public void EveryScalarTruncationIncludingAlignmentRejectsWithPlaintextOffset(int type, int size)
    {
        using var payload = new TypedPayloadFixture();
        payload.Root(1, 1);
        payload.Primitive(2, type, new byte[size]);
        AssertEveryTruncation(payload.Bytes);
        var reader = new TypedReader(payload.Bytes);
        var field = Assert.Single(Assert.Single(reader.Parse()).Children);
        Assert.Equal(type, field.Type);
        Assert.Equal(size, field.Size);
        Assert.Equal(payload.Bytes.Length, reader.Position);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CompoundRecordTruncationsReachStringsValueArraysNestedClassesAndClassMetadata(bool marker)
    {
        using var payload = new TypedPayloadFixture();
        payload.Root(1, 5);
        payload.String(2, [65, 0, 66, 0, 0, 0]);
        payload.Array(3, FieldTags.UInt32, sizeof(uint), 2, 0);
        payload.Word(11); payload.Word(22);
        payload.Field(4, FieldTags.Class);
        payload.Class(5, 1);
        payload.Primitive(6, FieldTags.UInt16, BitConverter.GetBytes((ushort)33));
        payload.Array(7, FieldTags.String, 0, 2, 0);
        payload.StringValue([67, 0]);
        payload.StringValue([]);
        payload.Array(8, FieldTags.Class, 0, 2, 1, marker);
        payload.Class(9, 0);
        payload.Class(10, 0);
        var reader = new TypedReader(payload.Bytes);
        var root = Assert.Single(reader.Parse());
        Assert.Equal(5, root.Children.Count);
        Assert.Equal(2, root.Children[^1].Children.Count);
        Assert.Equal(payload.Bytes.Length, reader.Position);
        AssertEveryTruncation(payload.Bytes);
    }

    [Theory]
    [InlineData(20, uint.MaxValue, "primitive type/size")]
    [InlineData(24, uint.MaxValue, "member size")]
    [InlineData(28, uint.MaxValue, "element count")]
    [InlineData(32, 2U, "array kind")]
    [InlineData(32, uint.MaxValue, "array kind")]
    [InlineData(28, 100U, "element count")]
    public void CorruptArrayDeclarationsRejectBeforeWalkingElements(int offset, uint value, string diagnostic)
    {
        using var payload = new TypedPayloadFixture();
        payload.Root(1, 1);
        payload.Array(2, FieldTags.UInt32, sizeof(uint), 2, 0);
        payload.Word(11); payload.Word(22);
        var bytes = payload.Bytes;
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), value);
        var error = Assert.Throws<SaveFormatException>(() => new TypedReader(bytes).Parse());
        Assert.Contains(diagnostic, error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0U)]
    [InlineData(4U)]
    [InlineData(uint.MaxValue)]
    public void CorruptStringLengthsRejectWhenTheyBreakFieldFraming(uint count)
    {
        using var payload = new TypedPayloadFixture();
        payload.Root(1, 2);
        payload.String(2, [65, 0, 0, 0]);
        payload.Primitive(3, FieldTags.UInt32, BitConverter.GetBytes(42U));
        var bytes = payload.Bytes;
        // The original count is two UTF-16 code units; these declarations misframe field 3.
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(20), count);
        Assert.Throws<SaveFormatException>(() => new TypedReader(bytes).Parse());
    }

    [Fact]
    public void StringLengthWithinAlignmentPaddingDoesNotMisframeTheNextField()
    {
        using var payload = new TypedPayloadFixture();
        payload.Root(1, 2);
        payload.String(2, [65, 0, 0, 0]);
        payload.Primitive(3, FieldTags.UInt32, BitConverter.GetBytes(42U));
        var bytes = payload.Bytes;
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(20), 1);
        var reader = new TypedReader(bytes);
        var root = Assert.Single(reader.Parse());
        Assert.Equal(2, root.Children[0].Size);
        Assert.Equal(42U, PrimitiveCodec.Read(root.Children[1], bytes).AsUInt32());
        Assert.Equal(bytes.Length, reader.Position);
    }

    [Theory]
    [InlineData(100, true)]
    [InlineData(101, false)]
    public void NestingBoundaryIsExplicitAndBounded(int depth, bool supported)
    {
        using var payload = new TypedPayloadFixture();
        payload.Root(1, 1);
        for (var index = 0; index < depth; index++)
        {
            payload.Field(2, FieldTags.Class);
            payload.Class(3, index + 1 == depth ? 0 : 1);
        }
        var reader = new TypedReader(payload.Bytes);
        if (supported)
        {
            Assert.Single(reader.Parse());
            Assert.Equal(payload.Bytes.Length, reader.Position);
        }
        else
            Assert.Contains("nesting limit", Assert.Throws<SaveFormatException>(() => reader.Parse()).Message, StringComparison.Ordinal);
    }

    private static void AssertEveryTruncation(byte[] bytes)
    {
        for (var length = 0; length < bytes.Length; length++)
        {
            var prefix = bytes.AsSpan(0, length).ToArray();
            var error = Assert.Throws<SaveFormatException>(() => new TypedReader(prefix).Parse());
            Assert.Contains("plaintext 0x", error.Message, StringComparison.Ordinal);
        }
    }
}
