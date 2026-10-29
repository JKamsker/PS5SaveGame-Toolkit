using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.EldenRing.Tests;

public sealed class CatalogSearchTests
{
    [Fact]
    public void SharedProvenanceCache_DoesNotRetainEveryDistinctSourceInLargeImportedCatalogs()
    {
        var dataset = new ReferenceDatasetInfo("test.names", "items", ReferenceDatasetKind.Records,
            new("https://example.org", "Test author", "MIT", "https://opensource.org/license/mit",
                new(2026, 10, 29), new string('x', 4096)));
        var records = Enumerable.Range(0, 100).Select(index => new ReferenceRecord($"id-{index}", "Item")
            { Dataset = dataset, Source = $"https://example.org/source/{index}" }).ToArray();
        var search = new ReferenceSearchText();
        foreach (var record in records) Assert.False(search.ContainsDetails(record, "no-match"));

        var before = GC.GetAllocatedBytesForCurrentThread();
        var first = search.ContainsDetails(records[0], "no-match");
        var cachedAllocation = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.False(first);
        Assert.InRange(cachedAllocation, 0, 1024); // Shared evidence at the start still benefits from caching.

        before = GC.GetAllocatedBytesForCurrentThread();
        foreach (var record in records) _ = search.ContainsDetails(record, "no-match");
        var overflowAllocation = GC.GetAllocatedBytesForCurrentThread() - before;
        // The oversized tail is deliberately formatted again instead of being retained in
        // the cache. An unbounded cache allocates almost nothing here and fails this test.
        Assert.True(overflowAllocation > 64 * 1024, $"Expected uncached overflow, allocated {overflowAllocation} bytes.");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RepeatedNoMatchSearch_DoesNotFormatEvidenceForEveryRecord(bool community)
    {
        var source = CommunityReferenceTests.Context().Catalog(community
            ? CommunityReferences.ItemsCatalogId : ReferenceBrowser.CatalogId)!;
        var query = Browser(source, community);
        var request = new QueryableCollectionQuery { Text = "no-such-item-match" };
        Assert.Empty(query(request).Rows); // Populate the immutable metadata index before measuring.

        var before = GC.GetAllocatedBytesForCurrentThread();
        var result = query(request);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Empty(result.Rows);
        Assert.Equal(source.Count, result.TotalCount);
        // A zero-row response should scan existing strings, not allocate 20+ MB of
        // descriptions, dataset provenance and attribute labels on every keystroke.
        Assert.InRange(allocated, 1, 1024 * 1024);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OptimizedSearchRetainsProtocolValidation(bool community)
    {
        var source = CommunityReferenceTests.Context().Catalog(ReferenceBrowser.CatalogId)!;
        var query = Browser(source, community);
        Assert.Throws<ArgumentException>(() => query(new() { Text = new string('x', QueryableCollectionLimits.MaxQueryLength + 1) }));
        Assert.Throws<ArgumentException>(() => query(new() { Text = "no-match", Cursor = "unsupported" }));
        Assert.Throws<ArgumentException>(() => query(new() { Text = "no-match", SortColumn = "unknown" }));
        Assert.Throws<ArgumentException>(() => query(new()
            { Text = "no-match", Filters = new Dictionary<string, string> { ["unknown"] = "value" } }));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SearchMatchesTheEntireRenderedText_IncludingPunctuationAndJoinBoundaries(bool community)
    {
        var source = CommunityReferenceTests.Context().Catalog(ReferenceBrowser.CatalogId)!;
        var dataset = new ReferenceDatasetInfo("test.names", "items", ReferenceDatasetKind.Records,
            new("https://example.org", "Test author", "MIT", "https://opensource.org/license/mit",
                new(2026, 10, 29), "Synthetic evidence"));
        ReferenceRecord[] records =
        [
            new("first", "First item")
            {
                Dataset = dataset, Category = "goods", Description = "Description · with punctuation: alpha",
                Source = "https://example.org/first", Attributes = new Dictionary<string, string>
                { ["groups"] = "First group", ["editorGroups"] = "Second group", ["fact"] = "Fact value" },
            },
            new("second", "Second item")
            {
                Dataset = dataset, Description = "   ",
                Attributes = new Dictionary<string, string> { ["unused"] = "Other: value · suffix" },
            },
            new("third", "Third item") { Dataset = dataset, Description = "Last description" },
        ];
        var query = Browser(new ProgressTestCatalog(source, records), community);
        var rendered = query(new() { Limit = 200 }).Rows.ToDictionary(row => row.Key,
            row => row.Cells.Values.Select(Text).ToArray(), StringComparer.Ordinal);
        var terms = new HashSet<string>(StringComparer.Ordinal)
        { "FIRST", "  first  ", "goods", "unknown", "Fact value", "fact: Fact", "Source:", "MIT", "Pack ", "·", "no-match" };
        foreach (var text in rendered.Values.SelectMany(value => value))
        foreach (var separator in new[] { " · ", ": ", "; " })
        {
            var position = text.IndexOf(separator, StringComparison.Ordinal);
            if (position < 0) continue;
            var start = Math.Max(0, position - 3);
            terms.Add(text[start..Math.Min(text.Length, position + separator.Length + 3)]);
        }
        foreach (var term in terms)
        {
            var expected = rendered.Where(row => row.Value.Any(text =>
                text.Contains(term.Trim(), StringComparison.OrdinalIgnoreCase))).Select(row => row.Key).Order(StringComparer.Ordinal);
            var actual = query(new() { Text = term, Limit = 200 }).Rows.Select(row => row.Key).Order(StringComparer.Ordinal);
            Assert.Equal(expected, actual);
        }

        // Exact rendered-evidence filters and sorting retain their original contract too.
        var details = rendered[community ? "test/third" : "catalog/third"][^1];
        Assert.Single(query(new() { Filters = new Dictionary<string, string> { ["details"] = details } }).Rows);
        var sorted = query(new() { SortColumn = "details", Descending = true }).Rows;
        Assert.Equal(rendered.OrderByDescending(row => row.Value[^1], StringComparer.OrdinalIgnoreCase)
            .Select(row => row.Key), sorted.Select(row => row.Key));
    }

    private static Func<QueryableCollectionQuery, QueryableCollectionPage> Browser(IReferenceCatalog source, bool community)
    {
        if (community)
        {
            var browser = new CommunityCatalogView("test", "Test references", source);
            return browser.Query;
        }
        var primary = new ReferenceBrowser(source);
        return primary.Query;
    }

    private static string Text(ViewNode node) => node switch
    {
        TextNode text => text.Text,
        SectionNode section => Assert.IsType<TextNode>(section.Children[0]).Text,
        _ => throw new InvalidOperationException("Unexpected reference text cell."),
    };
}
