using System.Buffers.Binary;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;

namespace SaveToolkit.Plugins.BaldursGate3.Tests;

public sealed class LsfWriterTests
{
    public static IEnumerable<object[]> Layouts()
    {
        foreach (var version in new uint[] { 5, 6, 7 })
        foreach (var metadata in new uint[] { 0, 1, 2 })
        foreach (var codec in new byte[] { 0, 1, 2, 3 })
        {
            yield return [version, metadata, codec];
        }
    }

    [Theory]
    [MemberData(nameof(Layouts))]
    public void NoOpAndLengthChangingEditsPreserveEveryOtherAttribute(uint version, uint metadata, byte codec)
    {
        var original = ResourceFixture.Create(version, metadata == 1, codec, metadata: metadata);
        original = [.. original, 0xca, 0xfe]; // unknown trailing bytes must survive writes
        var resource = LsfReader.Read(original);
        Assert.Equal(original, LsfWriter.Write(original, new Dictionary<int, byte[]>()));
        Assert.Equal(original, LsfBinary.Read(original).Rebuild(new Dictionary<int, byte[]>()));
        foreach (var text in new[] { "A longer string — 雪 😀", "", "x" })
        {
            var encoded = AttributeEncoder.Write(resource.Attributes[1], FieldValue.Of(text));
            var result = LsfWriter.Write(original, new Dictionary<int, byte[]> { [1] = encoded });
            var reopened = LsfReader.Read(result);
            Assert.Equal(text, AttributeDecoder.Read(reopened.Attributes[1]).AsString());
            Assert.Equal(resource.Attributes[0].Data.ToArray(), reopened.Attributes[0].Data.ToArray());
            Assert.Equal(resource.Attributes[2].Data.ToArray(), reopened.Attributes[2].Data.ToArray());
            Assert.Equal(resource.Nodes.Select(n => n.KeyAttribute), reopened.Nodes.Select(n => n.KeyAttribute));
            Assert.Equal(new byte[] { 0xca, 0xfe }, result[^2..]);
            Assert.Equal(original[..LsfLayout.MetadataOffset], result[..LsfLayout.MetadataOffset]);
            var before = LsfBinary.Read(original);
            var after = LsfBinary.Read(result);
            Assert.Equal(before.Data(LsfLayout.StringsPair), after.Data(LsfLayout.StringsPair));
            Assert.Equal(before.Data(LsfLayout.NodesPair), after.Data(LsfLayout.NodesPair));
            if (version >= 6) Assert.Equal(before.Data(LsfLayout.KeysPair), after.Data(LsfLayout.KeysPair));
            var oldStored = StoredSections(original);
            var newStored = StoredSections(result);
            Assert.Equal(oldStored[0], newStored[0]); // original compressed string/name table
            Assert.Equal(oldStored[before.Pair(LsfLayout.NodesPair)], newStored[after.Pair(LsfLayout.NodesPair)]);
            if (version >= 6) Assert.Equal(oldStored[1], newStored[1]); // keys physically move but keep encoding
        }
    }

    [Fact]
    public void ExtendedUnorderedValuesAndUnreferencedGapsArePreserved()
    {
        var source = ResourceFixture.Create(adjacency: true);
        var binary = LsfBinary.Read(source);
        var attributes = binary.Data(LsfLayout.AttributesPair).ToArray();
        // Counter is relocated after the flag; message stays before both. Gaps contain sentinel bytes.
        BinaryPrimitives.WriteUInt32LittleEndian(attributes.AsSpan(12), 11);
        BinaryPrimitives.WriteUInt32LittleEndian(attributes.AsSpan(16 + 12), 2);
        BinaryPrimitives.WriteUInt32LittleEndian(attributes.AsSpan(32 + 12), 9);
        byte[] values = [0xca, 0xfe, .. "hello\0"u8, 0xac, 1, 0xdc, 7, 0, 0, 0, 0xef];
        var original = binary.Rebuild(new Dictionary<int, byte[]> { [3] = attributes, [4] = values });
        var result = LsfWriter.Write(original, new Dictionary<int, byte[]> { [1] = "long message\0"u8.ToArray() });
        var rebuiltValues = LsfBinary.Read(result).Data(LsfLayout.ValuesPair);
        byte[] expected = [0xca, 0xfe, .. "long message\0"u8, 0xac, 1, 0xdc, 7, 0, 0, 0, 0xef];
        Assert.Equal(expected, rebuiltValues);
        var reopened = LsfReader.Read(result);
        Assert.Equal(7, AttributeDecoder.Read(reopened.Attributes[0]).AsInt32());
        Assert.True(AttributeDecoder.Read(reopened.Attributes[2]).AsBool());
    }

