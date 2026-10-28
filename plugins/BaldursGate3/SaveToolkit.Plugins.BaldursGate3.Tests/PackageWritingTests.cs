using System.Buffers.Binary;
using SaveToolkit.Sdk;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.BaldursGate3.Tests;

public sealed class PackageWritingTests
{
    [Theory]
    [InlineData((byte)0x00)]
    [InlineData((byte)0x11)]
    [InlineData((byte)0x22)]
    [InlineData((byte)0x43)]
    public void WriterKeepsUntouchedStoredBytesNamesFlagsAndBothOrders(byte flags)
    {
        var original = PackageFixture.Create(method: (byte)(flags & 0x0f), payloads:
            [("z-first.bin", [1, 2, 3]), ("a-second.bin", [4, 5, 6]), ("keep.bin", new byte[2048])]);
        // Retain unknown bytes in the filename padding and change table order, independently of payload order.
        var offset = (int)BinaryData.U64(original, LspkLayout.FileListOffset);
        var table = LarianCompression.Decode(original.AsSpan(offset + 8), 3 * LspkLayout.Entry18Size, 2);
        for (var i = 0; i < 3; i++) table[i * LspkLayout.Entry18Size + LspkLayout.Entry18Flags] = flags;
        table[100] = 0xab;
        var reordered = new byte[table.Length];
        table.AsSpan(LspkLayout.Entry18Size, LspkLayout.Entry18Size).CopyTo(reordered);
        table.AsSpan(0, LspkLayout.Entry18Size).CopyTo(reordered.AsSpan(LspkLayout.Entry18Size));
        table.AsSpan(2 * LspkLayout.Entry18Size).CopyTo(reordered.AsSpan(2 * LspkLayout.Entry18Size));
        var compressed = PackageFixture.Compress(reordered, 2);
        original = [.. original[..offset], 3, 0, 0, 0, .. BitConverter.GetBytes(compressed.Length), .. compressed, 0xde, 0xad];
        BinaryPrimitives.WriteUInt32LittleEndian(original.AsSpan(16), (uint)(compressed.Length + 8));
        original[21] = 0x7f; // package priority
        var before = LspkPackage.Read(original);
        Assert.Equal(original, LspkWriter.Write(original, new Dictionary<string, byte[]>()));
        var next = "a payload that changes length"u8.ToArray();
        var result = LspkWriter.Write(original, new Dictionary<string, byte[]> { ["a-second.bin"] = next });
        var after = LspkPackage.Read(result);
        Assert.Equal(before.Entries.Select(e => e.Name), after.Entries.Select(e => e.Name));
        Assert.Equal(before.Entries.OrderBy(e => e.Offset).Select(e => e.Name), after.Entries.OrderBy(e => e.Offset).Select(e => e.Name));
        Assert.Equal(before.Entries.Select(e => e.Flags), after.Entries.Select(e => e.Flags));
        Assert.Equal(0x7f, result[21]);
        Assert.Equal(new byte[] { 0xde, 0xad }, result[^2..]);
        foreach (var entry in before.Entries)
        {
            var rewritten = after.Entries.Single(e => e.Name == entry.Name);
            Assert.Equal(entry.Name == "a-second.bin" ? next : before.Extract(entry), after.Extract(rewritten));
            if (entry.Name != "a-second.bin")
            {
                Assert.True(original.AsSpan(entry.Offset, entry.StoredSize).SequenceEqual(result.AsSpan(rewritten.Offset, rewritten.StoredSize)));
            }
        }

        var newList = (int)BinaryData.U64(result, 8);
        var newSize = (int)BinaryData.U32(result, 16);
        var newTable = LarianCompression.Decode(result.AsSpan(newList + 8, newSize - 8), reordered.Length, 2);
        Assert.Equal(0xab, newTable[LspkLayout.Entry18Size + 100]);
        Assert.Equal(result[22..38], LspkIntegrity.Compute(after.Entries.OrderBy(e => e.Offset).Select(after.Extract)));
    }

    [Fact]
    public void ChecksumRejectsPayloadAndDigestCorruptionEvenWhenStructureIsValid()
    {
        var original = PackageFixture.Create(payloads: [("raw.bin", [1, 2, 3, 4])]);
        var changed = original.ToArray();
        changed[LspkLayout.Header16Size] ^= 1;
        var error = Assert.Throws<SaveFormatException>(() => new BaldursGate3Plugin().Open(MemorySaveSet.SingleFile("bad.lsv", changed)));
        Assert.Contains("digest mismatch", error.Message, StringComparison.Ordinal);
        changed = original.ToArray();
        changed[LspkLayout.Digest] ^= 1;
        Assert.Throws<SaveFormatException>(() => LspkPackage.Read(changed));
        Assert.Throws<SaveFormatException>(() => LspkWriter.Write(original, new Dictionary<string, byte[]> { ["unknown"] = [] }));
        Assert.Throws<SaveFormatException>(() => LspkWriter.Write(PackageFixture.Create(version: 16), new Dictionary<string, byte[]>()));
    }

    [Fact]
    public void LegacyPackagesAndXmlRemainReadOnly()
    {
        foreach (var version in new uint[] { 15, 16 })
        {
            using var document = new BaldursGate3Plugin().Open(MemorySaveSet.SingleFile("old.lsv",
                PackageFixture.Create(version, payloads: [("test.lsf", ResourceFixture.Create())])));
            Assert.False(document.CanEdit);
        }

        using var xml = new BaldursGate3Plugin().Open(MemorySaveSet.SingleFile("test.lsx", "<save/>"u8.ToArray()));
        Assert.False(xml.CanEdit);
    }

    [Theory]
    [InlineData("", "D51E8DDA9001B305EA810A99EDF9437F")]
    [InlineData("abc", "910251993DD350B1D797407E29E28073")]
    public void LegacyMd5PlusOneHasKnownVectors(string text, string expected)
    {
        Assert.Equal(expected, Convert.ToHexString(LspkIntegrity.Compute([System.Text.Encoding.ASCII.GetBytes(text)])));
        Assert.Equal(LspkIntegrity.Compute(["ab"u8.ToArray(), "c"u8.ToArray()]), LspkIntegrity.Compute(["abc"u8.ToArray()]));
    }
}
