using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.Gearbox.Tests;

[Collection(typeof(AllocationTestCollection))]
public sealed class SerialDatabaseLoadingTests
{
    private const long SmallCatalogAllocationLimit = 2 * 1024 * 1024;

    [Theory]
    [InlineData("bl3", 62)]
    [InlineData("ttwl", 59)]
    public void Initialization_DoesNotMaterializeCatalogs(string game, int version)
    {
        var allocated = AllocationProbe.Measure(() => new SerialDatabase(game, version), out var database);
        Assert.Equal(version, database.MaximumVersion);
        Assert.Equal("gearbox." + game, database.Pack.Id);
        allocated.AssertLessThan(SmallCatalogAllocationLimit, $"Manifest initialization allocated {allocated.MinimumBytes:N0} bytes.");
    }

    [Theory]
    [InlineData("bl3", 62)]
    [InlineData("ttwl", 59)]
    public void SmallCatalog_IsLoadedOnceWithoutMaterializingUnrelatedTables(string game, int version)
    {
        var allocated = AllocationProbe.Measure(() =>
        {
            var database = new SerialDatabase(game, version);
            var records = database.Records("skills").ToArray();
            return (database, records);
        }, out var measured);
        var database = measured.database;
        var records = measured.records;
        Assert.NotEmpty(records);
        allocated.AssertLessThan(SmallCatalogAllocationLimit, $"One small catalog allocated {allocated.MinimumBytes:N0} bytes.");
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
        var allocated = AllocationProbe.Measure(() =>
        {
            var database = new SerialDatabase(game, version);
            var width = database.Width("InventoryBalanceData", version);
            return (database, width);
        }, out var measured);
        var database = measured.database;
        var width = measured.width;
        Assert.InRange(width, 1, 31);
        allocated.AssertLessThan(SmallCatalogAllocationLimit, $"Width lookup allocated {allocated.MinimumBytes:N0} bytes.");
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
