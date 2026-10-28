using System.Buffers.Binary;
using SaveToolkit.Codecs;
using SaveToolkit.Plugins.UnrealGvas.Gvas;

namespace SaveToolkit.Plugins.UnrealGvas.Tests;

/// <summary>Test-only top-level legacy array splice. No writer is registered with the plugin/UI.</summary>
internal static class HogwartsWriteProbe
{
    public static byte[] Replace(byte[] original, string name, byte[] frames)
    {
        var gvas = GvasFile.Parse(original);
        Assert.Equal((ushort)4, gvas.Header.EngineMajor);
        Assert.Equal((ushort)27, gvas.Header.EngineMinor);
        var node = Assert.Single(gvas.Properties, node => node.Name == name && node.Type == "ArrayProperty");
        var tagPrefix = GvasFixture.Body(writer =>
        {
            GvasFixture.String(writer, name);
            GvasFixture.String(writer, "ArrayProperty");
        });
        int start = original.AsSpan(0, node.Offset).LastIndexOf(tagPrefix);
        Assert.True(start >= 0);
        int sizeOffset = start + tagPrefix.Length;
        Assert.Equal(node.Length, BinaryPrimitives.ReadInt32LittleEndian(original.AsSpan(sizeOffset)));
        Assert.Equal(0U, BinaryPrimitives.ReadUInt32LittleEndian(original.AsSpan(sizeOffset + sizeof(int))));
        var metadata = GvasFixture.Body(writer =>
        {
            GvasFixture.String(writer, "ByteProperty");
            writer.Write(false); // The observed legacy tag has no property GUID.
        });
        Assert.Equal(metadata, original.AsSpan(sizeOffset + 2 * sizeof(int), node.Offset - sizeOffset - 2 * sizeof(int)).ToArray());
        byte[] result = new byte[checked(original.Length - node.Length + sizeof(int) + frames.Length)];
        original.AsSpan(0, node.Offset).CopyTo(result);
        BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(sizeOffset), frames.Length + sizeof(int));
        BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(node.Offset), frames.Length);
        frames.CopyTo(result, node.Offset + sizeof(int));
        original.AsSpan(node.Offset + node.Length).CopyTo(result.AsSpan(node.Offset + sizeof(int) + frames.Length));
        Assert.Equal(original.AsSpan(node.Offset + node.Length).ToArray(), result.AsSpan(node.Offset + sizeof(int) + frames.Length).ToArray());
        return result;
    }

    // Independent frame traversal, separate from HogwartsImage.Decode. The additional independent
    // format oracle is native SQLite's integrity_check; this still uses the same managed Kraken codec.
    public static byte[] IndependentlyDecode(GvasFile file, GvasNode node)
    {
        using var stream = new MemoryStream(file.Original.Slice(node.Offset, node.Length).ToArray());
        using var reader = new BinaryReader(stream);
        Assert.Equal(node.Length - sizeof(int), reader.ReadInt32());
        using var output = new MemoryStream();
        while (stream.Position < stream.Length)
        {
            Assert.Equal(0x9E2A83C1UL, reader.ReadUInt64());
            Assert.Equal(131072UL, reader.ReadUInt64());
            int compressed = checked((int)reader.ReadUInt64());
            int plain = checked((int)reader.ReadUInt64());
            Assert.Equal((ulong)compressed, reader.ReadUInt64());
            Assert.Equal((ulong)plain, reader.ReadUInt64());
            output.Write(OodleCodec.Decompress(reader.ReadBytes(compressed), plain));
        }
        byte[] prefixed = output.ToArray();
        Assert.Equal(prefixed.Length - sizeof(int), BinaryPrimitives.ReadInt32LittleEndian(prefixed));
        Assert.Equal(prefixed.Length - 2 * sizeof(int), BinaryPrimitives.ReadInt32LittleEndian(prefixed.AsSpan(sizeof(int))));
        return prefixed[(2 * sizeof(int))..];
    }
}
