using SaveToolkit.Sdk.Collections;

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
        Assert.Equal(200, registry.Query("test", new() { Limit = int.MaxValue }).Limit);
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
        var registry = new RiftCollections();
        registry.Add("lazy", "Lazy", [new("value", "Value")], Enumerable.Range(0, 1000).Select(i =>
            new RiftRow($"row-{i:D4}", new Dictionary<string, RiftCell>
            {
                ["value"] = new(i.ToString(System.Globalization.CultureInfo.InvariantCulture), () =>
                {
                    builds++;
                    return new SaveToolkit.Sdk.Views.TextNode("Built");
                }, i),
            })), "Synthetic paging test");
        Assert.Equal(0, builds);
        var page = registry.Query("lazy", new() { SortColumn = "value", Descending = true, Limit = 3 });
        Assert.Equal(3, builds);
        Assert.Equal(["row-0999", "row-0998", "row-0997"], page.Rows.Select(r => r.Key));
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
