using System.Buffers.Binary;

namespace SaveToolkit.Plugins.BaldursGate3.Tests;

internal static class LiveNewAgeFixture
{
    internal static byte[] Create()
    {
        var health = new byte[64];
        foreach (var offset in new[] { 0, 32 })
        {
            LsmfFixture.U32(health, offset, 8);
            LsmfFixture.U32(health, offset + 4, 10);
            LsmfFixture.U32(health, offset + 8, 1);
            LsmfFixture.U32(health, offset + 12, 2);
        }
        var stats = new byte[72];
        foreach (var row in new[] { 0, 1 })
            for (var ability = 0; ability < 6; ability++) LsmfFixture.U32(stats, row * 36 + 8 + ability * 4, 10);
        var bytes = LsmfFixture.Create(
            ("core.v0.EntityId", 16, 2, new byte[32]),
            ("game.inventory.v0.StackEntry", 8, 3, new byte[24]),
            ("game.inventory.v0.Stack", 32, 1, new byte[32]),
            ("test.Members", 8, 2, new byte[16]),
            ("game.inventory.v0.NewStackComponent", 8, 1, new byte[8]),
            ("game.inventory.v1.ContainerComponent", 32, 1, new byte[32]),
            ("game.inventory.v0.ContainerSlotData", 16, 2, new byte[32]),
            ("test.Keys", 2, 0, new byte[8]), // extra arena bytes, outside nominal columns
            ("test.Values", 8, 2, new byte[16]),
            ("game.stats.v0.HealthComponent", 32, 2, health),
            ("game.stats.v3.StatsComponent", 36, 2, stats));
        int Offset(int column) => (int)BinaryData.U64(bytes, LsmfFixture.Descriptor(bytes, column) + 40) + 48;
        void Pair(int offset, int begin, int length)
        {
            LsmfFixture.U64(bytes, offset, (ulong)(begin - 48));
            LsmfFixture.U64(bytes, offset + 8, (ulong)(begin + length - 48));
        }
        var entries = Offset(1);
        for (var index = 0; index < 3; index++)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(entries + index * 8), (ushort)(index == 2 ? 1 : 0));
            BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(entries + index * 8 + 2), 0xabcd);
            LsmfFixture.U32(bytes, entries + index * 8 + 4, (uint)(index + 2));
        }
        Pair(Offset(2), Offset(3), 16);
        Pair(Offset(2) + 16, entries, 24);
        LsmfFixture.U64(bytes, Offset(3), (ulong)Offset(0));
        LsmfFixture.U64(bytes, Offset(3) + 8, (ulong)(Offset(0) + 16));
        LsmfFixture.U64(bytes, Offset(4), (ulong)(Offset(2) - 48));
        Pair(Offset(5), Offset(7), 4);
        Pair(Offset(5) + 16, Offset(8), 16);
        for (var index = 0; index < 2; index++)
        {
            LsmfFixture.U64(bytes, Offset(6) + index * 16, (ulong)(Offset(0) + index * 16));
            LsmfFixture.U32(bytes, Offset(6) + index * 16 + 8, (uint)index);
            BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(Offset(7) + index * 2), (ushort)(index + 4));
            LsmfFixture.U64(bytes, Offset(8) + index * 8, (ulong)(Offset(6) + index * 16 - 48));
        }
        return bytes;
    }

    internal static LsmfLiveSource Source(byte[]? bytes = null) => new(0, 0, bytes ?? Create());
}
