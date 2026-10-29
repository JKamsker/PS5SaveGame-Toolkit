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
    [InlineData("count", "Tsushima counted inventory table is missing or ambiguous.")]
    [InlineData("length", "Invalid Tsushima inventory name length.")]
    [InlineData("name", "Invalid Tsushima inventory identifier.")]
    [InlineData("duplicate", "Duplicate Tsushima inventory identifier.")]
    [InlineData("truncated", "Tsushima inventory table is truncated.")]
    [InlineData("ambiguous", "Tsushima inventory anchor is ambiguous.")]
    public void MalformedInventoryIsRejectedEvenWithAValidChecksum(string corruption, string expected)
    {
        var bytes = Slot("suckerpunchUser0").ReadAllBytes();
        const int tableOffset = 0x4B92F;
        var suppliesName = WriteAssertions.CountOffset(bytes, "Supplies") - sizeof(uint) - "Supplies".Length;
        switch (corruption)
        {
            case "count":
                bytes[tableOffset] = 49;
                break;
            case "length":
                BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(suppliesName - sizeof(uint)), uint.MaxValue);
                break;
            case "name":
                bytes[suppliesName] = 0xFF;
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
            default: throw new ArgumentOutOfRangeException(nameof(corruption));
        }

        RepairChecksum(bytes);
        Assert.Equal(expected, Assert.Throws<SaveFormatException>(() => Open(WithBytes(bytes))).Message);
    }

    [Fact]
    public void LengthMismatchIsRejected()
    {
        var bytes = Slot("suckerpunchUser0").ReadAllBytes();
        Assert.Throws<SaveFormatException>(() => Open(WithBytes(bytes[..^1])));
    }
}
