using SaveToolkit.Core.References;
using SaveToolkit.Core.Tests.Fakes;
using SaveToolkit.Sdk.References;

namespace SaveToolkit.Core.Tests.References;

public sealed class ReferenceSortingTests
{
    private static IReferenceCatalog Catalog() => new ReferencePackStore([new ItemsPlugin()])
        .Import(new PackBuilder().Records("items.json", """
            {"records":[
              {"id":"3","name":"Same","category":"B"},
              {"id":"2","name":"same","category":"a"},
              {"id":"1","name":"Alpha","category":"a"},
              {"id":"4","name":"Missing category"}
            ]}
            """).Zip(), "sorting.zip").Catalog("items")!;

    [Fact]
    public void DefaultOrderIsUnchanged_AndSortKeysAreDeclared()
    {
        var catalog = Catalog();
        Assert.Equal([ReferenceSortKey.Key, ReferenceSortKey.Name, ReferenceSortKey.Category], catalog.SortKeys);
        Assert.Equal(["3", "2", "1", "4"], catalog.Search(new ReferenceQuery()).Items.Select(r => r.Key));
        Assert.Equal(["3", "2", "1", "4"], catalog.Search(new ReferenceQuery { Descending = true }).Items.Select(r => r.Key));
    }

    [Fact]
    public void SortingCombinesWithCaseInsensitiveFiltersAndStablePaging()
    {
        var catalog = Catalog();
        var query = new ReferenceQuery { Text = "a", Category = "A", SortBy = ReferenceSortKey.Name, Limit = 1 };
        var first = catalog.Search(query);
        var second = catalog.Search(query with { Offset = 1 });
        Assert.Equal(2, first.Total);
        Assert.Equal("1", Assert.Single(first.Items).Key);
        Assert.Equal("2", Assert.Single(second.Items).Key);
        Assert.Equal("2", Assert.Single(catalog.Search(query with { Descending = true }).Items).Key);
        Assert.Equal(["1", "4", "2", "3"], catalog.Search(new ReferenceQuery { SortBy = ReferenceSortKey.Name }).Items.Select(r => r.Key));
        Assert.Equal(["2", "3", "4", "1"], catalog.Search(new ReferenceQuery { SortBy = ReferenceSortKey.Name, Descending = true }).Items.Select(r => r.Key));
        Assert.Equal(["4", "1", "2", "3"], catalog.Search(new ReferenceQuery { SortBy = ReferenceSortKey.Category }).Items.Select(r => r.Key));
    }

    [Fact]
    public void QueriesStayBounded_AndUnknownSortKeysAreRejected()
    {
        var catalog = Catalog();
        Assert.Equal("1", Assert.Single(catalog.Search(new ReferenceQuery { SortBy = ReferenceSortKey.Key, Limit = 0, Offset = -10 }).Items).Key);
        Assert.Empty(catalog.Search(new ReferenceQuery { SortBy = ReferenceSortKey.Key, Offset = int.MaxValue }).Items);
        Assert.Throws<ArgumentException>(() => catalog.Search(new ReferenceQuery { SortBy = (ReferenceSortKey)999 }));
        var records = string.Join(',', Enumerable.Range(0, 250).Select(n => $$"""{"id":"{{n}}","name":"Item {{n}}"}"""));
        var large = new ReferencePackStore([new ItemsPlugin()]).Import(
            new PackBuilder().Records("items.json", $$"""{"records":[{{records}}]}""").Zip(), "large.zip").Catalog("items")!;
        var page = large.Search(new ReferenceQuery { SortBy = ReferenceSortKey.Key, Limit = int.MaxValue });
        Assert.Equal(250, page.Total);
        Assert.Equal(ReferenceQuery.MaxLimit, page.Items.Count);
    }
}
