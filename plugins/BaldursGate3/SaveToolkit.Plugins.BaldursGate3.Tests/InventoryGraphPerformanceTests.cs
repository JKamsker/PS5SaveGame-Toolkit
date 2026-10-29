using System.Buffers.Binary;
using System.Diagnostics;

namespace SaveToolkit.Plugins.BaldursGate3.Tests;

[Collection(PerformanceTestCollection.Name)]
public sealed class InventoryGraphPerformanceTests(ITestOutputHelper output)
{
    private const int SlotCount = 50_000;

    [Fact]
    public void LargeUnownedInventoryDoesNotRescanPreviouslyAddedEntriesForEverySlot()
    {
        var access = Access(SlotCount);
        var identity = new LsmfIdentityGraph(access, null);
        var timer = Stopwatch.StartNew();
        var graph = new LsmfInventoryGraph(access, identity, null);
        timer.Stop();
        output.WriteLine($"Unowned inventory graph: {SlotCount:N0} slots in {timer.Elapsed.TotalMilliseconds:F2} ms.");
        Assert.Equal(SlotCount, graph.Entries.Count);
        Assert.All(graph.Entries, entry => Assert.Null(entry.OwnerBodyEntityRow));
        Assert.Equal(SlotCount, graph.Entries.Select(entry => entry.Path).Distinct().Count());
        Assert.True(timer.Elapsed < TimeSpan.FromSeconds(1), $"Inventory graph took {timer.Elapsed} for {SlotCount:N0} slots.");
    }

    [Fact]
    public void SameItemInDifferentUnownedContainersKeepsBothAmbiguousOccurrences()
    {
        var access = Access(4, containerCount: 2);
        var graph = new LsmfInventoryGraph(access, new LsmfIdentityGraph(access, null), null);
        Assert.Equal(4, graph.Entries.Count);
        Assert.Equal(4, graph.Entries.Select(entry => entry.Path).Distinct().Count());
        Assert.All(graph.Entries, entry =>
        {
            Assert.Null(entry.OwnerBodyEntityRow);
            Assert.True(entry.IsAmbiguous);
        });
        Assert.All(graph.Entries.GroupBy(entry => entry.ItemEntityRow), group =>
            Assert.Equal<int>([0, 1], group.Select(entry => entry.ContainerRow)));
    }

    private static LsmfAccess Access(int slots, int containerCount = 1)
    {
        var slotsPerContainer = slots / containerCount;
        var bytes = LsmfFixture.Create(
            ("test.Padding", 1, 0, new byte[16]),
            ("test.OwnerDescriptor", 32, 0, new byte[32]),
            (LsmfReader.EntityComponent, 16, slotsPerContainer + containerCount, new byte[(slotsPerContainer + containerCount) * 16]),
            ("game.inventory.v1.ContainerComponent", 32, containerCount, new byte[containerCount * 32]),
            ("game.inventory.v0.ContainerSlotData", 16, slots, new byte[slots * 16]),
            ("test.Keys", 2, 0, new byte[slots * 2]),
            ("test.Values", 8, slots, new byte[slots * 8]),
            ("test.Owners", 4, containerCount, new byte[containerCount * 4]));
        int Offset(int index) => (int)BinaryData.U64(bytes, LsmfFixture.Descriptor(bytes, index) + 40) + 48;
        LsmfFixture.U64(bytes, 64, (ulong)Offset(7));
        LsmfFixture.U64(bytes, 72, (ulong)(Offset(7) + containerCount * 4));
        LsmfFixture.U64(bytes, 80, 3);
        LsmfFixture.U64(bytes, 88, (ulong)containerCount);
        for (var row = 0; row < containerCount; row++)
        {
            var container = Offset(3) + row * 32;
            var start = row * slotsPerContainer;
            LsmfFixture.U64(bytes, container, (ulong)(Offset(5) + start * 2 - 48));
            LsmfFixture.U64(bytes, container + 8, (ulong)(Offset(5) + (start + slotsPerContainer) * 2 - 48));
            LsmfFixture.U64(bytes, container + 16, (ulong)(Offset(6) + start * 8 - 48));
            LsmfFixture.U64(bytes, container + 24, (ulong)(Offset(6) + (start + slotsPerContainer) * 8 - 48));
            LsmfFixture.U32(bytes, Offset(7) + row * 4, (uint)row);
        }
        for (var index = 0; index < slots; index++)
        {
            var record = Offset(4) + index * 16;
            LsmfFixture.U64(bytes, record, (ulong)(Offset(2) + (index % slotsPerContainer + containerCount) * 16));
            LsmfFixture.U32(bytes, record + 8, (uint)index);
            BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(Offset(5) + index * 2), (ushort)index);
            LsmfFixture.U64(bytes, Offset(6) + index * 8, (ulong)(record - 48));
        }
        return new LsmfAccess(LsmfReader.Read(bytes));
    }
}

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class PerformanceTestCollection
{
    public const string Name = "BG3 performance measurements";
}

