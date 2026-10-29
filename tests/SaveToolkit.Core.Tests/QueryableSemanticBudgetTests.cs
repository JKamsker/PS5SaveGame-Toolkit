using SaveToolkit.Core.Plugins;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Core.Storage;
using SaveToolkit.Core.Tests.Fakes;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Core.Tests;

public sealed class QueryableSemanticBudgetTests : IDisposable
{
    private readonly WorkspaceStore _store = new(new PluginCatalog([new CounterPlugin()]),
        new WorkspaceStoreOptions { DataRoot = TestZips.TempDir() });
    public void Dispose() => _store.Dispose();

    private SemanticChangeLog Review(SemanticQueryableDocument current, SemanticQueryableDocument original,
        DocumentSessionOptions? options = null)
    {
        var plugin = new CounterPlugin();
        var zip = TestZips.Build(("PPSA99999/sdimg_SAVE/save.dat", CounterPlugin.Create(5)));
        var workspace = _store.Create("budget", [new ImportSource("dump.zip", () => new MemoryStream(zip))]);
        using var session = new DocumentSession("review", workspace.Set.AllDiskFiles.Single(), plugin, current, options, () => original);
        return session.GetChanges();
    }

    [Theory]
    [InlineData("rows", 3, 1)]
    [InlineData("pages", 1, 1)]
    [InlineData("rows", 0, 0)]
    [InlineData("pages", 0, 0)]
    [InlineData("bytes", 0, 0)]
    public void BudgetsAreGlobalAcrossCollections(string budget, int limit, int expectedQueries)
    {
        var current = new SemanticQueryableDocument(3, 2);
        var options = budget switch
        {
            "rows" => new DocumentSessionOptions { MaxReviewQueryRows = limit },
            "pages" => new DocumentSessionOptions { MaxReviewQueryPages = limit },
            _ => new DocumentSessionOptions { MaxReviewQueryBytes = 0 },
        };
        var log = Review(current, new(3, 2), options);
        Assert.False(log.Complete);
        Assert.Contains(log.Warnings, w => w.Contains("truncated", StringComparison.Ordinal));
        Assert.Equal(0, log.Coverage?.OmittedCount);
        Assert.True(log.Coverage?.OmittedCountIsLowerBound);
        Assert.Equal(expectedQueries, current.Queries.Count);
        Assert.DoesNotContain(log.Changes, c => !c.OriginalExists || !c.CurrentExists);
    }

    [Fact]
    public void RowBudgetReducesTheLastPageAndRetainsProvenChanges()
    {
        var current = new SemanticQueryableDocument();
        current.SetQuantity(0, 200, 99);
        current.SetQuantity(0, 250, 99);
        var log = Review(current, new(), new() { MaxReviewQueryRows = 201 });
        Assert.False(log.Complete);
        Assert.Equal(2, current.Queries.Count);
        Assert.Equal(1, current.Queries[1].Query.Limit);
        Assert.Equal(SemanticQueryableDocument.ValueId(0, 200), Assert.Single(log.Changes).ValueId);
        Assert.True(log.Changes[0].CanRevert);
    }

    [Fact]
    public void PartialOriginalNeverInventsAdditionsInCompleteCurrent()
    {
        var original = new SemanticQueryableDocument
        { TransformPage = p => p.Offset > 0 ? p with { Rows = [] } : p };
        var current = new SemanticQueryableDocument();
        current.SetQuantity(0, 0, 7);
        current.SetQuantity(0, 250, 99);
        var log = Review(current, original);
        Assert.False(log.Complete);
        Assert.Equal(SemanticQueryableDocument.ValueId(0, 0), Assert.Single(log.Changes).ValueId);
    }

    [Fact]
    public void ReadOnlyQueryableFieldsHaveNavigationButCannotRevert()
    {
        var current = new SemanticQueryableDocument(3) { ReadOnlyQuantity = true };
        current.SetQuantity(0, 0, 7);
        var change = Assert.Single(Review(current, new(3) { ReadOnlyQuantity = true }).Changes);
        Assert.NotNull(change.Target);
        Assert.False(change.CanRevert);
    }

