using SaveToolkit.Core.Sessions;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.Witcher3.Tests;

public sealed class WitcherCatalogSortTests
{
    private const string CollectionId = "catalog/gwent";
    private const string Build = "sav3-64.23.163";

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SavedCountsSortAsNativeIntegersBeforePagingWithOrdinalKeyTies(bool descending)
    {
        var references = ReferencePackFixture.Pin();
        var calls = 0;
        var view = new CatalogViews(references, Build, () => {
            calls++;
            // Deliberately disagree with both count order and ordinal key order.
            return [new GwentCard(2, 2), new GwentCard(4, 100), new GwentCard(0, 0),
                new GwentCard(3, 10), new GwentCard(1, int.MaxValue), new GwentCard(10, 2)];
        });
        string[] expected = descending ? ["1", "4", "3", "10", "2", "0"] : ["0", "10", "2", "3", "4", "1"];
        var query = new QueryableCollectionQuery {
            Text = "saved copies", SortColumn = "owned", Descending = descending, Limit = 2,
            Filters = new Dictionary<string, string> { ["category"] = "nEuTrAl" },
        };
        AssertPages(view, query, expected, references.Catalog("gwent")!.Count);
        Assert.Equal(4, calls); // Three pages and the empty page after the final match.

        var maximum = view.Query(CollectionId, query with {
            Text = "2147483647 saved copies", Filters = new Dictionary<string, string>(),
        });
        Assert.Equal("2147483647 saved copies", Assert.IsType<TextNode>(Assert.Single(maximum.Rows).Cells["owned"]).Text);
        Assert.Equal("1", maximum.Rows[0].Key);
        var ties = view.Query(CollectionId, query with {
            Text = null, Filters = new Dictionary<string, string> { ["owned"] = "2 SAVED COPIES" },
        });
        Assert.Equal(["10", "2"], ties.Rows.Select(row => row.Key));
        Assert.Equal(2, ties.FilteredCount);
    }

