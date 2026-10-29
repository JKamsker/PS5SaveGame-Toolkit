using System.Buffers.Binary;
using System.Text;

namespace SaveToolkit.Plugins.BaldursGate3.Tests;

/// <summary>Authored maps with an owned nested cycle and an ambiguous, unowned item occurrence.</summary>
internal static class RecursiveInventoryFixture
{
    private const int EntityBytes = 16;
    private const int EntityCount = 7;
    private const int ContainerBytes = 32;
    private const int SlotBytes = 16;
    private const int PartyMemberBytes = 48;
    private const int ArenaBase = 48;
    private const int SlotsPerContainer = 2;
    private const int ContainerCount = 3;
    private const int SlotCount = ContainerCount * SlotsPerContainer;

    internal static byte[] Blob()
    {
        var entities = new byte[EntityCount * EntityBytes];
        for (var entity = 0; entity < EntityCount; entity++) entities[entity * EntityBytes] = (byte)(entity + 1);
        var bytes = LsmfFixture.Create(
            ("test.Padding", 1, 0, new byte[16]),
            ("test.OwnerDescriptors", 32, 0, new byte[64]),
            ("core.v0.EntityId", EntityBytes, EntityCount, entities),
            ("game.inventory.v1.ContainerComponent", ContainerBytes, ContainerCount, new byte[ContainerCount * ContainerBytes]),
            ("game.inventory.v0.ContainerSlotData", SlotBytes, SlotCount, new byte[SlotCount * SlotBytes]),
            ("game.party.v0.MemberComponent", PartyMemberBytes, 1, new byte[PartyMemberBytes]),
            ("test.Keys", 2, 0, new byte[16]),
            ("test.Values", sizeof(ulong), SlotCount, new byte[SlotCount * sizeof(ulong)]),
            ("test.Owners", 4, 4, new byte[16]));

        int Offset(int column) => checked((int)BinaryData.U64(bytes, LsmfFixture.Descriptor(bytes, column) + 40) + ArenaBase);
        ulong Entity(int row) => (ulong)(Offset(2) + row * EntityBytes);
        void Range(int offset, int begin, int size)
        {
            LsmfFixture.U64(bytes, offset, (ulong)(begin - ArenaBase));
            LsmfFixture.U64(bytes, offset + 8, (ulong)(begin + size - ArenaBase));
        }
        void Owners(int descriptor, int column, int first, params uint[] owners)
        {
            var start = Offset(8) + first * sizeof(uint);
            LsmfFixture.U64(bytes, descriptor, (ulong)start);
            LsmfFixture.U64(bytes, descriptor + 8, (ulong)(start + owners.Length * sizeof(uint)));
            LsmfFixture.U64(bytes, descriptor + 16, (ulong)column);
            LsmfFixture.U64(bytes, descriptor + 24, (ulong)owners.Length);
            for (var row = 0; row < owners.Length; row++) LsmfFixture.U32(bytes, start + row * sizeof(uint), owners[row]);
        }

        // Party entity 0 points to Creator body 1. Container entities are 0, 2, 3.
        Owners(64, 3, 0, 0, 2, 3);
        Owners(96, 5, 3, 0);
        for (var field = 0; field < PartyMemberBytes; field += sizeof(ulong))
            LsmfFixture.U64(bytes, Offset(5) + field, ulong.MaxValue);
        LsmfFixture.U64(bytes, Offset(5), Entity(1));

        int[] items = [2, 4, 0, 5, 5, 6]; // 0→2→0 cycle; entity 5 occurs in two containers.
        ushort[] keys = [0, ushort.MaxValue, 7, 15, 0, ushort.MaxValue];
        for (var container = 0; container < ContainerCount; container++)
        {
            var first = container * SlotsPerContainer;
            Range(Offset(3) + container * ContainerBytes, Offset(6) + first * sizeof(ushort), SlotsPerContainer * sizeof(ushort));
            Range(Offset(3) + container * ContainerBytes + 16, Offset(7) + first * sizeof(ulong), SlotsPerContainer * sizeof(ulong));
        }
        for (var index = 0; index < items.Length; index++)
        {
            var slot = Offset(4) + index * SlotBytes;
            LsmfFixture.U64(bytes, slot, Entity(items[index]));
            LsmfFixture.U32(bytes, slot + 8, (uint)(index % SlotsPerContainer));
            LsmfFixture.U32(bytes, slot + 12, 0xabcdef01); // unknown bytes must survive
            BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(Offset(6) + index * sizeof(ushort)), keys[index]);
            LsmfFixture.U64(bytes, Offset(7) + index * sizeof(ulong), (ulong)(slot - ArenaBase));
        }
        return bytes;
    }

    internal static byte[] Resource(byte[] blob)
    {
        string[] names = ["NewAge", "Creators", "Creator", "Entity", "TemplateID"];
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
        var nodes = Write(writer =>
        {
            (int Name, int Attribute, int Parent)[] rows = [(0, 0, -1), (1, -1, -1), (2, 1, 1)];
            foreach (var node in rows)
            {
                writer.Write(node.Name);
                writer.Write(node.Attribute);
                writer.Write(node.Parent);
            }
        });
        var body = new byte[EntityBytes];
        body[0] = 2;
        var template = new byte[EntityBytes];
        template[0] = 0x42;
        (int Name, uint Type, byte[] Data, int Owner)[] values = [(0, 25, blob, 500), (3, 31, body, 501), (4, 31, template, 501)];
        var attributes = Write(writer =>
        {
            foreach (var value in values)
            {
                writer.Write(value.Name);
                writer.Write(((uint)value.Data.Length << 6) | value.Type);
                writer.Write(value.Owner);
            }
        });
        return LsfBinary.Read(LsmfFixture.Resource(blob)).Rebuild(new Dictionary<int, byte[]>
        {
            [0] = strings, [2] = nodes, [3] = attributes, [4] = values.SelectMany(value => value.Data).ToArray(),
        });
    }

    private static byte[] Write(Action<BinaryWriter> action)
    {
        using var output = new MemoryStream();
        using var writer = new BinaryWriter(output, Encoding.UTF8, leaveOpen: true);
        action(writer);
        return output.ToArray();
    }
}
