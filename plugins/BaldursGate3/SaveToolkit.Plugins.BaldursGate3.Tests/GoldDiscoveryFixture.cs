using System.Text;

namespace SaveToolkit.Plugins.BaldursGate3.Tests;

internal static class GoldDiscoveryFixture
{
    internal static (byte[] Bytes, LarianResource Resource, LsmfLiveSource Source) Create()
    {
        var entities = new byte[64];
        entities[0] = entities[32] = 0x10;
        entities[16] = entities[48] = 0x20;
        var blob = LsmfFixture.Create(
            ("test.Padding", 1, 0, new byte[16]),
            ("test.OwnerDescriptor", 32, 0, new byte[32]),
            ("core.v0.EntityId", 16, 4, entities),
            ("game.inventory.v0.StackEntry", 8, 3, new byte[24]),
            ("game.inventory.v0.Stack", 32, 1, new byte[32]),
            ("test.Members", 8, 2, new byte[16]),
            ("game.inventory.v0.NewStackComponent", 8, 1, new byte[8]),
            ("game.inventory.v1.ContainerComponent", 32, 1, new byte[32]),
            ("game.inventory.v0.ContainerSlotData", 16, 2, new byte[32]),
            ("test.Keys", 2, 0, new byte[8]),
            ("test.Values", 8, 2, new byte[16]),
            ("test.Owners", 4, 1, new byte[4]));
        int Offset(int column) => (int)BinaryData.U64(blob, LsmfFixture.Descriptor(blob, column) + 40) + 48;
        void Range(int offset, int begin, int size)
        {
            LsmfFixture.U64(blob, offset, (ulong)(begin - 48));
            LsmfFixture.U64(blob, offset + 8, (ulong)(begin + size - 48));
        }
        LsmfFixture.U64(blob, 64, (ulong)Offset(11));
        LsmfFixture.U64(blob, 72, (ulong)(Offset(11) + 4));
        LsmfFixture.U64(blob, 80, 7);
        LsmfFixture.U64(blob, 88, 1);
        Range(Offset(4), Offset(5), 16);
        Range(Offset(4) + 16, Offset(3), 24);
        LsmfFixture.U64(blob, Offset(6), (ulong)(Offset(4) - 48));
        Range(Offset(7), Offset(9), 4);
        Range(Offset(7) + 16, Offset(10), 16);
        for (var member = 0; member < 2; member++)
        {
            LsmfFixture.U64(blob, Offset(5) + member * 8, (ulong)(Offset(2) + member * 16));
            LsmfFixture.U64(blob, Offset(8) + member * 16, (ulong)(Offset(2) + (member + 2) * 16));
            LsmfFixture.U32(blob, Offset(8) + member * 16 + 8, (uint)member);
            blob[Offset(9) + member * 2] = (byte)member;
            LsmfFixture.U64(blob, Offset(10) + member * 8, (ulong)(Offset(8) + member * 16 - 48));
        }
        for (var contribution = 0; contribution < 3; contribution++)
        {
            LsmfFixture.U32(blob, Offset(3) + contribution * 8, contribution == 2 ? 1u : 0u);
            LsmfFixture.U32(blob, Offset(3) + contribution * 8 + 4, (uint)(contribution + 2));
        }
        var bytes = Resource(blob, entities);
        var resource = LsfReader.Read(bytes);
        return (bytes, resource, new LsmfLiveSource(0, 0, blob, resource));
    }

    private static byte[] Resource(byte[] blob, byte[] entities)
    {
        string[] names = ["NewAge", "Items", "Item", "CurrentTemplate", "Stats", "Creators", "Creator", "Entity", "TemplateID"];
        var template = new byte[16];
        template[0] = 0x30;
        var rows = new (int Name, int Parent, (int Name, uint Type, byte[] Data)[] Attributes)[]
        {
            (0, -1, [(0, 25, blob)]),
            (1, -1, []),
            (2, 1, [(3, 31, template), (4, 22, "OBJ_GoldCoin\0"u8.ToArray())]),
            (2, 1, [(3, 31, template), (4, 22, "OBJ_GoldPile\0"u8.ToArray())]),
            (5, -1, []),
            (6, 4, [(7, 31, entities[..16]), (8, 31, template)]),
            (6, 4, [(7, 31, entities[16..32]), (8, 31, template)]),
        };
        var strings = Write(writer =>
        {
            writer.Write(1u);
            writer.Write((ushort)names.Length);
            foreach (var name in names)
            {
                writer.Write((ushort)Encoding.UTF8.GetByteCount(name));
                writer.Write(Encoding.UTF8.GetBytes(name));
            }
        });
        var firstAttribute = 0;
        var nodes = Write(writer =>
        {
            foreach (var row in rows)
            {
                writer.Write(row.Name);
                writer.Write(row.Attributes.Length == 0 ? -1 : firstAttribute);
                writer.Write(row.Parent);
                firstAttribute += row.Attributes.Length;
            }
        });
        var attributes = Write(writer =>
        {
            for (var index = 0; index < rows.Length; index++)
                foreach (var attribute in rows[index].Attributes)
                {
                    writer.Write(attribute.Name);
                    writer.Write(((uint)attribute.Data.Length << 6) | attribute.Type);
                    writer.Write(index + 500);
                }
        });
        var values = rows.SelectMany(row => row.Attributes).SelectMany(attribute => attribute.Data).ToArray();
        return LsfBinary.Read(ResourceFixture.Create(compression: 3)).Rebuild(new Dictionary<int, byte[]>
        { [0] = strings, [1] = [], [2] = nodes, [3] = attributes, [4] = values });
    }

    private static byte[] Write(Action<BinaryWriter> action)
    {
        using var output = new MemoryStream();
        using var writer = new BinaryWriter(output);
        action(writer);
        return output.ToArray();
    }
}
