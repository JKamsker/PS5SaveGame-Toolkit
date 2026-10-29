using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.EldenRing.Tests;

public sealed class CommunityCatalogTraversalTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(201)]
    [InlineData(401)]
    [InlineData(20_000)]
    public void CompleteIndexIsLazyReusedAndSortedAcrossPageBoundaries(int count)
    {
        var template = CommunityReferenceTests.Context().Catalog(CommunityReferences.ItemsCatalogId)!;
        var record = template.Search(new() { Limit = 1 }).Items[0];
        var records = Enumerable.Range(0, count).Reverse()
            .Select(index => record with { Key = $"id-{index:D5}", Name = "Same name", Category = null }).ToArray();
        var source = new ProgressTestCatalog(template, records);
        var browser = new CommunityCatalogView("test", "Test catalog", source);
        Assert.Equal(count, browser.Collection.TotalCount);
        _ = browser.View();
        Assert.Equal(0, source.Searches);

        var request = new QueryableCollectionQuery { Offset = Math.Max(0, count - 3), Limit = 3, SortColumn = "name", Descending = true };
        var page = browser.Query(request);
        Assert.Equal(count, page.TotalCount);
        Assert.Equal(count, page.FilteredCount);
        Assert.Equal(request.Offset, page.Offset);
        Assert.Equal(3, page.Limit);
        Assert.Equal(records.Select(row => "test/" + row.Key).Order(StringComparer.Ordinal).Skip(request.Offset),
            page.Rows.Select(row => row.Key));
        Assert.All(page.Rows, row => Assert.Equal("Unknown", Assert.IsType<TextNode>(row.Cells["category"]).Text));

        var searches = source.Searches;
        Assert.Equal((count + ReferenceQuery.MaxLimit - 1) / ReferenceQuery.MaxLimit, searches);
        foreach (var text in new string?[] { null, "", "   " })
        {
            var repeated = browser.Query(request with { Text = text, IncludeEmpty = false });
            Assert.Equal(page.Rows.Select(row => row.Key), repeated.Rows.Select(row => row.Key));
            Assert.Equal(count, repeated.FilteredCount);
            Assert.Equal(0, repeated.HiddenEmptyCount);
        }
        Assert.Equal(searches, source.Searches);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(20_001)]
    public void InvalidCatalogCountFailsBeforeSearchAndFailureIsNeverAnEmptyIndex(int count)
    {
        var template = CommunityReferenceTests.Context().Catalog(CommunityReferences.ItemsCatalogId)!;
        var source = new ProgressTestCatalog(template, [], count);
        var browser = new CommunityCatalogView("test", "Invalid", source);
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var error = Assert.Throws<InvalidOperationException>(() => browser.Query(new()));
            Assert.Equal("Community catalog exceeds the 20000-record budget.", error.Message);
        }
        Assert.Equal(0, source.Searches);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void IncompleteOrDuplicateIndexFailsInsteadOfPublishingPartialResults(bool duplicate)
    {
        var template = CommunityReferenceTests.Context().Catalog(CommunityReferences.ItemsCatalogId)!;
        var row = template.Search(new() { Limit = 1 }).Items[0];
        var rows = duplicate ? new[] { row, row } : [];
        var source = new ProgressTestCatalog(template, rows, duplicate ? 2 : 1);
        var browser = new CommunityCatalogView("test", "Invalid", source);
        Assert.Throws<InvalidOperationException>(() => browser.Query(new()));
        Assert.Throws<InvalidOperationException>(() => browser.Query(new()));
        Assert.Equal(1, source.Searches);
    }
}
