using System.Buffers.Binary;
using SaveToolkit.Plugins.Pragmata.Crypto;
using SaveToolkit.Plugins.Pragmata.Format;
using SaveToolkit.Sdk;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.Pragmata.Tests;

public sealed class MalformedInputTests
{
    [Fact]
    public void ForeignOrTruncatedContainersFailWithSaveFormatException()
    {
        foreach (var size in new[] { 0, 1, 15, 16, 155, 172, 1024 })
            Assert.Throws<SaveFormatException>(() => new DsssContainer(new byte[size]));
    }

    [Fact]
    public void ProbeRequiresTitleFileNameVersionAndFlags()
    {
        var plugin = new PragmataPlugin();
        var bytes = new byte[16];
        "DSSS"u8.CopyTo(bytes);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), 2);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(8), 0x10);
        Assert.True(plugin.Probe(MemorySaveSet.SingleFile("data000.bin", bytes, "PPSA02530")).IsMatch);
        Assert.False(plugin.Probe(MemorySaveSet.SingleFile("data000.bin", bytes, "PPSA00000")).IsMatch);
        Assert.False(plugin.Probe(MemorySaveSet.SingleFile("other.bin", bytes, "PPSA02530")).IsMatch);
        bytes[4] = 3;
        Assert.False(plugin.Probe(MemorySaveSet.SingleFile("data000.bin", bytes, "PPSA02530")).IsMatch);
        bytes[4] = 2;
        bytes[8] = 0x12;
        Assert.False(plugin.Probe(MemorySaveSet.SingleFile("data000.bin", bytes, "PPSA02530")).IsMatch);
    }

    [Fact]
    public void InvalidDeclaredLengthsAndExtraDataAreRejectedEvenWithCorrectOuterHash()
    {
        var original = SampleEvidenceTests.Files().Single(f => f.FileName == "data00-1.bin").ReadAllBytes();
        foreach (var length in new[] { 0UL, 64UL * 1024 * 1024 + 1, ulong.MaxValue, 131073UL })
        {
            var bytes = original.ToArray();
            BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(bytes.Length - 12), length);
            SampleEvidenceTests.RepairOuterHash(bytes);
            Assert.Throws<SaveFormatException>(() => new DsssContainer(bytes));
        }
        var extra = new byte[original.Length + 16];
        original.AsSpan(0, original.Length - 140).CopyTo(extra);
        original.AsSpan(original.Length - 140).CopyTo(extra.AsSpan(extra.Length - 140));
        SampleEvidenceTests.RepairOuterHash(extra);
        Assert.Throws<SaveFormatException>(() => new DsssContainer(extra));
        var damaged = original.ToArray();
        damaged[20] ^= 1;
        Assert.Contains("MurmurHash3", Assert.Throws<SaveFormatException>(() => new DsssContainer(damaged)).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void StructAlignmentIncludesSixteenByteContainerOrigin()
    {
        // The 24-byte struct starts at 24 with origin 16, but would start at 40 with origin 0.
        var bytes = PrimitivePayload(type: 16, size: 24, total: 48);
        var reader = new TypedReader(bytes);
        var root = Assert.Single(reader.Parse());
        var field = Assert.Single(root.Children);
        Assert.Equal(24, field.Offset);
        Assert.Equal(bytes.Length, reader.Position);
        Assert.Throws<SaveFormatException>(() => new TypedReader(bytes[..^1]).Parse());
    }

    [Fact]
    public void UnsupportedTagsSizesCountsAndTrailingBytesAreRejected()
    {
        foreach (var type in new[] { int.MinValue, -2, 0, 18, int.MaxValue })
            Assert.Throws<SaveFormatException>(() => new TypedReader(PrimitivePayload(type, 4, 28)).Parse());
        foreach (var size in new[] { 0, 3, 8, 65537 })
            Assert.Throws<SaveFormatException>(() => new TypedReader(PrimitivePayload(7, size, 28)).Parse());
        var count = PrimitivePayload(7, 4, 28);
        BinaryPrimitives.WriteUInt32LittleEndian(count.AsSpan(4), uint.MaxValue);
        Assert.Throws<SaveFormatException>(() => new TypedReader(count).Parse());
        var trailing = PrimitivePayload(7, 4, 29);
        Assert.Throws<SaveFormatException>(() => new TypedReader(trailing).Parse());
    }

    private static byte[] PrimitivePayload(int type, int size, int total)
    {
        var bytes = new byte[total];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, 1); // root hash
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), 1); // field count
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(8), 2); // class hash
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(12), 3); // field hash
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(16), type);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(20), size);
        return bytes;
    }
}
