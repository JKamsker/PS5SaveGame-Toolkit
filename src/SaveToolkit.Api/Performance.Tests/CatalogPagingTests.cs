using SaveToolkit.Core.References;
using SaveToolkit.Core.Tests.References;
using SaveToolkit.Sdk.References;

namespace SaveToolkit.Api.Performance.Tests;

public sealed class CatalogPagingTests
{
    private const long WarmPageAllocationLimit = 32 * 1024;

    [Fact]
    public void RepeatedSortedPagesDoNotRematerializeTheCatalog()
    {
        const int count = 25_000;
        var records = string.Join(',', Enumerable.Range(0, count).Select(i =>
            $$"""{"id":"{{i:D6}}","name":"Item {{count - i:D6}}"}"""));
        var catalog = Load(records);
        var query = new ReferenceQuery { SortBy = ReferenceSortKey.Name, Offset = 12_000 };
        _ = catalog.Search(query);

        var before = GC.GetAllocatedBytesForCurrentThread();
        var page = catalog.Search(query);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(count, page.Total);
        Assert.Equal(ReferenceQuery.DefaultLimit, page.Items.Count);
        Assert.Equal("012999", page.Items[0].Key);
        Assert.True(allocated < WarmPageAllocationLimit, $"Warm catalog page allocated {allocated:N0} bytes.");
    }

    [Theory]
    [InlineData(ReferenceSortKey.Key, false)]
    [InlineData(ReferenceSortKey.Key, true)]
    [InlineData(ReferenceSortKey.Name, false)]
    [InlineData(ReferenceSortKey.Name, true)]
    [InlineData(ReferenceSortKey.Category, false)]
    [InlineData(ReferenceSortKey.Category, true)]
    public void CachedOrderingPreservesNullsTiesFiltersAndPaging(ReferenceSortKey sort, bool descending)
    {
        var catalog = Load("""
            {"id":"b","name":"same","category":"A"},
            {"id":"B","name":"Same","category":"a"},
            {"id":"c","name":"Alpha"},
            {"id":"a","name":"Zeta","category":"B"}
            """);
        var original = catalog.Search(new ReferenceQuery()).Items;
        Func<ReferenceRecord, string?> key = sort switch
        {
            ReferenceSortKey.Key => r => r.Key,
            ReferenceSortKey.Name => r => r.Name,
            _ => r => r.Category,
        };
        var expected = (descending
            ? original.OrderByDescending(key, StringComparer.OrdinalIgnoreCase)
            : original.OrderBy(key, StringComparer.OrdinalIgnoreCase)).ThenBy(r => r.Key, StringComparer.Ordinal).ToArray();
        var query = new ReferenceQuery { SortBy = sort, Descending = descending, Limit = 1 };

        Parallel.For(0, 16, _ =>
        {
            for (var i = 0; i < expected.Length; i++)
                Assert.Equal(expected[i], Assert.Single(catalog.Search(query with { Offset = i }).Items));
        });
        var filtered = catalog.Search(query with { Text = " SAME ", Category = "a", Limit = 200 });
        Assert.Equal(2, filtered.Total);
        Assert.Equal(expected.Where(r => r.Key is "b" or "B"), filtered.Items);
        Assert.Empty(catalog.Search(query with { Offset = int.MaxValue }).Items);
    }

    [Fact]
    public void UnfilteredPagingKeepsFileOrderAndNormalizesBounds()
    {
        var catalog = Load("""{"id":"2","name":"Two"},{"id":"1","name":"One"}""");
        Assert.Equal("2", Assert.Single(catalog.Search(new ReferenceQuery { Text = " ", Offset = -1, Limit = 0 }).Items).Key);
        Assert.Equal(["2", "1"], catalog.Search(new ReferenceQuery { Descending = true, Limit = int.MaxValue }).Items.Select(r => r.Key));
        Assert.Empty(catalog.Search(new ReferenceQuery { Offset = int.MaxValue }).Items);
        Assert.Throws<ArgumentException>(() => catalog.Search(new ReferenceQuery { SortBy = (ReferenceSortKey)999 }));
    }

    private static IReferenceCatalog Load(string records) => new ReferencePackStore([])
        .Import(new PackBuilder().Records("items.json", $$"""{"records":[{{records}}]}""").Zip(), "paging.zip")
        .Catalog("items")!;
}
