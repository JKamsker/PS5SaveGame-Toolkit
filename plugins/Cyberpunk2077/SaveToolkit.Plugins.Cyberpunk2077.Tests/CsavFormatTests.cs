using System.Buffers.Binary;
using System.Text;
using K4os.Compression.LZ4;
using SaveToolkit.Sdk;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.Cyberpunk2077.Tests;

public sealed class CsavFormatTests
{
    [Fact]
    public void TweakDbMoneyIdentifierMatchesIndependentCrc32Vector()
    {
        Assert.Equal(0xCBF43926U, Crc32("123456789"u8));
        Assert.Equal(InventoryReader.MoneyTweakId, Crc32("Items.money"u8) | ((ulong)"Items.money"u8.Length << 32));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void SyntheticContainerPreservesPaddingGapsDirectoryAndUnchangedPayload(bool compressed)
    {
        var bytes = Fixture(compressed);
        var container = CsavContainer.Parse(bytes);
        Assert.Equal("opaque", Assert.Single(CsavTree.Parse(container).Nodes).Name);
        Assert.Equal(bytes, container.Rebuild(container.Payload));
        var edited = container.Payload.ToArray();
        edited[4] = (byte)'z';
        var rebuilt = CsavContainer.Parse(container.Rebuild(edited));
        Assert.Equal(edited, rebuilt.Payload);
        Assert.Equal(bytes.AsSpan(45, 24).ToArray(), rebuilt.Original.AsSpan(45, 24).ToArray());
        Assert.Equal(bytes.AsSpan(container.DirectoryOffset - 3, 3).ToArray(),
            rebuilt.Original.AsSpan(rebuilt.DirectoryOffset - 3, 3).ToArray());
        Assert.Equal(bytes.AsSpan(container.DirectoryOffset, bytes.Length - container.DirectoryOffset - 8).ToArray(),
            rebuilt.Original.AsSpan(rebuilt.DirectoryOffset, rebuilt.Original.Length - rebuilt.DirectoryOffset - 8).ToArray());
    }

    [Fact]
    public void CodecAcceptsPublishedLiteralBlockAndOverlappingMatchGrammar()
    {
        Span<byte> target = stackalloc byte[5];
        Assert.Equal(5, LZ4Codec.Decode("\x50hello"u8, target));
        Assert.True(target.SequenceEqual("hello"u8));
        byte[] block = [0x15, (byte)'a', 1, 0, 0x50, (byte)'b', (byte)'c', (byte)'d', (byte)'e', (byte)'f'];
        var expanded = new byte[15];
        Assert.Equal(15, LZ4Codec.Decode(block, expanded));
        Assert.Equal("aaaaaaaaaabcdef", Encoding.ASCII.GetString(expanded));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(25, 0)]
    [InlineData(29, int.MaxValue)]
    [InlineData(33, -1)]
    [InlineData(37, int.MaxValue)]
    [InlineData(41, int.MaxValue)]
    [InlineData(73, int.MaxValue)] // 4ZLX expanded-length word.
    public void RejectsMalformedContainerWords(int offset, int value)
    {
        var bytes = Fixture();
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(offset), value);
        Assert.Throws<SaveFormatException>(() => Open(bytes));
    }

    [Fact]
    public void RejectsTruncationBadLz4FooterAndNodeLinks()
    {
        var valid = Fixture();
        for (var length = 0; length < valid.Length; length++)
        {
            Assert.Throws<SaveFormatException>(() => Open(valid[..length]));
        }

        var bytes = valid.ToArray();
        bytes[^1] = 0;
        Assert.Throws<SaveFormatException>(() => Open(bytes));
        bytes = valid.ToArray();
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(bytes.Length - 8), int.MaxValue);
        Assert.Throws<SaveFormatException>(() => Open(bytes));
        bytes = valid.ToArray();
        bytes[77] = 0; // Invalid zero-distance LZ4 match after an empty literal sequence.
        bytes[78] = 0;
        bytes[79] = 0;
        Assert.Throws<SaveFormatException>(() => Open(bytes));
        bytes = valid.ToArray();
        var directory = BinaryData.Int32(bytes, bytes.Length - 8);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(directory + 12), 0); // self sibling link.
        Assert.Throws<SaveFormatException>(() => Open(bytes));
        bytes = valid.ToArray();
        bytes[directory + 5] = 0x7F; // Invalid node-name length/encoding.
        Assert.Throws<SaveFormatException>(() => Open(bytes));
    }

    [Fact]
    public void ProbeRequiresTitleFilenameAndMagicAndNeverClaimsJson()
    {
        var plugin = new Cyberpunk2077Plugin();
        Assert.True(plugin.Probe(MemorySaveSet.SingleFile("sav.dat", Fixture(), "PPSA04027")).IsMatch);
        Assert.False(plugin.Probe(MemorySaveSet.SingleFile("sav.dat", Fixture(), "PPSA04018")).IsMatch);
        Assert.False(plugin.Probe(MemorySaveSet.SingleFile("metadata.9.json", Fixture(), "PPSA04027")).IsMatch);
    }

    private static void Open(byte[] bytes)
    {
        using var document = new Cyberpunk2077Plugin().Open(MemorySaveSet.SingleFile("sav.dat", bytes));
    }

    private static byte[] Fixture(bool compressed = true)
    {
        const int dataOffset = 69;
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true);
        writer.Write("VASC"u8);
        writer.Write(new byte[21]);
        writer.Write("FZLC"u8);
        writer.Write(1);
        writer.Write(dataOffset);
        writer.Write(compressed ? 17 : 8);
        writer.Write(8);
        writer.Write(Enumerable.Repeat((byte)0xCC, 24).ToArray());
        if (compressed)
        {
            writer.Write("4ZLX"u8);
            writer.Write(8);
            writer.Write((byte)0x80); // One literal-only LZ4 block.
        }

        writer.Write(0); // node ID
        writer.Write("abcd"u8);
        writer.Write(new byte[] { 0xAA, 0xBB, 0xCC }); // unknown inter-region gap
        var directory = checked((int)stream.Position);
        writer.Write("EDON"u8);
        writer.Write((byte)1);
        writer.Write((byte)0x86);
        writer.Write("opaque"u8);
        writer.Write(-1);
        writer.Write(-1);
        writer.Write(dataOffset);
        writer.Write(8);
        writer.Write(new byte[] { 0x55, 0xAA }); // unknown directory extension
        writer.Write(directory);
        writer.Write("ENOD"u8);
        return stream.ToArray();
    }

    private static uint Crc32(ReadOnlySpan<byte> bytes)
    {
        var crc = uint.MaxValue;
        foreach (var value in bytes)
        {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++)
            {
                crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320U : crc >> 1;
            }
        }

        return ~crc;
    }
}
