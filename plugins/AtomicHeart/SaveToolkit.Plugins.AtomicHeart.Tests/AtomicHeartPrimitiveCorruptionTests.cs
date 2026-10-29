using System.Buffers.Binary;
using SaveToolkit.Sdk;

namespace SaveToolkit.Plugins.AtomicHeart.Tests;

public sealed class AtomicHeartPrimitiveCorruptionTests
{
    [Theory]
    [InlineData("BoolProperty", 1)]
    [InlineData("IntProperty", 0)]
    [InlineData("IntProperty", 3)]
    [InlineData("IntProperty", 5)]
    [InlineData("UInt32Property", 3)]
    [InlineData("FloatProperty", 5)]
    [InlineData("Int64Property", 7)]
    [InlineData("UInt64Property", 9)]
    [InlineData("DoubleProperty", 0)]
    public void InspectedFixedWidthPrimitivesRejectWrongPayloadSizes(string type, int size)
    {
        var plain = Plain(Primitive(type, size));
        var error = Assert.Throws<SaveFormatException>(() => WriteTestData.Open(SlotFixture.Create(plain: plain)));
        var expected = type == "BoolProperty" ? 0 : type is "IntProperty" or "UInt32Property" or "FloatProperty" ? 4 : 8;
        Assert.Equal($"Invalid {type} payload size: expected {expected} bytes, got {size}.", error.Message);
    }

    [Theory]
    [InlineData("IntProperty", 3, null)]
    [InlineData("IntProperty", 5, null)]
    [InlineData("BoolProperty", 1, null)]
    [InlineData("StructProperty", 7, "Timespan")]
    [InlineData("StructProperty", 9, "DateTime")]
    public void MetadataPrimitivesRejectWrongPayloadSizes(string type, int size, string? detail)
    {
        var error = Assert.Throws<SaveFormatException>(() => WriteTestData.Open(
            SlotFixture.Create(metadataPayload: Primitive(type, size, detail))));
        var expected = type == "BoolProperty" ? 0 : type == "IntProperty" ? 4 : 8;
        var label = detail is null ? type : $"{type} ({detail})";
        Assert.Equal($"Invalid {label} payload size: expected {expected} bytes, got {size}.", error.Message);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(255)]
    public void MetadataRejectsInvalidBooleanTagValue(byte value)
    {
        var error = Assert.Throws<SaveFormatException>(() => WriteTestData.Open(
            SlotFixture.Create(metadataPayload: Primitive("BoolProperty", 0, boolean: value))));
        Assert.Equal("Invalid SlotInfo property boolean.", error.Message);
    }

    [Theory]
    [InlineData("IntProperty", 4, null)]
    [InlineData("BoolProperty", 0, null)]
    [InlineData("StructProperty", 8, "Timespan")]
    [InlineData("StructProperty", 8, "DateTime")]
    [InlineData("StructProperty", 3, "UnknownNative")]
    [InlineData("UnknownProperty", 3, null)]
    public void ValidMetadataAndUnknownPayloadsRemainByteIdentical(string type, int size, string? detail)
    {
        var bytes = SlotFixture.Create(metadataPayload: Primitive(type, size, detail));
        using var document = WriteTestData.Open(bytes);
        Assert.Equal(bytes, WriteTestData.Output(document));
    }

    private static byte[] Primitive(string type, int size, string? detail = null, byte boolean = 0)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        SlotFixture.String(writer, "AuthoredValue"); SlotFixture.String(writer, type);
        writer.Write(size); writer.Write(0);
        if (detail is not null) { SlotFixture.String(writer, detail); writer.Write(new byte[16]); }
        if (type == "BoolProperty") writer.Write(boolean);
        writer.Write((byte)0);
        writer.Write(new byte[size]);
        PlainFixture.End(writer); writer.Write(0);
        return stream.ToArray();
    }

    private static byte[] Plain(byte[] root)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(0); writer.Write(root);
        writer.Write(0); // No subsystems.
        SlotFixture.String(writer, "AuthoredLevel"); writer.Write(0); writer.Write(0);
        var plain = stream.ToArray();
        BinaryPrimitives.WriteInt32LittleEndian(plain, plain.Length - sizeof(int));
        return plain;
    }
}
