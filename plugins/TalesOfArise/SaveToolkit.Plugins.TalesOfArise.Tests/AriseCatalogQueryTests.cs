using SaveToolkit.Core.References;
using SaveToolkit.Sdk.References;

namespace SaveToolkit.Plugins.TalesOfArise.Tests;

public sealed class AriseCatalogQueryTests
{
    [Theory]
    [InlineData("id", ReferenceSortKey.Key, false)]
    [InlineData("id", ReferenceSortKey.Key, true)]
    [InlineData("name", ReferenceSortKey.Name, false)]
    [InlineData("name", ReferenceSortKey.Name, true)]
    [InlineData("category", ReferenceSortKey.Category, false)]
    [InlineData("category", ReferenceSortKey.Category, true)]
    public void StandardSortsDelegateBoundedPagingToTheCatalog(string column, ReferenceSortKey sort, bool descending)
    {
        var catalog = Catalog("equipment");
        var page = AriseCatalogQuery.Query(catalog.Id, catalog, new()
        {
            Offset = 203, Limit = 17, SortColumn = column, Descending = descending,
        });
        var request = Assert.Single(catalog.Requests);
        Assert.Equal(sort, request.SortBy);
        Assert.Equal(descending, request.Descending);
        Assert.Equal(203, request.Offset);
        Assert.Equal(17, request.Limit);
        Assert.Equal(catalog.Inner.Search(request).Items.Select(record => record.Key), page.Rows.Select(row => row.Key));
        Assert.Equal(catalog.Count, page.FilteredCount);
    }

    [Fact]
    public void CategoryFilteringAndCategorySortingShareOneBoundedCatalogQuery()
    {
        var catalog = Catalog("items");
        var page = AriseCatalogQuery.Query(catalog.Id, catalog, new()
        {
            Offset = 2, Limit = 3, SortColumn = "category", Descending = true,
            Filters = new Dictionary<string, string> { ["category"] = "fishing item" },
        });
        var request = Assert.Single(catalog.Requests);
        Assert.Equal("fishing item", request.Category);
        Assert.Equal(ReferenceSortKey.Category, request.SortBy);
        Assert.Equal(25, page.FilteredCount);
        Assert.Equal(3, page.Rows.Count);
        Assert.Equal(catalog.Inner.Search(request).Items.Select(record => record.Key), page.Rows.Select(row => row.Key));
    }

    [Fact]
    public void CategoryFilterIsForwardedBeforePagingAndCombinesWithRichFactSearch()
    {
        var catalog = Catalog("skill-nodes");
        var first = catalog.Inner.Search(new() { Text = "Sonic Thrust", Limit = 1 }).Items[0];
        var page = AriseCatalogQuery.Query(catalog.Id, catalog, new()
        {
            Text = "Escape Artist", SortColumn = "name",
            Filters = new Dictionary<string, string> { ["category"] = first.Category! },
        });
        Assert.Equal(5, page.FilteredCount);
        Assert.All(catalog.Requests, request =>
        {
            Assert.Equal(first.Category, request.Category);
            Assert.Equal(ReferenceSortKey.Name, request.SortBy);
            Assert.Null(request.Text); // F2 searches key/name; the plugin additionally searches facts and titles.
        });
        Assert.All(page.Rows, row => Assert.Contains("escape-artist", row.Key));
    }

    [Fact]
    public void CatalogsWithoutDeclaredSortingRetainDeterministicFallback()
    {
        var catalog = Catalog("equipment", declareSorts: false);
        var page = AriseCatalogQuery.Query(catalog.Id, catalog, new() { Offset = 203, Limit = 17, SortColumn = "name", Descending = true });
        Assert.All(catalog.Requests, request => Assert.Null(request.SortBy));
        var expected = catalog.Inner.Search(new() { SortBy = ReferenceSortKey.Name, Descending = true, Offset = 203, Limit = 17 });
        Assert.Equal(expected.Items.Select(record => record.Key), page.Rows.Select(row => row.Key));
    }

    private static RecordingCatalog Catalog(string id, bool declareSorts = true)
    {
        var plugin = new TalesOfArisePlugin();
        var references = new ReferencePackStore([plugin]).Pin(plugin, "PPSA02745");
        return new RecordingCatalog(references.Catalog(id)!, declareSorts);
    }

    private sealed class RecordingCatalog(IReferenceCatalog inner, bool declareSorts) : IReferenceCatalog
    {
        public IReferenceCatalog Inner => inner;
        public List<ReferenceQuery> Requests { get; } = [];
        public string Id => inner.Id;
        public ReferencePackInfo Pack => inner.Pack;
        public ReferenceDatasetInfo Dataset => inner.Dataset;
        public int Count => inner.Count;
        public IReadOnlyList<string> Categories => inner.Categories;
        public IReadOnlyList<ReferenceSortKey> SortKeys => declareSorts ? inner.SortKeys : [];
        public ReferenceRecord? Find(string key) => inner.Find(key);
        public ReferencePage Search(ReferenceQuery query)
        {
            Requests.Add(query);
            return inner.Search(query);
        }
    }
}
