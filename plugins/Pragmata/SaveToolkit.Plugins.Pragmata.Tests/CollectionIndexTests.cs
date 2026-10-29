using SaveToolkit.Plugins.Pragmata.Views;
using SaveToolkit.Sdk.Collections;

namespace SaveToolkit.Plugins.Pragmata.Tests;

public sealed class CollectionIndexTests
{
    [Fact]
    public void IndexRetainsOneCriteriaSnapshotAndInvalidatesProjectionExplicitly()
    {
        var index = new CollectionIndex<int>();
        var descriptions = 0;
        var selections = 0;
        int[] Describe() { descriptions++; return [1, 2, 3]; }
        IEnumerable<int> Select(IEnumerable<int> rows, QueryableCollectionQuery query)
        {
            selections++;
            return query.Descending ? rows.Reverse() : rows;
        }
        var filters = new Dictionary<string, string> { ["a"] = "1", ["b"] = "2" };
        var query = new QueryableCollectionQuery { Filters = filters };
        Assert.Equal([1, 2, 3], index.Match(query, Describe, Select));
        _ = index.Match(query with { Offset = 20, Limit = 1, Filters = new Dictionary<string, string> { ["b"] = "2", ["a"] = "1" } }, Describe, Select);
        Assert.Equal(1, descriptions);
        Assert.Equal(1, selections);
        filters["a"] = "changed";
        _ = index.Match(query, Describe, Select);
        Assert.Equal(2, selections);
        _ = index.Match(query with { Text = "search" }, Describe, Select);
        _ = index.Match(query with { SortColumn = "other" }, Describe, Select);
        Assert.Equal([3, 2, 1], index.Match(query with { Descending = true }, Describe, Select));
        _ = index.Match(query, Describe, Select);
        Assert.Equal(6, selections); // Previous criteria were evicted, rather than accumulated.
        Assert.Equal(1, descriptions);
        index.Invalidate();
        _ = index.Match(query, Describe, Select);
        Assert.Equal(2, descriptions);
        Assert.Equal(7, selections);
    }
}
