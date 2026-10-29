using SaveToolkit.Core.Plugins;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Core.Storage;
using SaveToolkit.Core.Tests.Fakes;
using SaveToolkit.Sdk.Collections;

namespace SaveToolkit.Core.Tests;

public sealed class QueryableSemanticRobustnessTests : IDisposable
{
    private const int PageSize = QueryableCollectionLimits.MaxPageSize;
    private const int RowCount = PageSize * 4 + 1;
    private readonly WorkspaceStore _store = new(new PluginCatalog([new CounterPlugin()]),
        new WorkspaceStoreOptions { DataRoot = TestZips.TempDir() });

    public void Dispose() => _store.Dispose();

    [Theory]
    [InlineData(-1, false)]
    [InlineData(1, false)]
    [InlineData(-1, true)]
    [InlineData(1, true)]
    public void TotalChangingAfterFirstPageKeepsOnlyProvenChanges(int delta, bool malformedOriginal)
    {
        var malformed = new SemanticQueryableDocument(RowCount)
        {
            TransformPage = page => page.Offset == 0 ? page : page with
            { TotalCount = page.TotalCount + delta, FilteredCount = page.TotalCount + delta },
        };
        var current = malformedOriginal ? new SemanticQueryableDocument(RowCount) : malformed;
        current.SetQuantity(0, 0, 9);
        current.SetQuantity(0, PageSize, 999);
        var log = Review(current, malformedOriginal ? malformed : new(RowCount));
        AssertTruncated(log, "invalid page metadata");
        Assert.Equal(SemanticQueryableDocument.ValueId(0, 0), Assert.Single(log.Changes).ValueId);
        Assert.Equal(2, malformed.Queries.Count);
    }

    [Fact]
    public void MultiCursorCycleWithDistinctRowsStopsBeforeRevisitingAPage()
    {
        var current = new SemanticQueryableDocument(RowCount)
        {
            CursorPaging = true,
            TransformPage = page => page.Offset == PageSize * 2 ? page with { NextCursor = PageSize.ToString(System.Globalization.CultureInfo.InvariantCulture) } : page,
        };
        var log = Review(current, new(RowCount));
        AssertTruncated(log, "repeated cursor");
        Assert.Equal(3, current.Queries.Count);
        Assert.Equal([null, "200", "400"], current.Queries.Select(call => call.Query.Cursor));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ZeroRowPageAfterProgressStopsEvenWithAFreshCursor(bool malformedOriginal)
    {
        var malformed = new SemanticQueryableDocument(RowCount)
        {
            TransformPage = page => page.Offset == PageSize ? page with { Rows = [], NextCursor = "400" } : page,
        };
        var current = malformedOriginal ? new SemanticQueryableDocument(RowCount) : malformed;
        current.SetQuantity(0, 0, 9);
        current.SetQuantity(0, PageSize, 999);
        var log = Review(current, malformedOriginal ? malformed : new(RowCount));
        AssertTruncated(log, "empty page");
        Assert.Single(log.Changes);
        Assert.Equal(2, malformed.Queries.Count);
    }

    [Fact]
    public void RowBudgetEndsPartwayThroughANativePageAndDoesNotQueryLaterCollections()
    {
        const int rowBudget = PageSize + 1;
        var current = new SemanticQueryableDocument(RowCount, 2);
        current.SetQuantity(0, PageSize, 999);
        current.SetQuantity(0, PageSize + 1, 999);
        current.SetQuantity(1, 0, 9);
        var log = Review(current, new(RowCount, 2), new() { MaxReviewQueryRows = rowBudget });
        AssertTruncated(log, "row cap");
        var change = Assert.Single(log.Changes);
        Assert.Equal(SemanticQueryableDocument.ValueId(0, PageSize), change.ValueId);
        Assert.Equal(1, change.Target!.Query.Limit);
        Assert.Equal([PageSize, 1], current.Queries.Select(call => call.Query.Limit));
        Assert.All(current.Queries, call => Assert.Equal(SemanticQueryableDocument.Collection(0), call.Collection));
    }

    [Fact]
    public void ViewMemoryBudgetExhaustedMidPageRetainsEarlierProofAndStopsFurtherQueries()
    {
        var current = new SemanticQueryableDocument(RowCount, 2);
        current.SetQuantity(0, 0, 9);
        current.SetQuantity(0, 10, 999);
        var log = Review(current, new(RowCount, 2), new() { MaxOriginalBytes = 1000 });
        AssertTruncated(log, "view memory or traversal cap");
        Assert.Equal(SemanticQueryableDocument.ValueId(0, 0), Assert.Single(log.Changes).ValueId);
        Assert.Single(current.Queries);
    }

    private SemanticChangeLog Review(SemanticQueryableDocument current, SemanticQueryableDocument original,
        DocumentSessionOptions? options = null)
    {
        var zip = TestZips.Build(("PPSA99999/sdimg_SAVE/save.dat", CounterPlugin.Create(5)));
        var workspace = _store.Create("robust review", [new ImportSource("dump.zip", () => new MemoryStream(zip))]);
        using var session = new DocumentSession("review", workspace.Set.AllDiskFiles.Single(), new CounterPlugin(),
            current, options, () => original);
        return session.GetChanges();
    }

    private static void AssertTruncated(SemanticChangeLog log, string reason)
    {
        Assert.False(log.Complete);
        Assert.Contains(log.Warnings, warning => warning.Contains(reason, StringComparison.Ordinal));
        Assert.All(log.Changes, change => { Assert.True(change.OriginalExists); Assert.True(change.CurrentExists); });
    }
}
