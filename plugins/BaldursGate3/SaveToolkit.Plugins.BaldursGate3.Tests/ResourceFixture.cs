using System.Text;

namespace SaveToolkit.Plugins.BaldursGate3.Tests;

internal static class ResourceFixture
{
    // Two nodes, interleaved attributes. Owner tags deliberately differ from node indices.
    internal static byte[] Create(uint version = 7, bool adjacency = false, byte compression = 0, bool framed = true, uint? metadata = null,
        bool itemQuantity = false)
    {
        string[] names = itemQuantity ? ["Items", "Item", "Quantity", "Message", "Flag"] : ["Root", "Child", "Counter", "Message", "Flag"];
        var strings = Write(writer =>
        {
            writer.Write(1u); // one hash bucket
            writer.Write((ushort)names.Length);
            foreach (var name in names)
            {
                var encoded = Encoding.UTF8.GetBytes(name);
                writer.Write((ushort)encoded.Length);
                writer.Write(encoded);
            }
        });
        var nodes = Write(writer =>
        {
            Node(0, -1, itemQuantity ? 1 : 0);
            Node(1, 0, itemQuantity ? 0 : 1);
            void Node(uint name, int parent, int firstAttribute)
            {
                writer.Write(name);
                if (adjacency)
                {
                    writer.Write(parent);
                    writer.Write(-1); // no next sibling
                    writer.Write(firstAttribute);
                }
                else
                {
                    writer.Write(firstAttribute);
                    writer.Write(parent);
                }
            }
        });
        var attributes = Write(writer =>
        {
            Attribute(2, 4, 4, 500, 2, 0); // root: Counter = 7
            Attribute(3, 20, 6, 100, -1, 4); // child: Message = hello
            Attribute(4, 19, 1, 500, -1, 10); // root: Flag = true
            void Attribute(uint name, uint type, uint length, int owner, int next, uint offset)
            {
                writer.Write(name);
                writer.Write((length << 6) | type);
                writer.Write(adjacency ? next : owner);
                if (adjacency)
                {
                    writer.Write(offset);
                }
            }
        });
        byte[] values = [7, 0, 0, 0, .. "hello\0"u8, 1];
        var keys = version >= 6 && adjacency ? Write(writer => { writer.Write(0u); writer.Write(2u); }) : [];
        byte[][] sections = version >= 6 ? [strings, keys, nodes, attributes, values] : [strings, nodes, attributes, values];
        var compressed = sections.Select((section, i) => section.Length == 0 ? [] :
            PackageFixture.Compress(section, compression, framed && i != 0 && compression == 2)).ToArray();
        return Write(writer =>
        {
            writer.Write("LSOF"u8);
            writer.Write(version);
            writer.Write(0x02040000000002bcUL); // sample engine version, not a gameplay field
            for (var i = 0; i < sections.Length; i++)
            {
                writer.Write(sections[i].Length);
                writer.Write(compression == 0 ? 0 : compressed[i].Length);
            }

            writer.Write(compression);
            writer.Write((byte)0);
            writer.Write((ushort)0);
            writer.Write(metadata ?? (adjacency ? 1u : 0u));
            writer.Write(compressed[0]);
            ReadOnlySpan<int> order = version >= 6 ? [2, 3, 4, 1] : [1, 2, 3];
            foreach (var index in order)
            {
                writer.Write(compressed[index]);
            }
        });
    }

    private static byte[] Write(Action<BinaryWriter> action)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, true);
        action(writer);
        return stream.ToArray();
    }
}
