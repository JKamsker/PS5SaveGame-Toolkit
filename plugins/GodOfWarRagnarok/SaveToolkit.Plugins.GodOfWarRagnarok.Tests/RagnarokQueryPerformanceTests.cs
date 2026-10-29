using System.Globalization;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.GodOfWarRagnarok.Tests;

public sealed class RagnarokQueryPerformanceTests
{
    private static readonly string[] SortColumns = ["container", "name", "id", "category", "quantity", "metadata", "source", "offset", "icon", "inspect", "restore"];
    private static readonly bool[] SortDirections = [false, true];
    [Theory]
    [InlineData("page")]
    [InlineData("search")]
    [InlineData("numeric-search")]
    [InlineData("filter")]
    [InlineData("numeric-sort")]
    [InlineData("text-sort")]
    public void SmallPagesDoNotMaterializeEveryLiveRow(string kind)
    {
        using var document = RagnarokPerformanceFixture.Open();
        var query = kind switch
        {
            "search" => new QueryableCollectionQuery { Text = "  Hacksilver  ", Limit = 1 },
            "numeric-search" => new() { Text = "4294967295", Limit = 1 },
            "filter" => new() { Filters = new Dictionary<string, string> { ["quantity"] = "10" }, Limit = 1 },
            "numeric-sort" => new() { SortColumn = "quantity", Descending = true, Limit = 1 },
            "text-sort" => new() { SortColumn = "name", Limit = 1 },
            _ => new() { Offset = 800, Limit = 1 },
        };
        _ = document.Query("resources", query);
        var before = GC.GetAllocatedBytesForCurrentThread();
        var page = document.Query("resources", query);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Single(page.Rows);
        Assert.InRange(allocated, 0, 40_000);
    }

    public static TheoryData<string, bool> SortCases => new(
        from column in SortColumns
        from reverse in SortDirections
        select (column, reverse));

    [Theory]
    [MemberData(nameof(SortCases))]
    public void EveryColumnSortsBeforePagingWithAscendingIdentityTies(string column, bool descending)
    {
        using var document = RagnarokPerformanceFixture.Open();
        var native = ReadAll(document, new());
        Assert.Equal(RagnarokPerformanceFixture.RowCount, native.Length);
        for (var i = 0; i < native.Length; i++)
        {
            Assert.Equal(RagnarokPerformanceFixture.Quantity(i), Assert.IsType<FieldNode>(native[i].Cells["quantity"]).Value.AsUInt32());
            Assert.Equal($"0x{RagnarokPerformanceFixture.RowsOffset + i * 16:X8}", Assert.IsType<TextNode>(native[i].Cells["offset"]).Text);
        }
        var comparer = Comparer<TableRow>.Create((left, right) =>
        {
            var a = Text(left, column);
            var b = Text(right, column);
            var primary = column is "quantity" or "metadata"
                ? ulong.Parse(a, CultureInfo.InvariantCulture).CompareTo(ulong.Parse(b, CultureInfo.InvariantCulture))
                : StringComparer.OrdinalIgnoreCase.Compare(a, b);
            return primary == 0 ? StringComparer.Ordinal.Compare(left.Key, right.Key) : descending ? -primary : primary;
        });
        var expected = native.Order(comparer).Select(row => row.Key).ToArray();
        Assert.Equal(expected, ReadAll(document, new() { SortColumn = column, Descending = descending }).Select(row => row.Key));
    }

    [Fact]
    public void LiveQuantitySearchFiltersAndSortObserveEditsAndRestores()
    {
        using var document = RagnarokPerformanceFixture.Open();
        var rows = ReadAll(document, new());
        var target = rows[4]; // Independently authored UInt32.MaxValue counter.
        var field = Assert.IsType<FieldNode>(target.Cells["quantity"]);
        Assert.True(document.ApplyEdits([new(field.ValueId, FieldValue.Of(314159265L))]).Succeeded);
        Assert.Equal(target.Key, Assert.Single(document.Query("resources", new() { Text = "  314159265  " }).Rows).Key);
        var filters = new Dictionary<string, string> { ["quantity"] = "314159265", ["name"] = "hAcKsIlVeR" };
        Assert.Equal(target.Key, Assert.Single(document.Query("resources", new() { Filters = filters }).Rows).Key);
        filters["quantity"] = "0314159265";
        Assert.Empty(document.Query("resources", new() { Filters = filters }).Rows);
        Assert.True(document.ApplyEdits([new(field.ValueId, field.Value)]).Succeeded);
        Assert.Empty(document.Query("resources", new() { Text = "314159265" }).Rows);
        Assert.Contains(target.Key, ReadAll(document, new() { Text = "4294967295", SortColumn = "quantity", Descending = true }).Select(row => row.Key));
        var sorted = ReadAll(document, new() { SortColumn = "quantity" });
        Assert.Equal(rows.OrderBy(row => ((FieldNode)row.Cells["quantity"]).Value.AsUInt32()).ThenBy(row => row.Key, StringComparer.Ordinal).Select(row => row.Key),
            sorted.Select(row => row.Key));
        Assert.Equal(RagnarokPerformanceFixture.Bytes(), Assert.Single(document.Serialize()).Content.ToArray());
    }

    private static TableRow[] ReadAll(RagnarokDocument document, QueryableCollectionQuery query)
    {
        var rows = new List<TableRow>();
        while (true)
        {
            var page = document.Query("resources", query with { Offset = rows.Count, Limit = 37 });
            Assert.Equal(RagnarokPerformanceFixture.RowCount, page.TotalCount);
            rows.AddRange(page.Rows);
            if (rows.Count == page.FilteredCount) return rows.ToArray();
            Assert.NotEmpty(page.Rows);
        }
    }

    private static string Text(TableRow row, string column) => row.Cells.GetValueOrDefault(column) switch
    {
        TextNode text => text.Text,
        FieldNode field when field.Value.Kind == FieldValueKind.Integer => field.Value.AsInteger().ToString(CultureInfo.InvariantCulture),
        FieldNode field => field.Value.AsString(),
        _ => "",
    };
}