    [Fact]
    public void NewQueryRefreshesNumericOrderAfterStagedCopyChanges()
    {
        IReadOnlyList<GwentCard> cards = [new GwentCard(0, 2), new GwentCard(1, 10)];
        var view = new CatalogViews(ReferencePackFixture.Pin(), Build, () => cards);
        var query = new QueryableCollectionQuery { Text = "saved copies", SortColumn = "owned", Limit = 1 };
        Assert.Equal("0", Assert.Single(view.Query(CollectionId, query).Rows).Key);
        cards = [new GwentCard(0, 100), new GwentCard(1, 10)];
        Assert.Equal("1", Assert.Single(view.Query(CollectionId, query).Rows).Key);
        var second = view.Query(CollectionId, query with { Offset = 1 });
        Assert.Equal("0", Assert.Single(second.Rows).Key);
        Assert.Equal("100 saved copies", Assert.IsType<TextNode>(second.Rows[0].Cells["owned"]).Text);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnrecordedBucketKeepsItsPlacementAndZeroIsARecordedCount(bool descending)
    {
        var references = ReferencePackFixture.Pin();
        var catalog = references.Catalog("gwent")!;
        var view = new CatalogViews(references, Build, () => [new GwentCard(2, 10), new GwentCard(0, 0)]);
        var unrecorded = Keys(catalog).Where(key => key is not ("0" or "2")).Order(StringComparer.Ordinal).ToArray();
        string[] recorded = descending ? ["2", "0"] : ["0", "2"];
        var expected = descending ? unrecorded.Concat(recorded).ToArray() : recorded.Concat(unrecorded).ToArray();
        var query = new QueryableCollectionQuery { SortColumn = "owned", Descending = descending, Limit = 37 };
        AssertPages(view, query, expected, catalog.Count);
        AssertPages(view, query with {
            Text = "AVAILABILITY UNKNOWN",
            Filters = new Dictionary<string, string> { ["owned"] = "Not recorded; availability unknown" },
        }, unrecorded, catalog.Count);
        var zero = Assert.Single(view.Query(CollectionId, query with {
            Text = "0 saved copies", Filters = new Dictionary<string, string> { ["raw"] = "0" },
        }).Rows);
        Assert.Equal("0 saved copies", Assert.IsType<TextNode>(zero.Cells["owned"]).Text);
        Assert.False(zero.IsEmpty);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void MissingAndEmptyCollectionsSortByNativeKeysWithoutInventingCounts(bool available, bool descending)
    {
        var references = ReferencePackFixture.Pin();
        var catalog = references.Catalog("gwent")!;
        var view = new CatalogViews(references, Build, () => available ? [] : null);
        var text = available ? "Not recorded; availability unknown" : "Collection unavailable";
        AssertPages(view, new() {
            SortColumn = "owned", Descending = descending, Limit = 37, Text = text,
            Filters = new Dictionary<string, string> { ["owned"] = text },
        }, Keys(catalog).Order(StringComparer.Ordinal).ToArray(), catalog.Count);
    }

    [Fact]
    public void NativeAliasesKeepIndependentCountsIdentityAndAttribution()
    {
        var references = ReferencePackFixture.Pin();
        var catalog = references.Catalog("gwent")!;
        var view = new CatalogViews(references, Build, () => [new GwentCard(130, 2), new GwentCard(131, 100)]);
        var query = new QueryableCollectionQuery {
            Text = "saved copies", SortColumn = "owned", Descending = true,
            Filters = new Dictionary<string, string> { ["category"] = "Northern Realms" },
        };
        var page = view.Query(CollectionId, query);
        Assert.Equal(["131", "130"], page.Rows.Select(row => row.Key));
        Assert.Equal(["100 saved copies", "2 saved copies"], page.Rows.Select(row => Assert.IsType<TextNode>(row.Cells["owned"]).Text));
        Assert.All(page.Rows, row => {
            Assert.Equal(row.Key, Assert.IsType<TextNode>(row.Cells["raw"]).Text);
            var name = Assert.IsType<SectionNode>(row.Cells["name"]);
            Assert.Equal("Inferred mapping", name.Badge);
            Assert.Equal(catalog.Find(row.Key)!.AttributionText, Assert.IsType<TextNode>(Assert.Single(name.Children)).Text);
        });
    }

    [Fact]
    public void SortedCatalogBrowsingLeavesSessionHistoryRevisionAndBytesUnchanged()
    {
        var input = SyntheticSave.Create();
        var file = WitcherTestFiles.Import(input);
        var plugin = new Witcher3Plugin();
        var references = ReferencePackFixture.Pin(plugin);
        using var document = plugin.Open(file, references);
        using var session = new DocumentSession("catalog-sort", file, plugin, document, references: references);
        _ = session.BuildView();
        var before = session.State;
        foreach (var descending in new[] { false, true })
        {
            var page = session.QueryCollection(CollectionId, new() {
                SortColumn = "owned", Descending = descending, Offset = 200, Limit = 25,
            }, expectedRevision: before.Revision, cancellationToken: TestContext.Current.CancellationToken);
            Assert.Equal(25, page.Rows.Count);
            Assert.All(page.Rows, row => Assert.Equal("Collection unavailable", Assert.IsType<TextNode>(row.Cells["owned"]).Text));
            Assert.Equal(before, session.State);
            Assert.Equal(input, Assert.Single(document.Serialize()).Content.ToArray());
        }
    }

    private static string[] Keys(IReferenceCatalog catalog) => Enumerable.Range(0, (catalog.Count + ReferenceQuery.MaxLimit - 1) / ReferenceQuery.MaxLimit)
        .SelectMany(page => catalog.Search(new() { Offset = page * ReferenceQuery.MaxLimit, Limit = ReferenceQuery.MaxLimit }).Items)
        .Select(record => record.Key).ToArray();

    private static void AssertPages(CatalogViews view, QueryableCollectionQuery query, string[] expected, int total)
    {
        var actual = new List<string>();
        for (var offset = 0; offset < expected.Length; offset += query.Limit)
        {
            var page = view.Query(CollectionId, query with { Offset = offset });
            Assert.Equal(total, page.TotalCount);
            Assert.Equal(expected.Length, page.FilteredCount);
            Assert.Equal(expected.Skip(offset).Take(query.Limit), page.Rows.Select(row => row.Key));
            actual.AddRange(page.Rows.Select(row => row.Key));
        }
        Assert.Equal(expected, actual);
        Assert.Empty(view.Query(CollectionId, query with { Offset = expected.Length }).Rows);
    }
}
