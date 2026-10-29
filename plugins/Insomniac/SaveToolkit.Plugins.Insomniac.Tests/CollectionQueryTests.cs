using System.Globalization;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.Insomniac.Tests;

public sealed class CollectionQueryTests
{
    [Fact]
    public void QueriesFilterWholeCollectionSortNumbersAndUseStableTieBreaks()
    {
        var registry = Fixture();
        var result = registry.Query("test", new() { Text = "aLpHa", SortColumn = "value", Offset = 1, Limit = 2 });
        Assert.Equal(5, result.TotalCount);
        Assert.Equal(4, result.FilteredCount);
        Assert.Equal(["b", "a"], result.Rows.Select(r => r.Key));
        Assert.Equal(["d", "a", "b", "c"], registry.Query("test", new()
        {
            Filters = new Dictionary<string, string> { ["name"] = "ALPHA" }, SortColumn = "value", Descending = true,
        }).Rows.Select(r => r.Key));
        Assert.Equal(result.Rows.Select(r => r.Key), registry.Query("test", new()
        { Text = "alpha", SortColumn = "value", Offset = 1, Limit = 2 }).Rows.Select(r => r.Key));
    }

    [Fact]
    public void QueriesBoundPagesAndRejectUnsupportedInput()
    {
        var registry = Fixture();
        Assert.Single(registry.Query("test", new() { Offset = -1, Limit = -1 }).Rows);
        Assert.Empty(registry.Query("test", new() { Offset = int.MaxValue, Limit = int.MaxValue }).Rows);
        Assert.Throws<KeyNotFoundException>(() => registry.Query("missing", new()));
        Assert.Throws<ArgumentException>(() => registry.Query("test", new() { SortColumn = "missing" }));
        Assert.Throws<ArgumentException>(() => registry.Query("test", new() { Cursor = "invalid" }));
        Assert.Throws<ArgumentException>(() => registry.Query("test", new() { Text = new string('x', 257) }));
        Assert.Throws<ArgumentException>(() => registry.Query("test", new() { Filters = new Dictionary<string, string> { ["missing"] = "x" } }));
    }

    [Fact]
    public void QueryCellsAreBuiltOnlyForTheRequestedPage()
    {
        var builds = 0;
        var registry = PagingFixture(() => builds++);
        Assert.Equal(0, builds);
        var page = registry.Query("lazy", new() { SortColumn = "value", Descending = true, Limit = 3 });
        Assert.Equal(3, builds);
        Assert.Equal(["row-0999", "row-0998", "row-0997"], page.Rows.Select(r => r.Key));
        Assert.Equal(["999", "998", "997"], page.Rows.Select(row => Assert.IsType<TextNode>(row.Cells["value"]).Text));
    }

    [Theory]
    [InlineData(-1, -1, 0, 1, 1)]
    [InlineData(0, 0, 0, 1, 1)]
    [InlineData(0, int.MaxValue, 0, 200, 200)]
    [InlineData(790, int.MaxValue, 790, 200, 200)]
    [InlineData(990, 200, 990, 200, 10)]
    [InlineData(1000, 200, 1000, 200, 0)]
    [InlineData(int.MaxValue, int.MaxValue, int.MaxValue, 200, 0)]
    public void PageBoundsLimitActualRowsAndCellBuilds(int offset, int limit, int expectedOffset, int expectedLimit, int expectedRows)
    {
        var builds = 0;
        var registry = PagingFixture(() => builds++);
        var page = registry.Query("lazy", new() { Offset = offset, Limit = limit });
        Assert.Equal(expectedOffset, page.Offset);
        Assert.Equal(expectedLimit, page.Limit);
        Assert.Equal(1000, page.TotalCount);
        Assert.Equal(1000, page.FilteredCount);
        Assert.Equal(expectedRows, page.Rows.Count);
        Assert.Equal(expectedRows, builds);
        Assert.Equal(Enumerable.Range(expectedOffset, expectedRows).Select(RowKey), page.Rows.Select(row => row.Key));
    }

    [Fact]
    public void FilteredPagesAreCompleteAndDisjointAndAnEmptyQueryBuildsNoCells()
    {
        var builds = 0;
        var registry = PagingFixture(() => builds++);
        var keys = new List<string>();
        for (var offset = 0; offset < 500; offset += 200)
        {
            var page = registry.Query("lazy", new()
            { Text = " eVeN ", SortColumn = "value", Descending = true, Offset = offset, Limit = 200 });
            Assert.Equal(1000, page.TotalCount);
            Assert.Equal(500, page.FilteredCount);
            Assert.Equal(Math.Min(200, 500 - offset), page.Rows.Count);
            keys.AddRange(page.Rows.Select(row => row.Key));
        }
        Assert.Equal(Enumerable.Range(0, 500).Reverse().Select(index => RowKey(index * 2)), keys);
        Assert.Equal(500, keys.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(500, builds);
        var exhausted = registry.Query("lazy", new() { Text = "even", Offset = 500 });
        var missing = registry.Query("lazy", new() { Text = "absent" });
        Assert.Empty(exhausted.Rows);
        Assert.Equal(500, exhausted.FilteredCount);
        Assert.Empty(missing.Rows);
        Assert.Equal(0, missing.FilteredCount);
        Assert.Equal(1000, missing.TotalCount);
        Assert.Equal(500, builds);
    }

    private static string RowKey(int index) => "row-" + index.ToString("D4", CultureInfo.InvariantCulture);

    private static RiftCollections PagingFixture(Action onBuild)
    {
        var registry = new RiftCollections();
        registry.Add("lazy", "Lazy", [new("value", "Value")], Enumerable.Range(0, 1000).Select(index =>
            new RiftRow(RowKey(index), new Dictionary<string, RiftCell>
            {
                ["value"] = new(index % 2 == 0 ? "Even" : "Odd", () =>
                {
                    onBuild();
                    return new TextNode(index.ToString(CultureInfo.InvariantCulture));
                }, index),
            })), "Synthetic paging test");
        return registry;
    }

    private static RiftCollections Fixture()
    {
        var registry = new RiftCollections();
        registry.Add("test", "Test", [new("name", "Name"), new("value", "Value")],
            new[] { ("a", "Alpha", 10), ("b", "Alpha", 2), ("c", "Alpha", 1), ("d", "Alpha", 20), ("e", "Beta", 3) }
                .Select(row => new RiftRow(row.Item1, new Dictionary<string, RiftCell>
                { ["name"] = RiftCell.TextOnly(row.Item2), ["value"] = RiftCollections.Number(row.Item3) })), "Test");
        return registry;
    }
}
