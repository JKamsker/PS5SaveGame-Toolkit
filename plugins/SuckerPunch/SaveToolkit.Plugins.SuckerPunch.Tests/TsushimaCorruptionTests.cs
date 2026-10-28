using System.Buffers.Binary;
using SaveToolkit.Sdk;
using static SaveToolkit.Plugins.SuckerPunch.Tests.SuckerPunchTestData;

namespace SaveToolkit.Plugins.SuckerPunch.Tests;

public sealed class TsushimaCorruptionTests
{
    [Fact]
    public void IndependentSmallChecksumVectorIsAccepted()
    {
        var bytes = new byte[555_008];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, 0x14E);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), 20);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(8), 413);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(12), 49);
        new byte[] { 1, 2, 3, 4 }.CopyTo(bytes, 16);
        using var document = Open(WithBytes(bytes));
        Assert.False(document.CanEdit); // Valid container, no recognised inventory layout.
        Assert.Equal(bytes, Assert.Single(document.Serialize()).Content.ToArray());
        bytes[19] = 5;
        Assert.Throws<SaveFormatException>(() => Open(WithBytes(bytes)));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(4, 15)]
    [InlineData(4, 555009)]
    [InlineData(8, 0)]
    [InlineData(12, 0)]
    public void InvalidHeadersAreRejected(int offset, uint value)
    {
        var bytes = Slot("suckerpunchUser0").ReadAllBytes();
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), value);
        Assert.Throws<SaveFormatException>(() => Open(WithBytes(bytes)));
    }

    [Theory]
    [InlineData("count")]
    [InlineData("length")]
    [InlineData("name")]
    [InlineData("duplicate")]
    [InlineData("truncated")]
    [InlineData("ambiguous")]
    public void MalformedInventoryIsRejectedEvenWithAValidChecksum(string corruption)
    {
        var bytes = Slot("suckerpunchUser0").ReadAllBytes();
        const int tableOffset = 0x4B92F;
        switch (corruption)
        {
            case "count":
                bytes[tableOffset] = 49;
                break;
            case "length":
                BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(tableOffset + 4), uint.MaxValue);
                break;
            case "name":
                bytes[tableOffset + 8] = 0xFF;
                break;
            case "duplicate":
                var offset = bytes.AsSpan().IndexOf("ArrowHeavy"u8);
                "DartNormal"u8.CopyTo(bytes.AsSpan(offset));
                break;
            case "truncated":
                BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), tableOffset + 32);
                break;
            case "ambiguous":
                "\x11\0\0\0LegendsInvestment"u8.CopyTo(bytes.AsSpan(16));
                break;
        }

        RepairChecksum(bytes);
        Assert.Throws<SaveFormatException>(() => Open(WithBytes(bytes)));
    }

    [Fact]
    public void LengthMismatchIsRejected()
    {
        var bytes = Slot("suckerpunchUser0").ReadAllBytes();
        Assert.Throws<SaveFormatException>(() => Open(WithBytes(bytes[..^1])));
    }
}
