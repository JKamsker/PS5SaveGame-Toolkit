namespace SaveToolkit.Plugins.Gearbox.Tests;

public sealed class SerialDatabaseLoadingTests
{
    private const long SmallCatalogAllocationLimit = 2 * 1024 * 1024;

    [Theory]
    [InlineData("bl3", 62)]
    [InlineData("ttwl", 59)]
    public void Initialization_DoesNotMaterializeCatalogs(string game, int version)
    {
        var before = GC.GetAllocatedBytesForCurrentThread();
        var database = new SerialDatabase(game, version);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(version, database.MaximumVersion);
        Assert.Equal("gearbox." + game, database.Pack.Id);
        Assert.True(allocated < SmallCatalogAllocationLimit, $"Manifest initialization allocated {allocated:N0} bytes.");
    }

    [Theory]
    [InlineData("bl3", 62)]
    [InlineData("ttwl", 59)]
    public void SmallCatalog_IsLoadedOnceWithoutMaterializingUnrelatedTables(string game, int version)
    {
        var before = GC.GetAllocatedBytesForCurrentThread();
        var database = new SerialDatabase(game, version);
        var records = database.Records("skills").ToArray();
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.NotEmpty(records);
        Assert.True(allocated < SmallCatalogAllocationLimit, $"One small catalog allocated {allocated:N0} bytes.");
        Parallel.For(0, 16, _ =>
        {
            var record = database.Find("skills", records[0].Key);
            Assert.Same(records[0], record);
            Assert.Equal(database.Pack.Id, record!.Dataset.PackId);
        });
        Assert.Null(database.TryFind("absent-catalog", "absent-id"));
        Assert.Null(database.Find("skills", "absent-id"));
    }

    [Theory]
    [InlineData("bl3", 62)]
    [InlineData("ttwl", 59)]
    public void WidthOnly_DoesNotMaterializeSerialIndices(string game, int version)
    {
        var before = GC.GetAllocatedBytesForCurrentThread();
        var database = new SerialDatabase(game, version);
        var width = database.Width("InventoryBalanceData", version);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.InRange(width, 1, 31);
        Assert.True(allocated < SmallCatalogAllocationLimit, $"Width lookup allocated {allocated:N0} bytes.");
        Assert.Equal(width, database.Width("InventoryBalanceData", version));
    }

    [Theory]
    [InlineData("bl3", 62)]
    [InlineData("ttwl", 59)]
    public void ConcurrentFirstAccess_PublishesOneCatalog(string game, int version)
    {
        var database = new SerialDatabase(game, version);
        var firstRecords = new object[16];
        Parallel.For(0, firstRecords.Length, index => firstRecords[index] = database.Records("skills").First());
        Assert.All(firstRecords, record => Assert.Same(firstRecords[0], record));
    }
}
