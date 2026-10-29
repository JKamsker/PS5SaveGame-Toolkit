using System.Globalization;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.Cyberpunk2077.Tests;

public sealed class CollectionPagingPerformanceTests
{
    [Fact]
    public void StableKeysAreIndexedOnceAndOnlyRequestedRowsAreMaterialized()
    {
        const int count = 1000;
        var keysRead = 0;
        var rowsBuilt = 0;
        var collection = new InspectionCollection<int>(new QueryableCollection("paging", "Paging",
            [new("value", "Value", IntegerSpec.Int32)], count), Enumerable.Range(0, count).Reverse().ToArray(),
            i => { keysRead++; return i.ToString("D4", CultureInfo.InvariantCulture); },
            new Dictionary<string, Func<int, string>> { ["value"] = i => i.ToString(CultureInfo.InvariantCulture) },
            i => { rowsBuilt++; return new TableRow(i.ToString("D4", CultureInfo.InvariantCulture), new Dictionary<string, ViewNode>()); });
        _ = collection.View();
        Assert.Equal(0, keysRead);
        for (var page = 0; page < 3; page++)
        {
            var result = collection.Query(new QueryableCollectionQuery { Offset = page * 5, Limit = 5 });
            Assert.Equal(Enumerable.Range(page * 5, 5).Select(i => i.ToString("D4", CultureInfo.InvariantCulture)), result.Rows.Select(row => row.Key));
        }
        var descending = collection.Query(new QueryableCollectionQuery { Descending = true, Offset = 10, Limit = 2 });
        Assert.Equal(["0989", "0988"], descending.Rows.Select(row => row.Key));
        Assert.Empty(collection.Query(new QueryableCollectionQuery { Offset = int.MaxValue }).Rows);
        Assert.Equal(17, rowsBuilt);
        Assert.Equal(count, keysRead);
    }

    [Fact]
    public void CachedKeyOrderNeverCachesLiveValuesFiltersOrNumericSorts()
    {
        var numbers = new[] { 2, 10, 1 };
        var collection = new InspectionCollection<int>(new QueryableCollection("live", "Live",
            [new("value", "Value", IntegerSpec.Int32)], numbers.Length), [0, 1, 2],
            i => i.ToString(CultureInfo.InvariantCulture),
            new Dictionary<string, Func<int, string>> { ["value"] = i => numbers[i].ToString(CultureInfo.InvariantCulture) },
            i => new TableRow(i.ToString(CultureInfo.InvariantCulture), new Dictionary<string, ViewNode>()));
        _ = collection.Query(new QueryableCollectionQuery());
        var sorted = new QueryableCollectionQuery { SortColumn = "value" };
        Assert.Equal(["2", "0", "1"], collection.Query(sorted).Rows.Select(row => row.Key));
        numbers[0] = 20;
        Assert.Equal(["2", "1", "0"], collection.Query(sorted).Rows.Select(row => row.Key));
        var filtered = new QueryableCollectionQuery { Filters = new Dictionary<string, string> { ["value"] = "20" } };
        Assert.Equal("0", Assert.Single(collection.Query(filtered).Rows).Key);
        numbers[0] = 2; // Undo must immediately affect filters and searches too.
        Assert.Empty(collection.Query(filtered).Rows);
        Assert.Equal("0", Assert.Single(collection.Query(new QueryableCollectionQuery { Text = "2" }).Rows).Key);
    }
}