    [Fact]
    public void FullSerializedPageIncludingReadOnlyCellsUsesGlobalByteBudget()
    {
        var onePage = new SemanticQueryableDocument(1);
        var page = onePage.Query(SemanticQueryableDocument.Collection(0), new() { IncludeEmpty = true, Limit = 200 });
        var pageBytes = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(page).LongLength;
        var current = new SemanticQueryableDocument(1, 2);
        var log = Review(current, new(1, 2), new() { MaxReviewQueryBytes = pageBytes });
        Assert.False(log.Complete);
        Assert.Single(current.Queries);
        Assert.Contains(log.Warnings, w => w.Contains("byte cap", StringComparison.Ordinal));

        current = new SemanticQueryableDocument(1) { Padding = new string('x', 10_000) };
        current.SetQuantity(0, 0, 7);
        log = Review(current, new(1), new() { MaxReviewQueryBytes = pageBytes });
        Assert.False(log.Complete);
        Assert.Empty(log.Changes); // A rejected page cannot establish a removed field.
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExactFinalPageAtBudgetIsComplete(bool cursor)
    {
        var current = new SemanticQueryableDocument(200) { CursorPaging = cursor };
        var log = Review(current, new(200) { CursorPaging = cursor }, new()
        { MaxReviewQueryRows = 200, MaxReviewQueryPages = 1 });
        Assert.True(log.Complete);
        Assert.Empty(log.Warnings);
        Assert.Single(current.Queries);
    }

    [Fact]
    public void EmptyCollectionsRequireNoBudget()
    {
        var current = new SemanticQueryableDocument(0, 2);
        var log = Review(current, new(0, 2), new()
        { MaxReviewQueryRows = 0, MaxReviewQueryPages = 0, MaxReviewQueryBytes = 0 });
        Assert.True(log.Complete);
        Assert.Empty(current.Queries);
    }

    [Theory]
    [InlineData(9_999, true)]
    [InlineData(10_000, true)]
    [InlineData(10_001, false)]
    public void DefaultRowBoundaryNeverClaimsPartialCoverageIsComplete(int rows, bool complete)
    {
        var current = new SemanticQueryableDocument(rows);
        var log = Review(current, new(rows));
        Assert.Equal(complete, log.Complete);
        Assert.Equal(50, current.Queries.Count);
        Assert.Equal(complete, log.Warnings.Count == 0);
        if (!complete) Assert.Contains(log.Warnings, w => w.Contains("truncated", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(20_000, true)]
    [InlineData(20_001, false)]
    public void HundredPageBoundaryIsExact(int rows, bool complete)
    {
        const long enlargedByteLimit = 64L * 1024 * 1024;
        var current = new SemanticQueryableDocument(rows);
        var log = Review(current, new(rows), new()
        {
            MaxReviewQueryRows = 20_001, MaxReviewQueryPages = 100,
            MaxReviewQueryBytes = enlargedByteLimit, MaxOriginalBytes = enlargedByteLimit,
        });
        Assert.Equal(complete, log.Complete);
        Assert.Equal(100, current.Queries.Count);
        Assert.Equal(complete, log.Warnings.Count == 0);
        if (!complete) Assert.Contains(log.Warnings, w => w.Contains("page cap 100", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(-1, false)]
    [InlineData(0, true)]
    [InlineData(1, true)]
    public void SerializedByteBoundaryIsExact(int difference, bool complete)
    {
        var current = new SemanticQueryableDocument(1) { Padding = "quotes: \"\\\n; ä 🎮 <>&" };
        var original = new SemanticQueryableDocument(1) { Padding = current.Padding };
        var page = current.Query(SemanticQueryableDocument.Collection(0), new() { IncludeEmpty = true, Limit = 200 });
        var bytes = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(page).LongLength;
        current.Queries.Clear();
        var log = Review(current, original, new() { MaxReviewQueryBytes = bytes + difference });
        Assert.Equal(complete, log.Complete);
        Assert.Single(current.Queries);
        Assert.Equal(complete, log.Warnings.Count == 0);
        if (!complete) Assert.Contains(log.Warnings, w => w.Contains("byte cap", StringComparison.Ordinal));
    }

    [Fact]
    public void DistinctRowsBeyondDeclaredTotalCannotProduceACompleteReview()
    {
        var current = new SemanticQueryableDocument(3)
        { TransformPage = p => p with { Rows = [.. p.Rows, p.Rows[0] with { Key = "extra" }] } };
        var log = Review(current, new(3));
        Assert.False(log.Complete);
        Assert.Empty(log.Changes);
        Assert.Contains(log.Warnings, w => w.Contains("declared total", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("wrong-id")]
    [InlineData("wrong-offset")]
    [InlineData("wrong-total")]
    [InlineData("wrong-filtered")]
    [InlineData("wrong-limit")]
    [InlineData("hidden-empty")]
    [InlineData("empty-page")]
    [InlineData("empty-key")]
    [InlineData("duplicate-key")]
    [InlineData("excess-rows")]
    [InlineData("repeated-cursor")]
    [InlineData("empty-cursor")]
    [InlineData("long-cursor")]
    [InlineData("ended-cursor")]
    [InlineData("throws")]
    public void InvalidTraversalNeverClaimsComplete(string fault)
    {
        var current = new SemanticQueryableDocument(401)
        {
            CursorPaging = fault == "ended-cursor",
            TransformPage = p => fault switch
            {
                "wrong-id" => p with { CollectionId = "other" },
                "wrong-offset" => p with { Offset = -1 },
                "wrong-total" => p with { TotalCount = p.TotalCount + 1 },
                "wrong-filtered" => p with { FilteredCount = p.TotalCount - 1 },
                "wrong-limit" => p with { Limit = p.Limit - 1 },
                "hidden-empty" => p with { HiddenEmptyCount = 1 },
                "empty-page" => p with { Rows = [] },
                "empty-key" => p with { Rows = [p.Rows[0] with { Key = "" }] },
                "duplicate-key" => p with { Rows = [p.Rows[0], p.Rows[0]] },
                "excess-rows" => p with { Rows = [.. p.Rows, p.Rows[0]] },
                "repeated-cursor" => p with { NextCursor = "200" },
                "empty-cursor" => p with { NextCursor = "" },
                "long-cursor" => p with { NextCursor = new string('x', QueryableCollectionLimits.MaxCursorLength + 1) },
                "ended-cursor" when p.Offset > 0 => p with { NextCursor = null },
                "throws" => throw new InvalidOperationException("Synthetic query failure"),
                _ => p,
            },
        };
        var log = Review(current, new(401));
        Assert.False(log.Complete);
        Assert.Contains(log.Warnings, w => w.Contains("truncated", StringComparison.Ordinal));
        Assert.InRange(current.Queries.Count, 1, 2);
        Assert.DoesNotContain(log.Changes, c => !c.OriginalExists || !c.CurrentExists);
    }

    [Fact]
    public void EagerAliasKeepsLegacyLabelPathAndHasNoTarget()
    {
        var current = new SemanticQueryableDocument(3) { EagerAlias = true };
        current.SetQuantity(0, 0, 7);
        var change = Assert.Single(Review(current, new(3) { EagerAlias = true }).Changes);
        Assert.Equal("Legacy quantity", change.Label);
        Assert.Equal(["section:Root", "section:inventory"], change.Path);
        Assert.Null(change.Target);
    }

    [Fact]
    public void QueryLazyAliasesOfAlreadyVisitedCanonicalSectionsAreComplete()
    {
        var current = new SemanticQueryableDocument(3) { QueryLazyAlias = true };
        current.SetQuantity(0, 0, 7);
        var log = Review(current, new(3) { QueryLazyAlias = true });
        Assert.True(log.Complete);
        var change = Assert.Single(log.Changes);
        Assert.Equal("Legacy quantity", change.Label);
        Assert.Equal(["section:Root", "section:canonical"], change.Path);
        Assert.Null(change.Target);
    }

    [Fact]
    public void QueryLazyCyclesStillReportIncompleteCoverage()
    {
        var current = new SemanticQueryableDocument(3) { QueryLazyCycle = true };
        var log = Review(current, new(3) { QueryLazyCycle = true });
        Assert.False(log.Complete);
        Assert.NotEmpty(log.Warnings);
        Assert.Single(current.Queries);
    }

    [Fact]
    public void DistinctFieldIdentitiesAreNotGuessedToBeAliasesFromEqualValues()
    {
        var current = new SemanticQueryableDocument(3) { DistinctAlias = true };
        current.SetQuantity(0, 0, 7);
        var log = Review(current, new(3) { DistinctAlias = true });
        Assert.True(log.Complete);
        Assert.Equal(2, log.Changes.Count);
        Assert.Equal("raw/" + SemanticQueryableDocument.ValueId(0, 0), log.Changes[0].ValueId);
        Assert.Null(log.Changes[0].Target);
        Assert.Equal(SemanticQueryableDocument.ValueId(0, 0), log.Changes[1].ValueId);
        Assert.NotNull(log.Changes[1].Target);
        Assert.All(log.Changes, change => Assert.Equal("7", change.CurrentValue.ToString()));
    }

    [Fact]
    public void CompleteSnapshotsProveAddedAndRemovedRowsWithoutOfferingStructuralRevert()
    {
        var removed = Assert.Single(Review(new(2), new(3)).Changes);
        Assert.True(removed.OriginalExists);
        Assert.False(removed.CurrentExists);
        Assert.False(removed.CanRevert);
        Assert.Null(removed.Target);
        var added = Assert.Single(Review(new(3), new(2)).Changes);
        Assert.False(added.OriginalExists);
        Assert.True(added.CurrentExists);
        Assert.False(added.CanRevert);
        Assert.NotNull(added.Target);
    }
}
