using System.Buffers.Binary;
using SaveToolkit.Sdk;

namespace SaveToolkit.Plugins.EldenRing.Tests;

public sealed class ItemMapBoundsTests
{
    private const int MapOffset = 0x20;
    private const int EntryHeaderSize = 8;

    [Theory]
    [InlineData(81U, 5118, 0U, 0)]
    [InlineData(81U, 5118, 0x00001000U, 13)]
    [InlineData(81U, 5118, 0x10001000U, 8)]
    [InlineData(82U, 5120, 0U, 0)]
    [InlineData(82U, 5120, 0x00001000U, 13)]
    [InlineData(82U, 5120, 0x10001000U, 8)]
    [InlineData(150U, 5120, 0x00001000U, 13)]
    [InlineData(150U, 5120, 0x40001000U, 0)]
    [InlineData(uint.MaxValue, 5120, 0xF0001000U, 0)]
    public void VersionedFixedCountsAndVariableStridesStayWithinTheSuppliedMap(
        uint version, int count, uint id, int extraSize)
    {
        var stride = EntryHeaderSize + extraSize;
        var length = MapOffset + count * stride;
        var bytes = new byte[length + sizeof(uint)];
        for (var record = 0; record < count; record++)
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(MapOffset + record * stride + sizeof(uint)), id);
        // A nonempty word after the fixed array must not be interpreted as an extra entry.
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(length), uint.MaxValue);
        var map = ItemMapReader.Read(bytes, version);
        Assert.Equal(length, map.PlayerOffset);
        Assert.Equal(id == 0 ? 0 : count, map.Entries.Count);
        Assert.True(length < EldenRingLayout.SlotSize);

        var last = length - stride;
        int[] truncatedLengths = [0, MapOffset - 1, MapOffset, MapOffset + 3, MapOffset + 7,
            MapOffset + stride - 1, last, last + 3, last + 7, length - 1];
        foreach (var truncatedLength in truncatedLengths.Distinct())
        {
            var error = Assert.Throws<SaveFormatException>(() => ItemMapReader.Read(bytes.AsSpan(0, truncatedLength), version));
            Assert.Contains("character slot", error.Message);
        }
        Assert.Equal(length, ItemMapReader.Read(bytes.AsSpan(0, length), version).PlayerOffset);
    }
}
