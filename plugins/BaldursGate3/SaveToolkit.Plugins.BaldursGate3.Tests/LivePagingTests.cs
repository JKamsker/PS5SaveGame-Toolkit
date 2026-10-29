using System.Text.Json;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.BaldursGate3.Tests;

public sealed class LivePagingTests
{
    private const int StatsRows = 2000;
    private const int AbilitiesPerRow = 6;
    private const int StatsRowBytes = 36;

    [Fact]
    public void DefaultPagesAllocateForTheRequestedRowsEvenAtTheEndOfALargeCollection()
    {
        var stats = new byte[StatsRows * StatsRowBytes];
        for (var row = 0; row < StatsRows; row++)
            for (var ability = 0; ability < AbilitiesPerRow; ability++)
                LsmfFixture.U32(stats, row * StatsRowBytes + 8 + ability * sizeof(int), 10);
        using var document = Open(LsmfFixture.Create(("game.stats.v3.StatsComponent", StatsRowBytes, StatsRows, stats)));
        var query = new QueryableCollectionQuery { Offset = StatsRows * AbilitiesPerRow - 10, Limit = 10 };
        _ = document.Query("live/0/0/values", query);
        var allocated = GC.GetAllocatedBytesForCurrentThread();
        for (var page = 0; page < 20; page++) _ = document.Query("live/0/0/values", query);
        allocated = GC.GetAllocatedBytesForCurrentThread() - allocated;
        Assert.True(allocated < 1024 * 1024, $"Twenty ten-row pages allocated {allocated:N0} bytes.");
        var result = document.Query("live/0/0/values", query);
        Assert.Equal(StatsRows * AbilitiesPerRow, result.FilteredCount);
        Assert.Equal(10, result.Rows.Count);
        Assert.EndsWith("Stats/1999/Charisma", result.Rows[^1].Key, StringComparison.Ordinal);
    }

    [Fact]
    public void DefaultAndWhitespacePagesPreserveOrderCountsAndContentsForEveryCollection()
    {
        using var document = Open(LiveNewAgeFixture.Create());
        foreach (var collection in document.Collections.Where(c => c.Id.StartsWith("live/", StringComparison.Ordinal)))
        {
            var complete = document.Query(collection.Id, new QueryableCollectionQuery { Limit = 200 });
            var paged = new List<string>();
            for (var offset = 0; offset < complete.FilteredCount; offset += 2)
            {
                var page = document.Query(collection.Id, new QueryableCollectionQuery { Offset = offset, Limit = 2, Text = "  " });
                Assert.Equal(complete.TotalCount, page.TotalCount);
                Assert.Equal(complete.FilteredCount, page.FilteredCount);
                paged.AddRange(page.Rows.Select(row => JsonSerializer.Serialize(row)));
            }
            Assert.Equal(complete.Rows.Select(row => JsonSerializer.Serialize(row)), paged);
            Assert.Empty(document.Query(collection.Id, new QueryableCollectionQuery { Offset = 10_000, Limit = 2 }).Rows);
        }
    }

    [Fact]
    public void DefaultSearchFilterAndSortPagesAllReadCurrentEditedValues()
    {
        using var document = Open(LiveNewAgeFixture.Create());
        const string collection = "live/0/0/values";
        var first = document.Query(collection, new QueryableCollectionQuery { Limit = 1 });
        var field = Assert.IsType<FieldNode>(Assert.Single(first.Rows).Cells["value"]);
        Assert.True(document.ApplyEdits([new(field.ValueId, FieldValue.Of(2L))]).Succeeded);
        QueryableCollectionQuery[] queries =
        [
            new() { Limit = 1 },
            new() { Limit = 200, Text = "  Stacks  " },
            new() { Limit = 200, Filters = new Dictionary<string, string> { ["value"] = "2" } },
            new() { Limit = 200, SortColumn = "value", Descending = true },
        ];
        foreach (var query in queries)
        {
            var page = document.Query(collection, query);
            var row = Assert.Single(page.Rows, row => row.Key == first.Rows[0].Key);
            Assert.Equal(2, Assert.IsType<FieldNode>(row.Cells["value"]).Value.AsInt32());
        }
    }

    private static BaldursGate3Document Open(byte[] blob) => Assert.IsType<BaldursGate3Document>(
        new BaldursGate3Plugin().Open(MemorySaveSet.SingleFile("Globals.lsf", LsmfFixture.Resource(blob))));
}
