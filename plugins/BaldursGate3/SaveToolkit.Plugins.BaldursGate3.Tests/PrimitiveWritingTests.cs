using System.Buffers.Binary;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.BaldursGate3.Tests;

public sealed class PrimitiveWritingTests
{
    public static IEnumerable<object[]> Scalars() =>
    [
        [LarianAttributeType.Byte, new byte[1], FieldValue.Of(255L)],
        [LarianAttributeType.Int8, new byte[1], FieldValue.Of(-128L)],
        [LarianAttributeType.Short, new byte[2], FieldValue.Of(-32768L)],
        [LarianAttributeType.UShort, new byte[2], FieldValue.Of(65535L)],
        [LarianAttributeType.Int, new byte[4], FieldValue.Of(int.MinValue)],
        [LarianAttributeType.UInt, new byte[4], FieldValue.Of((ulong)uint.MaxValue)],
        [LarianAttributeType.Long, new byte[8], FieldValue.Of(long.MinValue)],
        [LarianAttributeType.Int64, new byte[8], FieldValue.Of(long.MaxValue)],
        [LarianAttributeType.ULongLong, new byte[8], FieldValue.Of(ulong.MaxValue)],
        [LarianAttributeType.Float, new byte[4], FieldValue.Of(1.25)],
        [LarianAttributeType.Double, new byte[8], FieldValue.Of(-1.5)],
        [LarianAttributeType.Bool, new byte[1], FieldValue.Of(true)],
        [LarianAttributeType.String, new byte[1], FieldValue.Of("longer 雪 😀")],
        [LarianAttributeType.WString, new byte[1], FieldValue.Of("UTF-8")],
    ];

    [Theory]
    [MemberData(nameof(Scalars))]
    public void EveryEditablePrimitiveEncodesAndDecodesExactly(int typeId, byte[] original, FieldValue next)
    {
        var attribute = new ResourceAttribute("test", (LarianAttributeType)typeId, original);
        Assert.True(AttributeEncoder.CanWrite(attribute));
        Assert.Equal(next, AttributeDecoder.Read(attribute with { Data = AttributeEncoder.Write(attribute, next) }));
    }

    [Fact]
    public void RejectedBatchesRollBackAndUndoRestoresOriginalBytes()
    {
        var original = ResourceFixture.Create(adjacency: true, compression: 2);
        using var document = new BaldursGate3Plugin().Open(MemorySaveSet.SingleFile("test.lsf", original));
        _ = document.ExpandNode("attributes/0/0/0/2");
        _ = document.ExpandNode("attributes/0/1/0/1");
        const string counter = "resource/0/attribute/0";
        const string message = "resource/0/attribute/1";
        var failed = document.ApplyEdits([new ValueEdit(counter, FieldValue.Of(42L)), new ValueEdit(message, FieldValue.Of("bad\0text"))]);
        Assert.False(failed.Succeeded);
        Assert.Equal(7, document.GetValue(counter).AsInt32());
        Assert.Equal(original, Assert.Single(document.Serialize()).Content.ToArray());
        var outcome = document.ApplyEdits([new ValueEdit(counter, FieldValue.Of(42L)), new ValueEdit(message, FieldValue.Of("new longer string"))]);
        Assert.True(outcome.Succeeded);
        var bytes = Assert.Single(document.Serialize()).Content.ToArray();
        var resource = LsfReader.Read(bytes);
        Assert.Equal(42, AttributeDecoder.Read(resource.Attributes[0]).AsInt32());
        Assert.Equal("new longer string", AttributeDecoder.Read(resource.Attributes[1]).AsString());
        Assert.True(document.ApplyEdits(outcome.Changes.Select(c => new ValueEdit(c.ValueId, c.OldValue)).ToArray()).Succeeded);
        Assert.Equal(original, Assert.Single(document.Serialize()).Content.ToArray());
    }

    [Fact]
    public void UnsafePrimitiveInputsAreRejectedAndSpecialValuesStayReadOnly()
    {
        var text = new ResourceAttribute("Text", LarianAttributeType.String, "hello\0"u8.ToArray());
        Assert.Throws<SaveFormatException>(() => AttributeEncoder.Write(text, FieldValue.Of("bad\0text")));
        Assert.Throws<SaveFormatException>(() => AttributeEncoder.Write(text, FieldValue.Of(new string('a', AttributeEncoder.MaxTextLength + 1))));
        var bytes = new byte[sizeof(float)];
        BinaryPrimitives.WriteSingleLittleEndian(bytes, float.NaN);
        Assert.False(AttributeEncoder.CanWrite(new ResourceAttribute("NaN", LarianAttributeType.Float, bytes)));
        Assert.Throws<SaveFormatException>(() => AttributeEncoder.Write(new ResourceAttribute("Float", LarianAttributeType.Float, new byte[4]), FieldValue.Of(double.MaxValue)));
        Assert.Throws<OverflowException>(() => AttributeEncoder.Write(new ResourceAttribute("Byte", LarianAttributeType.Byte, new byte[1]), FieldValue.Of(256L)));
        Assert.False(AttributeEncoder.CanWrite(new ResourceAttribute("Vector", LarianAttributeType.Vec3, new byte[12])));
        Assert.False(AttributeEncoder.CanWrite(new ResourceAttribute("XML", LarianAttributeType.String, ReadOnlyMemory<byte>.Empty) { XmlValue = "read-only" }));
    }

    [Fact]
    public void UndoPreservesNoncanonicalBoolAndStringEncodings()
    {
        var truth = new ResourceAttribute("Flag", LarianAttributeType.Bool, new byte[] { 0x7f });
        Assert.Equal(new byte[] { 0x7f }, AttributeEncoder.Write(truth, FieldValue.Of(true)));
        var text = new ResourceAttribute("Text", LarianAttributeType.String, "hello\0\0"u8.ToArray());
        Assert.Equal(text.Data.ToArray(), AttributeEncoder.Write(text, FieldValue.Of("hello")));
    }
}
