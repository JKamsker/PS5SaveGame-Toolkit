using System.Buffers.Binary;
using SaveToolkit.Sdk;

namespace SaveToolkit.Plugins.BaldursGate3.Tests;

public sealed class WriterCompatibilityTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void StringGrowthHasTheExactExpectedUncompressedLayout(bool adjacency)
    {
        var original = ResourceFixture.Create(adjacency: adjacency);
        var binary = LsfBinary.Read(original);
        var attributesOffset = 64 + binary.Data(LsfLayout.StringsPair).Length + binary.Data(LsfLayout.NodesPair).Length;
        var valuesOffset = attributesOffset + binary.Data(LsfLayout.AttributesPair).Length;
        byte[] replacement = "expanded\0"u8.ToArray();
        byte[] expected = [.. original[..(valuesOffset + 4)], .. replacement, .. original[(valuesOffset + 10)..]];
        var recordSize = adjacency ? LsfLayout.AdjacencyRecordSize : LsfLayout.CompactRecordSize;
        BinaryPrimitives.WriteUInt32LittleEndian(expected.AsSpan(attributesOffset + recordSize + LsfLayout.AttributeTypeAndLength),
            ((uint)replacement.Length << LsfLayout.AttributeTypeBits) | (uint)LarianAttributeType.String);
        BinaryPrimitives.WriteUInt32LittleEndian(expected.AsSpan(LsfLayout.MetadataOffset + LsfLayout.ValuesPair * LsfLayout.SizePairLength), 14);
        if (adjacency)
            BinaryPrimitives.WriteUInt32LittleEndian(expected.AsSpan(attributesOffset + 2 * recordSize + LsfLayout.ExtendedValueOffset), 13);
        Assert.Equal(expected, LsfWriter.Write(original, new Dictionary<int, byte[]> { [1] = replacement }));
        Assert.Equal(original, LsfWriter.Write(original, new Dictionary<int, byte[]> { [1] = "hello\0"u8.ToArray() }));
    }

    [Fact]
    public void WritersValidateUnknownTargetsBeforeReturningANoOp()
    {
        var resource = ResourceFixture.Create();
        Assert.Equal("Unknown LSOF attribute edit.", Assert.Throws<SaveFormatException>(() =>
            LsfWriter.Write(resource, new Dictionary<int, byte[]> { [3] = [] })).Message);
        var package = PackageFixture.Create(payloads: [("keep.bin", [1, 2, 3])]);
        Assert.Equal("Unknown LSPK entry edit.", Assert.Throws<SaveFormatException>(() =>
            LspkWriter.Write(package, new Dictionary<string, byte[]> { ["missing"] = [] })).Message);
        Assert.Equal("Writing is supported only for LSPK v18.", Assert.Throws<SaveFormatException>(() =>
            LspkWriter.Write(PackageFixture.Create(version: 16, payloads: [("keep.bin", [1, 2, 3])]),
                new Dictionary<string, byte[]> { ["missing"] = [] })).Message);
        var noOp = LspkWriter.Write(package, new Dictionary<string, byte[]> { ["keep.bin"] = [1, 2, 3] });
        Assert.Equal(package, noOp);
        Assert.NotSame(package, noOp);
    }
}
