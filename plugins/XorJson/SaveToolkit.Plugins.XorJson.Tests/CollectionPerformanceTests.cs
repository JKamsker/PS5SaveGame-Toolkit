using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.XorJson.Tests;

[Collection(AllocationMeasurements.Name)]
public sealed class CollectionPerformanceTests
{
    private const int RowCount = 10_000;
    private const int PageBudget = 32 * 1024;

    [Fact]
    public void FirstViewAndMetadataDoNotMaterializeRawArrayCells()
    {
        using var document = OpenRows();
        var before = GC.GetAllocatedBytesForCurrentThread();
        _ = document.BuildView();
        var metadata = ((IQueryableDocument)document).Collections;
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.InRange(allocated, 1, 128 * 1024);
        Assert.Equal(RowCount, Assert.Single(metadata).TotalCount);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("999", null)]
    [InlineData(null, "value")]
    [InlineData(null, "details")]
    public void WarmSingleRowPagesHaveBoundedAllocations(string? text, string? sort)
    {
        using var document = OpenRows();
        var collection = Assert.IsAssignableFrom<IQueryableDocument>(document);
        var id = Assert.Single(collection.Collections).Id;
        var query = new QueryableCollectionQuery { Text = text, SortColumn = sort, Offset = 1, Limit = 1 };
        _ = collection.Query(id, query);
        var before = GC.GetAllocatedBytesForCurrentThread();
        var page = collection.Query(id, query);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Single(page.Rows);
        Assert.InRange(allocated, 1, PageBudget);
        Assert.Equal(text is null ? RowCount : 19, page.FilteredCount);
    }

    [Fact]
    public void WarmRawPathFiltersDoNotRebuildEveryTypedPath()
    {
        using var document = OpenRows();
        var collection = Assert.IsAssignableFrom<IQueryableDocument>(document);
        var id = Assert.Single(collection.Collections).Id;
        var query = new QueryableCollectionQuery
        {
            Filters = new Dictionary<string, string> { ["details"] = "$.member(\"rows\",0).element(9999)" },
            Limit = 1,
        };
        _ = collection.Query(id, query);
        var before = GC.GetAllocatedBytesForCurrentThread();
        var page = collection.Query(id, query);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal("value/10001", Assert.Single(page.Rows).Key);
        Assert.Equal(1, page.FilteredCount);
        Assert.InRange(allocated, 1, PageBudget);
        var removal = document.InvokeAction("json/remove/2", new Dictionary<string, FieldValue>());
        Assert.Empty(collection.Query(id, query).Rows);
        Assert.Equal("value/10001", Assert.Single(collection.Query(id, query with
        {
            Filters = new Dictionary<string, string> { ["details"] = "$.member(\"rows\",0).element(9998)" },
        }).Rows).Key);
        _ = document.RestoreMemento(removal.Revert!);
        Assert.Equal("value/10001", Assert.Single(collection.Query(id, query).Rows).Key);
    }

    [Fact]
    public void CombinedQueriesRetainLexicalOrderStableTiesCountsAndEndPages()
    {
        using var document = XorJsonTests.Open(XorJsonTests.Encode("{\"rows\":[\"a\",\"B\",\"a\",\"C\",\"A\"," +
            string.Join(',', Enumerable.Repeat("\"other\"", 100)) + "]}"));
        var collection = Assert.IsAssignableFrom<IQueryableDocument>(document);
        var id = Assert.Single(collection.Collections).Id;
        var query = new QueryableCollectionQuery { Text = "A", SortColumn = "value", Limit = 1 };
        Assert.Equal(["value/6", "value/2", "value/4"], Enumerable.Range(0, 3)
            .Select(offset => Assert.Single(collection.Query(id, query with { Offset = offset }).Rows).Key));
        Assert.Equal(["value/2", "value/4", "value/6"], Enumerable.Range(0, 3)
            .Select(offset => Assert.Single(collection.Query(id, query with { Offset = offset, Descending = true }).Rows).Key));
        var filtered = query with { Filters = new Dictionary<string, string> { ["value"] = "a" }, Limit = 200 };
        Assert.Equal(3, collection.Query(id, filtered).FilteredCount);
        Assert.Equal(105, collection.Query(id, filtered).TotalCount);
        Assert.Empty(collection.Query(id, filtered with { Offset = int.MaxValue }).Rows);
        Assert.Equal("value/106", Assert.Single(collection.Query(id, new() { Descending = true, Limit = 1 }).Rows).Key);
        Assert.Equal("value/2", Assert.Single(collection.Query(id, new() { Offset = -1, Limit = 0 }).Rows).Key);

        Assert.True(document.ApplyEdits([new("value/2", FieldValue.Of("z"))]).Succeeded);
        Assert.Equal(["value/6", "value/4"], collection.Query(id, query with { Limit = 200 }).Rows.Select(row => row.Key));
        Assert.Equal("value/2", Assert.Single(collection.Query(id, new() { Text = "z", SortColumn = "value" }).Rows).Key);
        var remove = document.InvokeAction("json/remove/4", new Dictionary<string, FieldValue>());
        Assert.Equal("value/6", Assert.Single(collection.Query(id, query).Rows).Key);
        _ = document.RestoreMemento(remove.Revert!);
        Assert.Equal(["value/6", "value/4"], collection.Query(id, query with { Limit = 200 }).Rows.Select(row => row.Key));
    }

    private static SaveToolkit.Sdk.ISaveDocument OpenRows() => XorJsonTests.Open(XorJsonTests.Encode(
        "{\"rows\":[" + string.Join(',', Enumerable.Range(0, RowCount)) + "]}"));
}


