using System.Globalization;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.FinalFantasy16.Tests;

public sealed class FaithStoredStackQueryTests
{
    private readonly FinalFantasy16Plugin _plugin = new();

    [Theory]
    [InlineData(null)]
    [InlineData(false)]
    [InlineData(true)]
    public void SignedBoundaryFiltersPreserveAllAllocatedRowsRegardlessOfEmptyVisibility(bool? includeEmpty)
    {
        var bytes = FaithStoredStackData.Save();
        using var document = _plugin.Open(FaithTestData.File(bytes));
        var queryable = (IQueryableDocument)document;
        var metadata = Assert.Single(queryable.Collections);
        Assert.Null(metadata.EmptyRows);
        Assert.Equal("Stored stack state", metadata.Columns.Single(column => column.Key == FaithStoredStackData.ColumnKey).Label);
        var table = Assert.Single(FaithViewNodes.All(document.BuildView()).OfType<QueryableTableNode>());
        Assert.Null(table.EmptyRows);
        foreach (var state in new[] { "Positive", "Zero", "Negative" })
        {
            var expected = Enumerable.Range(0, FaithInventory.Capacity).Where(slot => state switch
            {
                "Positive" => slot % FaithStoredStackData.CycleLength is 3 or 4,
                "Zero" => slot % FaithStoredStackData.CycleLength == 2,
                _ => slot % FaithStoredStackData.CycleLength is 0 or 1,
            }).ToArray();
            var rows = new List<TableRow>();
            // Include a page past the end to pin counts independently of returned page size.
            for (var offset = 0; offset <= expected.Length + 200; offset += 200)
            {
                var page = queryable.Query(metadata.Id, new()
                {
                    Filters = new Dictionary<string, string> { [FaithStoredStackData.ColumnKey] = state.ToLowerInvariant() },
                    IncludeEmpty = includeEmpty, Offset = offset, Limit = 200,
                });
                Assert.Equal(FaithInventory.Capacity, page.TotalCount);
                Assert.Equal(expected.Length, page.FilteredCount);
                Assert.Equal(0, page.HiddenEmptyCount);
                Assert.Equal(expected.Skip(offset).Take(200).Select(Key), page.Rows.Select(row => row.Key));
                Assert.All(page.Rows, row =>
                {
                    Assert.False(row.IsEmpty);
                    Assert.Equal(state, Assert.IsType<TextNode>(row.Cells[FaithStoredStackData.ColumnKey]).Text);
                });
                rows.AddRange(page.Rows);
            }
            Assert.Equal(expected.Select(Key), rows.Select(row => row.Key));
            foreach (var slot in expected)
                Assert.Equal(FaithStoredStackData.Quantity(slot), document.GetValue($"{Key(slot)}/quantity").AsInt32());
        }
        Assert.Equal(bytes, Assert.Single(document.Serialize()).Content.ToArray());
    }

    [Theory]
    [InlineData("stackState", false)]
    [InlineData("stackState", true)]
    [InlineData("quantity", false)]
    [InlineData("quantity", true)]
    public void StateAndSignedNumericSortsKeepNativeSlotTiesAcrossEveryPage(string column, bool descending)
    {
        var bytes = FaithStoredStackData.Save();
        using var document = _plugin.Open(FaithTestData.File(bytes));
        var queryable = (IQueryableDocument)document;
        // State ties span two quantity groups; their tie break is slot, not quantity.
        var slots = Enumerable.Range(0, FaithInventory.Capacity);
        int Group(int slot)
        {
            var cycle = slot % FaithStoredStackData.CycleLength;
            if (column == "quantity") return cycle;
            return cycle switch { 0 or 1 => 0, 3 or 4 => 1, _ => 2 };
        }
        var ordered = (descending ? slots.OrderByDescending(Group) : slots.OrderBy(Group)).ThenBy(slot => slot);
        var actual = Enumerable.Range(0, 6).SelectMany(page => queryable.Query(FaithInventoryView.CollectionId,
            new() { SortColumn = column, Descending = descending, Offset = page * 200, Limit = 200 }).Rows);
        Assert.Equal(ordered.Select(Key), actual.Select(row => row.Key));
        Assert.Equal(bytes, Assert.Single(document.Serialize()).Content.ToArray());
    }

