using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.SuckerPunch.Tests;

public sealed class CollectionQueryTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EverySortDirectionAndPageMatchesTheCompleteAuthoredModel(bool yotei)
    {
        using var data = new CollectionPerformanceData(yotei);
        string?[] columns = yotei ? [null, "id", "category", "a", "b", "state"] :
            [null, "raw", "reference", "category", "location", "mapping", "state", "icon"];
        foreach (var column in columns)
        foreach (var descending in new[] { false, true })
        {
            var expected = Expected(data, yotei, column, descending);
            var actual = new List<string>();
            const int pageSize = 73;
            for (var offset = 0; offset < expected.Length; offset += pageSize)
            {
                var page = data.Query(new() { SortColumn = column, Descending = descending, Offset = offset, Limit = pageSize });
                Assert.Equal(expected.Length, page.TotalCount);
                Assert.Equal(expected.Length, page.FilteredCount);
                Assert.Equal(expected.Skip(offset).Take(pageSize), page.Rows.Select(row => row.Key));
                actual.AddRange(page.Rows.Select(row => row.Key));
                foreach (var row in page.Rows)
                foreach (var field in row.Cells.Values.OfType<FieldNode>())
                    Assert.Equal(field.Value, data.Document.GetValue(field.ValueId));
            }
            Assert.Equal(expected, actual);
            Assert.Empty(data.Query(new() { SortColumn = column, Descending = descending, Offset = int.MaxValue, Limit = 1 }).Rows);
        }
        Assert.Equal(data.Original, Assert.Single(data.Document.Serialize()).Content.ToArray());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SearchAndMutableFiltersInvalidateTheCachedResultWithoutChangingPageMembership(bool yotei)
    {
        using var data = new CollectionPerformanceData(yotei);
        var rawColumn = yotei ? "id" : "raw";
        var filters = new Dictionary<string, string>(StringComparer.Ordinal) { ["state"] = "Unknown" };
        var query = new QueryableCollectionQuery { Limit = 10, Text = yotei ? "Gear00" : "ArmorTest00", Filters = filters };
        var expected = data.Rows.Select(row => row.Name).Where(name => name.Contains(query.Text, StringComparison.OrdinalIgnoreCase))
            .Order(yotei ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal).ToArray();
        Assert.True(expected.Length > query.Limit);
        var first = data.Query(query);
        Assert.Equal(expected.Length, first.FilteredCount);
        Assert.Equal(expected.Take(query.Limit), first.Rows.Select(row => row.Key));
        var next = data.Query(query with { Offset = 10, Limit = 7, Filters = new Dictionary<string, string> { ["state"] = "Unknown" } });
        Assert.Equal(expected.Skip(10).Take(7), next.Rows.Select(row => row.Key));
        // Mutate the exact dictionary instance previously passed to Query.
        filters[rawColumn] = expected[15];
        Assert.Equal(expected[15], Assert.Single(data.Query(query).Rows).Key);
        filters[rawColumn] = expected[16];
        Assert.Equal(expected[16], Assert.Single(data.Query(query).Rows).Key);
        filters["state"] = "Missing";
        var empty = data.Query(query);
        Assert.Equal(0, empty.FilteredCount);
        Assert.Empty(empty.Rows);
        filters.Clear();
        Assert.Equal(expected.Take(10), data.Query(query).Rows.Select(row => row.Key));
        // Text is part of the criteria too, including after an empty match set.
        Assert.Empty(data.Query(query with { Text = "no-such-identifier" }).Rows);
        Assert.Equal(data.Rows.Count, data.Query(query with { Text = null }).FilteredCount);
        Assert.Equal(expected.Reverse().Take(10), data.Query(query with { Descending = true }).Rows.Select(row => row.Key));
        Assert.Equal(data.Original, Assert.Single(data.Document.Serialize()).Content.ToArray());
    }

    private static string[] Expected(CollectionPerformanceData data, bool yotei, string? column, bool descending)
    {
        var rows = data.Rows;
        if (!yotei)
        {
            // Unknown reference/category/mapping/state ties retain physical-offset order; descending reverses it.
            var order = column is "category" or "mapping" or "state" or "location"
                ? rows.OrderBy(row => row.Offset) : rows.OrderBy(row => row.Name, StringComparer.Ordinal);
            return (descending ? order.Reverse() : order).Select(row => row.Name).ToArray();
        }
        if (column is "a" or "b")
        {
            var order = descending ? rows.OrderByDescending(row => column == "a" ? row.A : row.B) :
                rows.OrderBy(row => column == "a" ? row.A : row.B);
            return order.ThenBy(row => row.Name, StringComparer.Ordinal).Select(row => row.Name).ToArray();
        }
        if (column == "category")
        {
            var order = descending ? rows.OrderByDescending(row => Family(row.Name), StringComparer.OrdinalIgnoreCase) :
                rows.OrderBy(row => Family(row.Name), StringComparer.OrdinalIgnoreCase);
            return order.ThenBy(row => row.Name, StringComparer.Ordinal).Select(row => row.Name).ToArray();
        }
        if (column == "state") return rows.Select(row => row.Name).Order(StringComparer.Ordinal).ToArray();
        return (descending ? rows.OrderByDescending(row => row.Name, StringComparer.OrdinalIgnoreCase) :
            rows.OrderBy(row => row.Name, StringComparer.OrdinalIgnoreCase)).ThenBy(row => row.Name, StringComparer.Ordinal)
            .Select(row => row.Name).ToArray();
    }

    // Authored registry uses GearNNNN (Other), HalfShog101 (Mask) and SwordSwingFxOnryo (Other).
    private static string Family(string name) => name == "HalfShog101" ? "Mask" : "Other";
}
