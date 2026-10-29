using SaveToolkit.Sdk.References;
using static SaveToolkit.Core.Tests.SdkHelpers.ReferenceHelperFixture;

namespace SaveToolkit.Core.Tests.SdkHelpers;

public sealed class ReferenceCatalogReaderTests
{
    [Fact]
    public void TraversalPreservesAllQuerySettings_AndUsesActualProgressOnShortPages()
    {
        var records = new[] { Record("a"), Record("b"), Record("c"), Record("d") };
        var catalog = new TestCatalog(records)
        {
            SearchOverride = query => new(records.Skip(query.Offset).Take(1).ToArray(), records.Length, query.Offset),
        };
        var request = new ReferenceQuery { Text = "Name", Category = "Reference", SortBy = ReferenceSortKey.Key,
            Descending = true, Offset = 1, Limit = 2 };
        var actual = ReferenceCatalogReader.ReadBounded(catalog, request, 3);
        Assert.Equal(["b", "c", "d"], actual.Select(record => record.Key));
        Assert.Equal([1, 2, 3], catalog.Queries.Select(query => query.Offset));
        Assert.All(catalog.Queries, query => Assert.Equal(request with { Offset = query.Offset }, query));
    }

    [Fact]
    public void NullEmptyAndPastEndCatalogsAreCompleteEmptyResults()
    {
        Assert.Empty(ReferenceCatalogReader.ReadBounded(null, new(), 0));
        Assert.Empty(ReferenceCatalogReader.ReadBounded(new TestCatalog([]), new(), 0));
        Assert.Empty(ReferenceCatalogReader.ReadBounded(new TestCatalog([Record("a")]), new() { Offset = int.MaxValue }, 0));
    }

    [Fact]
    public void FullAndPartialLastPagesRespectTheMaximumPageSize()
    {
        var rows = Enumerable.Range(0, 401).Select(index => Record(index.ToString())).ToArray();
        var catalog = new TestCatalog(rows);
        Assert.Equal(rows, ReferenceCatalogReader.ReadBounded(catalog, new() { Limit = ReferenceQuery.MaxLimit }, 401));
        Assert.Equal([0, 200, 400], catalog.Queries.Select(query => query.Offset));
    }

    [Fact]
    public void BudgetUsesFilteredTotal_NotTheUnfilteredCount_AndNeverReturnsSilentTruncation()
    {
        var row = Record("a");
        var catalog = new TestCatalog([row]) { CountOverride = 1_000,
            SearchOverride = query => new([row], 1, query.Offset) };
        Assert.Single(ReferenceCatalogReader.ReadBounded(catalog, new() { Category = "Reference" }, 1));
        catalog.SearchOverride = query => new([row], 1_000, query.Offset);
        Assert.Contains("budget", Assert.Throws<InvalidOperationException>(() => ReferenceCatalogReader.ReadBounded(catalog, new(), 999)).Message);
    }

    [Theory]
    [InlineData("empty-page")]
    [InlineData("duplicate")]
    [InlineData("empty-key")]
    [InlineData("changed-total")]
    [InlineData("negative-total")]
    [InlineData("oversized-total")]
    [InlineData("wrong-offset")]
    [InlineData("oversized-page")]
    [InlineData("past-total")]
    [InlineData("changed-count")]
    public void MalformedCatalogResponsesAreRejected(string kind)
    {
        var catalog = new TestCatalog([Record("a"), Record("b")]);
        catalog.SearchOverride = query => kind switch
        {
            "empty-page" => new([], 2, query.Offset),
            "duplicate" => new([Record("a")], 2, query.Offset),
            "empty-key" => new([Record("")], 2, query.Offset),
            "changed-total" => new([Record(query.Offset.ToString())], query.Offset == 0 ? 2 : 1, query.Offset),
            "negative-total" => new([], -1, query.Offset),
            "oversized-total" => new([], 3, query.Offset),
            "wrong-offset" => new([Record("a")], 2, query.Offset + 1),
            "oversized-page" => new([Record("a"), Record("b")], 2, query.Offset),
            "past-total" => new([Record("a")], 0, query.Offset),
            _ => ChangeCount(catalog, query),
        };
        Assert.Throws<InvalidOperationException>(() => ReferenceCatalogReader.ReadBounded(catalog, new() { Limit = 1 }, 10));
    }

    private static ReferencePage ChangeCount(TestCatalog catalog, ReferenceQuery query)
    {
        catalog.CountOverride = 3;
        return new([Record("a")], 2, query.Offset);
    }

    [Fact]
    public void InvalidCountsBudgetsAndQueryBoundsFail()
    {
        var catalog = new TestCatalog([]) { CountOverride = -1 };
        Assert.Throws<InvalidOperationException>(() => ReferenceCatalogReader.ReadBounded(catalog, new(), 10));
        Assert.Throws<ArgumentOutOfRangeException>(() => ReferenceCatalogReader.ReadBounded(null, new(), -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => ReferenceCatalogReader.ReadBounded(null, new() { Offset = -1 }, 10));
        Assert.Throws<ArgumentOutOfRangeException>(() => ReferenceCatalogReader.ReadBounded(null, new() { Limit = 0 }, 10));
        Assert.Throws<ArgumentOutOfRangeException>(() => ReferenceCatalogReader.ReadBounded(null, new() { Limit = 201 }, 10));
    }
}