    [Fact]
    public void OverlappingExtendedValuesAreNotRewritten()
    {
        var source = ResourceFixture.Create(adjacency: true);
        var binary = LsfBinary.Read(source);
        var attributes = binary.Data(LsfLayout.AttributesPair).ToArray();
        BinaryPrimitives.WriteUInt32LittleEndian(attributes.AsSpan(32 + 12), 0); // bool aliases int's first byte
        var original = binary.Rebuild(new Dictionary<int, byte[]> { [3] = attributes });
        Assert.Equal(original, LsfWriter.Write(original, new Dictionary<int, byte[]>()));
        Assert.Throws<SaveFormatException>(() => LsfWriter.Write(original, new Dictionary<int, byte[]> { [0] = [8, 0, 0, 0] }));
    }

    [Fact]
    public void InvalidEditsAreRejectedBeforeWriting()
    {
        var original = ResourceFixture.Create();
        Assert.Throws<SaveFormatException>(() => LsfWriter.Write(original, new Dictionary<int, byte[]> { [99] = [] }));
        Assert.Throws<SaveFormatException>(() => LsfWriter.Write(original, new Dictionary<int, byte[]> { [0] = [1] }));
        Assert.Throws<SaveFormatException>(() => LsfWriter.Write(original, new Dictionary<int, byte[]> { [1] = "no terminator"u8.ToArray() }));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RawLz4AndRawSectionsUnderCompressedHeadersKeepTheirEncoding(bool rawSections)
    {
        var original = ResourceFixture.Create(compression: rawSections ? (byte)0 : (byte)2, framed: false, metadata: 2);
        if (rawSections) original[56] = 2; // stored sizes remain zero: raw sections, despite global LZ4 flag
        var result = LsfWriter.Write(original, new Dictionary<int, byte[]> { [1] = "length changed\0"u8.ToArray() });
        Assert.Equal(2, result[56]);
        Assert.Equal("length changed", AttributeDecoder.Read(LsfReader.Read(result).Attributes[1]).AsString());
        var values = StoredSections(result)[LsfLayout.ValuesPair];
        Assert.False(LarianCompression.IsLz4Frame(values));
    }

    private static byte[][] StoredSections(byte[] bytes)
    {
        var count = BinaryData.U32(bytes, 4) >= 6 ? LsfLayout.V6PairCount : LsfLayout.V5PairCount;
        var flags = bytes[LsfLayout.MetadataOffset + count * LsfLayout.SizePairLength];
        var position = LsfLayout.MetadataOffset + count * LsfLayout.SizePairLength + LsfLayout.MetadataTrailerSize;
        int[] order = count == LsfLayout.V6PairCount ? [0, 2, 3, 4, 1] : [0, 1, 2, 3];
        var result = new byte[count][];
        foreach (var index in order)
        {
            var expanded = (int)BinaryData.U32(bytes, LsfLayout.MetadataOffset + index * LsfLayout.SizePairLength);
            var stored = (int)BinaryData.U32(bytes, LsfLayout.MetadataOffset + index * LsfLayout.SizePairLength + sizeof(uint));
            var length = stored == 0 || (flags & 0x0f) == 0 ? expanded : stored;
            result[index] = bytes.AsSpan(position, length).ToArray();
            position += length;
        }

        return result;
    }
}
