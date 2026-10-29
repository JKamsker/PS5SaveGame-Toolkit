using System.Globalization;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.Insomniac.Tests;

public sealed class CollectionAllocationTests
{
    private const int RowCount = 20_000;
    private const int PageSize = 10;
    private const long PageAllocationBudget = 32_768;

    [Fact]
    public void FirstColumnSortHasBoundedWorkingStorageAndBuildsOnlyThePage()
    {
        const long sortAllocationBudget = 1_000_000;
        var builds = 0;
        var registry = Fixture(() => builds++);
        var query = new QueryableCollectionQuery { SortColumn = "value", Descending = true, Offset = 500, Limit = PageSize };
        var before = GC.GetAllocatedBytesForCurrentThread();
        var page = registry.Query("large", query);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.True(allocated <= sortAllocationBudget, $"First sort allocated {allocated:N0} bytes.");
        Assert.Equal(PageSize, builds);
        Assert.Equal(RowCount, page.FilteredCount);
        Assert.Equal(Enumerable.Range(0, RowCount).OrderByDescending(index => index % 101).ThenBy(index => index)
            .Skip(query.Offset).Take(PageSize).Select(Key), page.Rows.Select(row => row.Key));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(" eVeN ")]
    public void FirstNativePageAllocatesOnlyPageCells(string? text)
    {
        var builds = 0;
        var registry = Fixture(() => builds++);
        var query = new QueryableCollectionQuery { Text = text, Limit = PageSize };

        var before = GC.GetAllocatedBytesForCurrentThread();
        var page = registry.Query("large", query);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.True(allocated <= PageAllocationBudget, $"First page allocated {allocated:N0} bytes.");
        Assert.Equal(PageSize, builds);
        Assert.Equal(text is null ? RowCount : RowCount / 2, page.FilteredCount);
        Assert.Equal(Enumerable.Range(0, PageSize).Select(index => Key(text is null ? index : index * 2)),
            page.Rows.Select(row => row.Key));
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData(" eVeN ", null)]
    [InlineData(null, "value")]
    [InlineData(" eVeN ", "value")]
    public void RepeatedPagesAllocateOnlyPageCells(string? text, string? sortColumn)
    {
        var builds = 0;
        var registry = Fixture(() => builds++);
        var query = new QueryableCollectionQuery
        { Text = text, SortColumn = sortColumn, Descending = true, Offset = 500, Limit = PageSize };
        _ = registry.Query("large", query);
        builds = 0;

        var before = GC.GetAllocatedBytesForCurrentThread();
        var page = registry.Query("large", query);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.True(allocated <= PageAllocationBudget, $"Page allocated {allocated:N0} bytes; budget {PageAllocationBudget:N0}.");
        Assert.Equal(RowCount, page.TotalCount);
        Assert.Equal(text is null ? RowCount : RowCount / 2, page.FilteredCount);
        Assert.Equal(PageSize, page.Rows.Count);
        Assert.Equal(PageSize, builds);
        IEnumerable<int> expected = Enumerable.Range(0, RowCount).Where(index => text is null || index % 2 == 0);
        expected = sortColumn is null ? expected : expected.OrderByDescending(index => index % 101).ThenBy(index => index);
        Assert.Equal(expected.Skip(query.Offset).Take(PageSize).Select(Key), page.Rows.Select(row => row.Key));
    }

    [Fact]
    public void ExactFiltersAndEmptyResultsDoNotAllocatePerMatchedRow()
    {
        var builds = 0;
        var registry = Fixture(() => builds++);
        var query = new QueryableCollectionQuery
        {
            Text = " eVeN ", Filters = new Dictionary<string, string> { ["value"] = "EVEN" },
            Offset = RowCount, Limit = PageSize,
        };
        _ = registry.Query("large", query);
        var before = GC.GetAllocatedBytesForCurrentThread();
        var page = registry.Query("large", query);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.True(allocated <= PageAllocationBudget, $"Empty page allocated {allocated:N0} bytes.");
        Assert.Empty(page.Rows);
        Assert.Equal(RowCount / 2, page.FilteredCount);
        Assert.Equal(0, builds);
    }

    private static RiftCollections Fixture(Action onBuild)
    {
        var registry = new RiftCollections();
        registry.Add("large", "Large", [new("value", "Value")],
            Enumerable.Range(0, RowCount).Reverse().Select(index =>
                new RiftRow(Key(index), new Dictionary<string, RiftCell>
                {
                    ["value"] = new(index % 2 == 0 ? "Even" : "Odd", () =>
                    {
                        onBuild();
                        return new TextNode(index.ToString(CultureInfo.InvariantCulture));
                    }, index % 101),
                })), "Authored allocation regression");
        return registry;
    }

    private static string Key(int index) => "row-" + index.ToString("D5", CultureInfo.InvariantCulture);
}
