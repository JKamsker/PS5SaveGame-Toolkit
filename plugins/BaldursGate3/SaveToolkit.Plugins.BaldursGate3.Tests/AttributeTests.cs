using System.Buffers.Binary;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;

namespace SaveToolkit.Plugins.BaldursGate3.Tests;

public sealed class AttributeTests
{
    [Fact]
    public void ScalarsPreserveSignednessAndFloatingPointSpecialValues()
    {
        Assert.Equal(ulong.MaxValue, Read(LarianAttributeType.ULongLong, "FFFFFFFFFFFFFFFF").AsUInt64());
        Assert.Equal(long.MinValue, Read(LarianAttributeType.Int64, "0000000000000080").AsInt64());
        Assert.Equal(-1, Read(LarianAttributeType.Int8, "FF").AsInt32());
        Assert.Equal(65535, Read(LarianAttributeType.UShort, "FFFF").AsInt32());
        Assert.Equal(1.5, Read(LarianAttributeType.Float, "0000C03F").AsDouble());
        Assert.Equal("NaN", Read(LarianAttributeType.Float, "0000C07F").AsString());
        Assert.Equal("Infinity", Read(LarianAttributeType.Double, "000000000000F07F").AsString());
        Assert.True(Read(LarianAttributeType.Bool, "02").AsBool());
    }

    [Fact]
    public void GuidUsesLslibByteSwapConvention()
    {
        var guid = Read(LarianAttributeType.UUID, "064FD73E603CDC42F68334F047CB79C6");
        Assert.Equal("3ed74f06-3c60-42dc-83f6-f034cb47c679", guid.AsString());
    }

    [Fact]
    public void StringsVectorsAndTranslationHandlesDecodeWithoutInventingNames()
    {
        Assert.Equal("hello", Read(LarianAttributeType.String, "68656C6C6F0000").AsString());
        Assert.Equal("1, -2, 3", Read(LarianAttributeType.IVec3, "01000000FEFFFFFF03000000").AsString());
        Assert.Equal("1, 2, 3", Read(LarianAttributeType.Vec3, "0000803F0000004000004040").AsString());
        var translated = new ResourceAttribute("Name", LarianAttributeType.TranslatedString, new byte[] { 2, 0, 3, 0, 0, 0, (byte)'h', (byte)'1', 0 });
        AttributeDecoder.Validate(translated);
        Assert.Equal("h1; version 2", AttributeDecoder.Read(translated).AsString());
    }

    [Fact]
    public void BadPrimitiveSizesAndTranslatedStringsAreRejected()
    {
        Assert.Throws<SaveFormatException>(() => Read(LarianAttributeType.Int, "0000"));
        Assert.Throws<SaveFormatException>(() => Read(LarianAttributeType.String, "6869"));
        Assert.Throws<SaveFormatException>(() => Read(LarianAttributeType.TranslatedString, "0100FFFFFFFF"));
        var invalidCount = new byte[11];
        BinaryPrimitives.WriteInt32LittleEndian(invalidCount.AsSpan(2), 1);
        BinaryPrimitives.WriteInt32LittleEndian(invalidCount.AsSpan(7), -1);
        Assert.Throws<SaveFormatException>(() => AttributeDecoder.Validate(new ResourceAttribute("Name", LarianAttributeType.TranslatedFSString, invalidCount)));
    }

    private static FieldValue Read(LarianAttributeType type, string hex)
    {
        var attribute = new ResourceAttribute("raw", type, Convert.FromHexString(hex));
        AttributeDecoder.Validate(attribute);
        return AttributeDecoder.Read(attribute);
    }
}
