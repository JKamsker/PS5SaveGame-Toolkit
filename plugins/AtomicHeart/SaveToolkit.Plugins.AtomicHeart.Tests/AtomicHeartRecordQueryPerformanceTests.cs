using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.AtomicHeart.Tests;

public sealed class AtomicHeartRecordQueryPerformanceTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("class")]
    public void RepeatedPagesAllocateForThePageInsteadOfTheCompleteIndex(string? sort)
    {
        var collection = Collection(10000);
        var query = new QueryableCollectionQuery { SortColumn = sort, Limit = 20 };
        _ = collection.Query(RecordCollection.Id, query);
        var before = GC.GetAllocatedBytesForCurrentThread();
        var page = collection.Query(RecordCollection.Id, query with { Offset = 20 });
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(20, page.Rows.Count);
        Assert.True(allocated < 40_000, $"Repeated record page allocated {allocated:N0} bytes.");
    }

    [Fact]
    public void CachedCriteriaAreCopiedAndCompleteOrderingAndDetailsRemainStable()
    {
        var collection = Collection(253);
        var filters = new Dictionary<string, string> { ["kind"] = "actor" };
        var query = new QueryableCollectionQuery { Text = "sub", Filters = filters, SortColumn = "class", Descending = true, Limit = 37 };
        var expected = Enumerable.Range(0, 253).Where(i => i % 2 == 0)
            .OrderByDescending(i => "Class" + i % 3 + " | Sub", StringComparer.OrdinalIgnoreCase)
            .ThenBy(i => "id/" + i, StringComparer.Ordinal).Select(i => "id/" + i).ToArray();
        var rows = Enumerable.Range(0, 4).SelectMany(page => collection.Query(RecordCollection.Id,
            query with { Offset = page * 37 }).Rows).ToArray();
        Assert.Equal(expected, rows.Select(row => row.Key));
        Assert.All(rows, row => Assert.Equal("inspect/object/" + row.Key[3..], Assert.IsType<SectionNode>(row.Cells["details"]).LazyId));
        filters["kind"] = "component";
        Assert.Equal(126, collection.Query(RecordCollection.Id, query).FilteredCount);
        filters.Clear();
        Assert.Equal(253, collection.Query(RecordCollection.Id, query).FilteredCount);
        Assert.Empty(collection.Query(RecordCollection.Id, query with { Offset = 300 }).Rows);
        Assert.Equal(0, collection.Query(RecordCollection.Id, query with { Text = "missing" }).FilteredCount);
        Assert.Equal(127, collection.Query(RecordCollection.Id, query with { Descending = false,
            Filters = new Dictionary<string, string> { ["kind"] = "ACTOR" } }).FilteredCount);
    }

    [Fact]
    public void NumericFiltersAndSortsRetainNativeNumbersAndOrdinalTies()
    {
        var collection = Collection(253);
        var page = collection.Query(RecordCollection.Id, new() { SortColumn = "offset", Descending = true, Limit = 3 });
        Assert.Equal(["2520", "2510", "2500"], page.Rows.Select(row => Assert.IsType<TextNode>(row.Cells["offset"]).Text));
        var filtered = collection.Query(RecordCollection.Id, new() { Filters = new Dictionary<string, string> { ["bytes"] = "9" }, SortColumn = "name", Limit = 200 });
        Assert.Equal(85, filtered.FilteredCount);
        Assert.Equal(Enumerable.Range(0, 253).Where(i => i % 3 == 0).Select(i => "id/" + i).Order(StringComparer.Ordinal), filtered.Rows.Select(row => row.Key));
        Assert.Equal(0, collection.Query(RecordCollection.Id, new() { Filters = new Dictionary<string, string> { ["bytes"] = "09" } }).FilteredCount);
    }

    private static RecordCollection Collection(int count) => new(new([], [], Enumerable.Range(0, count).Select(i =>
        new ObjectRecord("id/" + i, i % 2 == 0 ? "actor" : "component", "Owner", "Name", "Class" + i % 3, "Sub",
            new(i * 10, 9 - i % 3), default, [], [], default, [])).ToArray(), []));
}
