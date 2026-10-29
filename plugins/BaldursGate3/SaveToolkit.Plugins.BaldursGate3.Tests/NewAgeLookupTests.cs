using SaveToolkit.Sdk;

namespace SaveToolkit.Plugins.BaldursGate3.Tests;

public sealed class NewAgeLookupTests
{
    private const string ValueComponent = "test.Values";

    [Fact]
    public void RepeatedEntityReadsReuseDecodedOccurrences()
    {
        var model = LsmfReader.Read(LsmfFixture.Entities(10_000));
        var entities = model.Entities();
        var allocated = GC.GetAllocatedBytesForCurrentThread();
        for (var iteration = 0; iteration < 20; iteration++) _ = model.Entities();
        allocated = GC.GetAllocatedBytesForCurrentThread() - allocated;
        Assert.True(allocated < 1024, $"Repeated EntityId reads allocated {allocated:N0} bytes.");
        Assert.Equal(10_000, entities.Count);
        Assert.Equal(1, entities[0].Occurrence);
        Assert.Equal(10_000, entities[^1].Occurrence);
        Assert.All(entities, entity => Assert.Equal(10_000, entity.Copies));
    }

    [Fact]
    public void OwnerLookupsPreserveDuplicatesRowOrderAndIndependentResults()
    {
        var access = Access();
        Assert.Equal<int>([0, 2], access.RowsForEntity(ValueComponent, 4, 2));
        Assert.Null(access.RowForEntity(ValueComponent, 4, 2));
        Assert.Equal(3, access.RowForEntity(ValueComponent, 4, 1));
        Assert.Equal(1, access.RowForEntity(ValueComponent, 4, 0));
        var changed = access.RowsForEntity(ValueComponent, 4, 2);
        changed[0] = 99;
        Assert.Equal<int>([0, 2], access.RowsForEntity(ValueComponent, 4, 2));
        Assert.Empty(access.RowsForEntity(ValueComponent, 4, -1));
        Assert.Empty(access.RowsForEntity(ValueComponent, 4, 3));
        Assert.Empty(access.RowsForEntity("missing", 4, 0));
        Assert.Empty(access.RowsForEntity(LsmfReader.EntityComponent, 16, 0));
        Assert.All(access.Rows(LsmfReader.EntityComponent, 16), row => Assert.Null(row.EntityRow));
        Assert.Throws<SaveFormatException>(() => access.RowsForEntity(ValueComponent, 8, 2));
    }

    [Fact]
    public void RepeatedOwnerLookupsDoNotRebuildTheWholeComponent()
    {
        var access = Access();
        _ = access.RowsForEntity(ValueComponent, 4, 2);
        var allocated = GC.GetAllocatedBytesForCurrentThread();
        for (var iteration = 0; iteration < 1000; iteration++) _ = access.RowsForEntity(ValueComponent, 4, 2);
        allocated = GC.GetAllocatedBytesForCurrentThread() - allocated;
        Assert.True(allocated < 64 * 1024, $"Owner lookups allocated {allocated:N0} bytes for four component rows.");
    }

    [Fact]
    public void ModelsAndAccessInstancesDoNotShareDecodedState()
    {
        var first = LsmfReader.Read(LsmfFixture.Entities(2));
        var second = LsmfReader.Read(LsmfFixture.Entities(3));
        Assert.Equal(2, first.Entities().Count);
        Assert.Equal(3, second.Entities().Count);
        Assert.Empty(new LsmfAccess(first).RowsForEntity(ValueComponent, 4, 2));
        Assert.Equal<int>([0, 2], Access().RowsForEntity(ValueComponent, 4, 2));
    }

    private static LsmfAccess Access()
    {
        var bytes = LsmfFixture.Create(
            ("test.Padding", 1, 0, new byte[16]),
            ("test.Descriptor", 32, 0, new byte[32]),
            (LsmfReader.EntityComponent, 16, 3, new byte[48]),
            (ValueComponent, 4, 4, new byte[16]),
            ("test.Owners", 4, 4, new byte[16]));
        // An aligned, absolute owner descriptor: entity 2 owns rows 0 and 2.
        LsmfFixture.U64(bytes, 64, 160);
        LsmfFixture.U64(bytes, 72, 176);
        LsmfFixture.U64(bytes, 80, 3);
        LsmfFixture.U64(bytes, 88, 4);
        uint[] owners = [2, 0, 2, 1];
        for (var row = 0; row < owners.Length; row++) LsmfFixture.U32(bytes, 160 + row * 4, owners[row]);
        return new LsmfAccess(LsmfReader.Read(bytes));
    }
}

