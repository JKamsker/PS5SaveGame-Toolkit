using System.Globalization;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.Cyberpunk2077.Tests;

public sealed class InspectionQueryTests
{
    [Fact]
    public void LargeCollectionSearchFiltersNumericSortingAndStablePagingCoverTheWholeModel()
    {
        var built = 0;
        var values = Enumerable.Range(0, 1000).ToArray();
        var collection = new InspectionCollection<int>(new QueryableCollection("test", "Synthetic inventory",
            [new("quantity", "Quantity", IntegerSpec.UInt32), new("category", "Category")], values.Length), values,
            i => $"instance-{i:D4}", new Dictionary<string, Func<int, string>>
            {
                ["quantity"] = i => i.ToString(CultureInfo.InvariantCulture),
                ["category"] = i => i % 2 == 0 ? "Ammo" : "Unknown",
            }, i => { built++; return new TableRow($"instance-{i:D4}", new Dictionary<string, ViewNode>()); });
        Assert.Equal(0, built);
        var first = collection.Query(new QueryableCollectionQuery { SortColumn = "quantity", Descending = true, Limit = 2 });
        Assert.Collection(first.Rows, row => Assert.Equal("instance-0999", row.Key), row => Assert.Equal("instance-0998", row.Key));
        var second = collection.Query(new QueryableCollectionQuery { SortColumn = "quantity", Descending = true, Offset = 2, Limit = 2 });
        Assert.Collection(second.Rows, row => Assert.Equal("instance-0997", row.Key), row => Assert.Equal("instance-0996", row.Key));
        var match = collection.Query(new QueryableCollectionQuery { Text = "998", Filters = new Dictionary<string, string> { ["category"] = "ammo" } });
        Assert.Equal("instance-0998", Assert.Single(match.Rows).Key);
        Assert.Equal(1000, match.TotalCount);
        Assert.Equal(1, match.FilteredCount);
        Assert.Equal(5, built); // No cells were materialized for other matches or sorted rows.
        Assert.Empty(collection.Query(new QueryableCollectionQuery { Offset = int.MaxValue }).Rows);
        Assert.Equal(QueryableCollectionLimits.MaxPageSize, collection.Query(new QueryableCollectionQuery { Limit = int.MaxValue }).Rows.Count);
        Assert.Throws<ArgumentException>(() => collection.Query(new QueryableCollectionQuery { SortColumn = "missing" }));
        Assert.Throws<ArgumentException>(() => collection.Query(new QueryableCollectionQuery { Filters = new Dictionary<string, string> { ["missing"] = "x" } }));
        Assert.Throws<ArgumentException>(() => collection.Query(new QueryableCollectionQuery { Text = new string('x', QueryableCollectionLimits.MaxQueryLength + 1) }));
        Assert.Throws<ArgumentException>(() => collection.Query(new QueryableCollectionQuery { Cursor = "unknown" }));
    }
}
