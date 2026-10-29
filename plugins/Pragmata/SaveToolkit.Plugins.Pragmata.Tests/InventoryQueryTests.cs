using SaveToolkit.Plugins.Pragmata.Views;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.Pragmata.Tests;

public sealed class InventoryQueryTests
{
    private const int InventorySize = 251;

    [Theory]
    [InlineData(false, 1)]
    [InlineData(true, 1)]
    [InlineData(false, 73)]
    [InlineData(true, 73)]
    [InlineData(false, 200)]
    [InlineData(true, 200)]
    public void QuantityOrderAndOrdinalTieBreakAreCorrectAcrossEveryPage(bool descending, int limit)
    {
        var bytes = SyntheticInventory.Create(InventorySize);
        using var document = new PragmataDocument("data000.bin", bytes);
        _ = document.BuildView();
        // The authored values, rather than queried cells, define the complete expected ordering.
        var expected = document.Layout.Items.Select((item, index) =>
            (Key: $"inventory/record-at-{item.Offset:x}", Quantity: index % 11)).ToArray();
        var ordered = descending ? expected.OrderByDescending(item => item.Quantity) : expected.OrderBy(item => item.Quantity);
        expected = ordered.ThenBy(item => item.Key, StringComparer.Ordinal).ToArray();
        var query = new QueryableCollectionQuery { Limit = limit, SortColumn = "quantity", Descending = descending };
        var actual = new List<(string Key, int Quantity)>();
        for (var offset = 0; offset < InventorySize; offset += limit)
        {
            var page = document.Query(InventoryCollections.CollectionId, query with { Offset = offset });
            Assert.Equal(InventorySize, page.TotalCount);
            Assert.Equal(InventorySize, page.FilteredCount);
            Assert.Equal(Math.Min(limit, InventorySize - offset), page.Rows.Count);
            actual.AddRange(page.Rows.Select(row => (row.Key, ((FieldNode)row.Cells["quantity"]).Value.AsInt32())));
        }
        Assert.Equal(expected, actual);
        Assert.Empty(document.Query(InventoryCollections.CollectionId, query with { Offset = InventorySize }).Rows);
        Assert.Equal(bytes, document.Serialize().Single().Content.ToArray());
    }

    [Fact]
    public void CombinedFiltersPageEveryMatchingRowAndSearchReadsLiveValues()
    {
        var bytes = SyntheticInventory.Create(InventorySize);
        using var document = new PragmataDocument("data000.bin", bytes);
        _ = document.BuildView();
        var query = new QueryableCollectionQuery
        {
            Limit = 7, Text = "UNMAPPED", SortColumn = "quantity",
            Filters = new Dictionary<string, string> { ["quantity"] = "3", ["category"] = "UNMAPPED" },
        };
        var expected = document.Layout.Items.Where((_, index) => index % 11 == 3)
            .Select(item => $"inventory/record-at-{item.Offset:x}").Order(StringComparer.Ordinal).ToArray();
        Assert.Equal(23, expected.Length);
        var rows = new List<TableRow>();
        for (var offset = 0; offset < expected.Length; offset += query.Limit)
        {
            var page = document.Query(InventoryCollections.CollectionId, query with { Offset = offset });
            Assert.Equal(InventorySize, page.TotalCount);
            Assert.Equal(expected.Length, page.FilteredCount);
            Assert.Equal(Math.Min(query.Limit, expected.Length - offset), page.Rows.Count);
            rows.AddRange(page.Rows);
        }
        Assert.Equal(expected, rows.Select(row => row.Key));
        var field = Assert.IsType<FieldNode>(rows[0].Cells["quantity"]);
        Assert.True(document.ApplyEdits([new(field.ValueId, Sdk.Values.FieldValue.Of(99L))]).Succeeded);
        var filtered = document.Query(InventoryCollections.CollectionId, query);
        Assert.Equal(expected.Length - 1, filtered.FilteredCount);
        Assert.DoesNotContain(filtered.Rows, row => row.Key == rows[0].Key);
        var live = document.Query(InventoryCollections.CollectionId, new() { Text = "99", Filters = new Dictionary<string, string> { ["quantity"] = "99" } });
        Assert.Equal(rows[0].Key, Assert.Single(live.Rows).Key);
        var missing = document.Query(InventoryCollections.CollectionId, query with { Text = "no-such-record" });
        Assert.Equal(0, missing.FilteredCount);
        Assert.Empty(missing.Rows);
    }
}
