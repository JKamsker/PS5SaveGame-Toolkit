using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Core.Tests.SdkHelpers;

public sealed class InMemoryCollectionAllocationTests(ITestOutputHelper output)
{
    private const int PageSize = 50;
    private const int MeasuredQueries = 5;
    private const long BytesPerThousandRows = 80_000;
    private const long StreamingBytesPerThousandRows = 160_000;
    private static readonly IReadOnlyDictionary<string, ViewNode> EmptyCells = new Dictionary<string, ViewNode>();

    [Theory]
    [InlineData(1_000, "page")]
    [InlineData(2_048, "page")]
    [InlineData(1_000, "search")]
    [InlineData(2_048, "search")]
    [InlineData(1_000, "filter")]
    [InlineData(2_048, "filter")]
    [InlineData(1_000, "sort")]
    [InlineData(2_048, "sort")]
    [InlineData(1_000, "empty")]
    [InlineData(2_048, "empty")]
    [InlineData(1_000, "stream")]
    [InlineData(2_048, "stream")]
    public void QueryAllocationsStayWithinPerThousandRowBudget(int count, string kind)
    {
        // Model, native keys, query and adapters are prepared outside the measurement.
        // Cells are deliberately empty to isolate helper overhead from plugin rendering.
        var items = Enumerable.Range(0, count).Reverse().ToArray();
        var keys = Enumerable.Range(0, count).Select(i => $"row/{i:D6}").ToArray();
        var metadata = new QueryableCollection("items", "Items", [new("name", "Name")], count);
        var rendered = 0;
        var collection = new InMemoryCollection<int>(metadata,
            () => kind == "stream" ? Stream(items) : items, i => keys[i],
            [new("name") { SearchText = _ => "row", FilterText = _ => "row",
                Comparer = Comparer<int>.Create((a, b) => (a % 7).CompareTo(b % 7)) }],
            i => { rendered++; return new(keys[i], EmptyCells); },
            kind == "empty" ? new() { IsEmpty = i => i % 2 == 0 } : null);
        var query = new QueryableCollectionQuery
        {
            Text = kind == "search" ? "ROW" : null,
            Filters = kind == "filter" ? new Dictionary<string, string> { ["name"] = "ROW" }
                : new Dictionary<string, string>(),
            SortColumn = kind == "sort" ? "name" : null,
            IncludeEmpty = kind == "empty" ? false : null,
            Offset = PageSize,
            Limit = PageSize,
        };
        for (var i = 0; i < MeasuredQueries; i++) collection.Query(query);

        QueryableCollectionPage? page = null;
        rendered = 0;
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < MeasuredQueries; i++) page = collection.Query(query);
        var bytesPerQuery = (GC.GetAllocatedBytesForCurrentThread() - before) / MeasuredQueries;

        Assert.NotNull(page);
        Assert.Equal(count, page.TotalCount);
        Assert.Equal(kind == "empty" ? count / 2 : count, page.FilteredCount);
        Assert.Equal(kind == "empty" ? count / 2 : 0, page.HiddenEmptyCount);
        Assert.Equal(PageSize, page.Rows.Count);
        Assert.Equal(PageSize * MeasuredQueries, rendered);
        var perThousand = bytesPerQuery * 1_000 / count;
        var budget = kind == "stream" ? StreamingBytesPerThousandRows : BytesPerThousandRows;
        output.WriteLine($"{kind}, {count} rows: {bytesPerQuery:N0} B/query, {perThousand:N0} B/1,000 rows.");
        Assert.True(perThousand <= budget,
            $"{kind}: {perThousand:N0} B/1,000 rows exceeds {budget:N0} B (query: {bytesPerQuery:N0} B).");
    }

    private static IEnumerable<int> Stream(int[] items)
    {
        foreach (var item in items) yield return item;
    }
}
