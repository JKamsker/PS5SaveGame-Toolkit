using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.References;

namespace SaveToolkit.Plugins.Witcher3.Tests;

public sealed class WitcherCatalogQueryTests
{
    [Theory]
    [InlineData("name", ReferenceSortKey.Name)]
    [InlineData("raw", ReferenceSortKey.Key)]
    [InlineData("category", ReferenceSortKey.Category)]
    public void CatalogTablesDelegateBoundedCategorySearchAndDeclaredSortToThePinnedCatalog(string column, ReferenceSortKey sort)
    {
        var plugin = new Witcher3Plugin();
        var context = ReferencePackFixture.Pin(plugin);
        var catalog = new TrackingCatalog(context.Catalog("items")!);
        var view = new CatalogViews(new TrackingContext(catalog), "sav3-64.23.163", () => null);
        var category = catalog.Find("NGP Bear Armor")!.Category!;
        var query = new QueryableCollectionQuery {
            Filters = new Dictionary<string, string> { ["category"] = category.ToUpperInvariant() },
            SortColumn = column, Descending = true, Offset = 2, Limit = 4,
        };
        var expectedQuery = new ReferenceQuery {
            Text = query.Text, Category = query.Filters["category"], SortBy = sort,
            Descending = true, Offset = 2, Limit = 4,
        };
        var expected = context.Catalog("items")!.Search(expectedQuery);
        var actual = view.Query("catalog/items", query);
        Assert.NotEmpty(expected.Items);
        Assert.Equal(expected.Items.Select(record => record.Key), actual.Rows.Select(row => row.Key));
        Assert.Equal(expected.Total, actual.FilteredCount);
        Assert.Equal(catalog.Count, actual.TotalCount);
        Assert.Equal(expectedQuery, Assert.Single(catalog.Queries));
        _ = view.Query("catalog/items", new QueryableCollectionQuery { Limit = int.MaxValue });
        Assert.Equal(ReferenceQuery.MaxLimit, catalog.Queries[^1].Limit);
        Assert.Equal(2, catalog.Queries.Count); // No full-catalog warmup or plugin-side metadata cache.
    }

    [Fact]
    public void UndeclaredSortUsesCatalogOrderAndExtendedFiltersKeepFullDatasetCoverage()
    {
        var plugin = new Witcher3Plugin();
        var context = ReferencePackFixture.Pin(plugin);
        var catalog = new TrackingCatalog(context.Catalog("items")!) { SortKeys = [] };
        var view = new CatalogViews(new TrackingContext(catalog), "sav3-64.23.163", () => null);
        var page = view.Query("catalog/items", new QueryableCollectionQuery { SortColumn = "name", Limit = 2 });
        Assert.Equal(catalog.Search(new ReferenceQuery { Limit = 2 }).Items.Select(record => record.Key), page.Rows.Select(row => row.Key));
        Assert.All(catalog.Queries, query => Assert.Null(query.SortBy));
        catalog.Queries.Clear();
        var exact = view.Query("catalog/items", new QueryableCollectionQuery {
            Filters = new Dictionary<string, string> { ["raw"] = "NGP Bear Armor", ["owned"] = "Reference only" },
        });
        Assert.Equal("NGP Bear Armor", Assert.Single(exact.Rows).Key);
        Assert.Equal(1, exact.FilteredCount);
        Assert.All(catalog.Queries, query => Assert.InRange(query.Limit, 1, ReferenceQuery.MaxLimit));
        var categories = context.Catalog("items")!;
        foreach (var descending in new[] { false, true })
        {
            var expected = categories.Search(new ReferenceQuery { SortBy = ReferenceSortKey.Category, Descending = descending, Limit = 200 });
            var actual = new CatalogViews(context, "sav3-64.23.163", () => null).Query("catalog/items",
                new QueryableCollectionQuery { SortColumn = "category", Descending = descending, Limit = 200 });
            Assert.Equal(expected.Items.Select(record => record.Key), actual.Rows.Select(row => row.Key));
        }
    }

    [Fact]
    public void FullMetadataSearchRetainsDescriptionCategoryAttributesAndSavedStateMatches()
    {
        var plugin = new Witcher3Plugin();
        var context = ReferencePackFixture.Pin(plugin);
        var view = new CatalogViews(context, "sav3-64.23.163", () => [new GwentCard(108, 1)]);
        var bread = context.Catalog("items")!.Find("Bread")!;
        foreach (var text in new[] { bread.Description![..20], bread.Category!, bread.Attributes["mappingConfidence"] })
        {
            var page = view.Query("catalog/items", new QueryableCollectionQuery {
                Text = text, Filters = new Dictionary<string, string> { ["raw"] = "Bread" },
            });
            Assert.Equal("Bread", Assert.Single(page.Rows).Key);
            Assert.Equal(1, page.FilteredCount);
        }
        Assert.Equal("108", Assert.Single(view.Query("catalog/gwent", new QueryableCollectionQuery { Text = "1 saved copies" }).Rows).Key);
        Assert.Equal(224, view.Query("catalog/gwent", new QueryableCollectionQuery { Text = "availability unknown" }).FilteredCount);
    }

    private sealed class TrackingCatalog(IReferenceCatalog inner) : IReferenceCatalog
    {
        public string Id => inner.Id;
        public ReferencePackInfo Pack => inner.Pack;
        public ReferenceDatasetInfo Dataset => inner.Dataset;
        public int Count => inner.Count;
        public IReadOnlyList<string> Categories => inner.Categories;
        public IReadOnlyList<ReferenceSortKey> SortKeys { get; init; } = inner.SortKeys;
        public List<ReferenceQuery> Queries { get; } = [];
        public ReferenceRecord? Find(string key) => inner.Find(key);
        public ReferencePage Search(ReferenceQuery query) { Queries.Add(query); return inner.Search(query); }
    }

    private sealed class TrackingContext(IReferenceCatalog catalog) : IReferenceContext
    {
        public IReadOnlyList<ReferencePackInfo> Packs => [catalog.Pack];
        public IReferenceCatalog? Catalog(string catalogId, string? gameBuild = null) => catalogId == catalog.Id ? catalog : null;
        public AssetContent? GetAsset(string assetId) => null;
        public ReferenceDatasetInfo? AssetProvenance(string assetId) => null;
    }
}
