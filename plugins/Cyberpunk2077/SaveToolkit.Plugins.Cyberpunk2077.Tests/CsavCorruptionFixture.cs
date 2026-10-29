using System.Buffers.Binary;
using System.Text;

namespace SaveToolkit.Plugins.Cyberpunk2077.Tests;

internal sealed record FixtureNode(string Name, int Next, int Child, int Offset, int Size);

/// <summary>Independent wire encoding, including literal-only LZ4 blocks; no production writer.</summary>
internal static class CsavCorruptionFixture
{
    public const int TableOffset = 33;
    public const int EntrySize = 12;

    public static byte[] Create(byte[] payload, IReadOnlyList<FixtureNode> nodes,
        int[]? chunkSizes = null, bool compressed = false)
    {
        chunkSizes ??= [payload.Length];
        var dataOffset = TableOffset + chunkSizes.Length * EntrySize;
        using var output = new MemoryStream();
        using var writer = new BinaryWriter(output, Encoding.ASCII, true);
        writer.Write("VASC"u8); writer.Write(new byte[21]); writer.Write("FZLC"u8);
        writer.Write(chunkSizes.Length);
        writer.Write(new byte[chunkSizes.Length * EntrySize]);
        var descriptors = new List<(int Offset, int Stored, int Expanded)>();
        var position = 0;
        foreach (var size in chunkSizes)
        {
            var offset = (int)output.Position;
            if (compressed)
            {
                writer.Write("4ZLX"u8); writer.Write(size);
                writer.Write((byte)(Math.Min(size, 15) << 4));
                if (size >= 15)
                {
                    var extra = size - 15;
                    while (extra >= 255) { writer.Write((byte)255); extra -= 255; }
                    writer.Write((byte)extra);
                }
            }
            writer.Write(payload.AsSpan(position, size));
            descriptors.Add((offset, (int)output.Position - offset, size));
            position += size;
        }
        var directory = (int)output.Position;
        writer.Write("EDON"u8); WritePacked(writer, nodes.Count);
        foreach (var node in nodes)
        {
            WritePacked(writer, -node.Name.Length); writer.Write(Encoding.ASCII.GetBytes(node.Name));
            writer.Write(node.Next); writer.Write(node.Child);
            writer.Write(dataOffset + node.Offset); writer.Write(node.Size);
        }
        writer.Write(directory); writer.Write("ENOD"u8);
        var bytes = output.ToArray();
        for (var i = 0; i < descriptors.Count; i++)
        {
            SetWord(bytes, TableOffset + i * EntrySize, descriptors[i].Offset);
            SetWord(bytes, TableOffset + i * EntrySize + 4, descriptors[i].Stored);
            SetWord(bytes, TableOffset + i * EntrySize + 8, descriptors[i].Expanded);
        }
        return bytes;
    }

    public static byte[] Flat(int count = 2, bool compressed = false)
    {
        var payload = new byte[count * sizeof(int)];
        var nodes = new FixtureNode[count];
        for (var i = 0; i < count; i++)
        {
            SetWord(payload, i * sizeof(int), i);
            nodes[i] = new("x", i + 1 < count ? i + 1 : -1, -1, i * sizeof(int), sizeof(int));
        }
        return Create(payload, nodes, compressed: compressed);
    }

    public static void SetWord(byte[] bytes, int offset, int value) =>
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(offset, sizeof(int)), value);

    public static void WritePacked(BinaryWriter writer, int value)
    {
        var magnitude = Math.Abs(value);
        var first = (byte)((value < 0 ? 0x80 : 0) | (magnitude & 0x3F));
        magnitude >>= 6;
        writer.Write((byte)(first | (magnitude > 0 ? 0x40 : 0)));
        while (magnitude > 0)
        {
            var next = (byte)(magnitude & 0x7F);
            magnitude >>= 7;
            writer.Write((byte)(next | (magnitude > 0 ? 0x80 : 0)));
        }
    }
}