    [Fact]
    public void StateCombinesWithTextCategoryExactQuantityAndPagingAndIsSearchableItself()
    {
        var bytes = FaithStoredStackData.Save();
        using var document = _plugin.Open(FaithTestData.File(bytes));
        var queryable = (IQueryableDocument)document;
        var query = new QueryableCollectionQuery
        {
            Text = "Unknown item 61", SortColumn = "quantity", Descending = true, Offset = 39, Limit = 3,
            Filters = new Dictionary<string, string> { [FaithStoredStackData.ColumnKey] = "Positive", ["category"] = "unmapped" },
        };
        var page = queryable.Query(FaithInventoryView.CollectionId, query);
        Assert.Equal(80, page.FilteredCount);
        Assert.Equal(["inventory/1199", "inventory/1003", "inventory/1008"], page.Rows.Select(row => row.Key));
        var exact = queryable.Query(FaithInventoryView.CollectionId, query with
        {
            Offset = 0,
            Filters = new Dictionary<string, string>(query.Filters) { ["quantity"] = "2147483647" },
        });
        Assert.Equal(40, exact.FilteredCount);
        Assert.Equal(["inventory/1004", "inventory/1009", "inventory/1014"], exact.Rows.Select(row => row.Key));
        var contradiction = queryable.Query(FaithInventoryView.CollectionId, query with
        {
            Offset = 0, Filters = new Dictionary<string, string>(query.Filters) { ["quantity"] = "0" },
        });
        Assert.Equal(0, contradiction.FilteredCount);
        Assert.Empty(contradiction.Rows);
        foreach (var state in new[] { "Positive", "Zero", "Negative" })
        {
            var search = queryable.Query(FaithInventoryView.CollectionId, new() { Text = state.ToLowerInvariant(), Limit = 1 });
            Assert.Equal(state == "Zero" ? 240 : 480, search.FilteredCount);
            Assert.Equal(state, Assert.IsType<TextNode>(Assert.Single(search.Rows).Cells[FaithStoredStackData.ColumnKey]).Text);
        }
        Assert.Equal(bytes, Assert.Single(document.Serialize()).Content.ToArray());
    }

    [Theory]
    [InlineData("de-AT")]
    [InlineData("tr-TR")]
    [InlineData("ar-SA")]
    public void EagerMainAndSecondaryUseTheSameInvariantBoundaryLabelsWithoutEmptyMarkers(string cultureName)
    {
        var originalCulture = CultureInfo.CurrentCulture;
        var originalUiCulture = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(cultureName);
            CultureInfo.CurrentUICulture = CultureInfo.CurrentCulture;
            var bytes = FaithStoredStackData.Save(eagerWithSecondary: true);
            using var document = _plugin.Open(FaithTestData.File(bytes));
            var tables = FaithViewNodes.All(document.BuildView()).OfType<TableNode>()
                .Where(table => table.Columns.Any(column => column.Key == FaithStoredStackData.ColumnKey)).ToArray();
            Assert.Equal(2, tables.Length);
            foreach (var table in tables)
            {
                Assert.Null(table.EmptyRows);
                Assert.Equal(["Negative", "Negative", "Zero", "Positive", "Positive"], table.Rows.Select(row =>
                    Assert.IsType<TextNode>(row.Cells[FaithStoredStackData.ColumnKey]).Text));
                Assert.All(table.Rows, row => Assert.False(row.IsEmpty));
                for (var slot = 0; slot < FaithStoredStackData.CycleLength; slot++)
                    Assert.Equal(FaithStoredStackData.Quantity(slot), Assert.IsType<FieldNode>(table.Rows[slot].Cells["quantity"]).Value.AsInt32());
            }
            var page = ((IQueryableDocument)document).Query(FaithInventoryView.CollectionId, new()
                { Filters = new Dictionary<string, string> { [FaithStoredStackData.ColumnKey] = "POSITIVE" } });
            Assert.Equal(["inventory/3", "inventory/4"], page.Rows.Select(row => row.Key));
            Assert.Equal("0", Assert.IsType<FieldNode>(tables[1].Rows[0].Cells["id"]).Value.AsString());
            Assert.Equal(bytes, Assert.Single(document.Serialize()).Content.ToArray());
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
            CultureInfo.CurrentUICulture = originalUiCulture;
        }
    }

    private static string Key(int slot) => "inventory/" + slot.ToString(CultureInfo.InvariantCulture);
}
