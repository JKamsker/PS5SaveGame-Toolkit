using SaveToolkit.Core.Sessions;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Core.Tests;

public sealed class TraversalMutationEffectivenessTests
{
    [Theory]
    [InlineData(0, true)]
    [InlineData(-1, false)]
    public void RetainedCursorNavigationCountsTowardsTheSemanticMemoryBudget(int adjustment, bool complete)
    {
        const int cursorLength = 4096;
        const int fieldCostWithoutCursor = 256 + 2 * (1 + 13 + 1 + 5 + 5 + 1);
        TraversalProbe Document(bool changed) => new()
        {
            Read = (_, call) => Page(call - 1, call == 1 ? "A" : "B") with
            {
                NextCursor = call == 1 ? new string('c', cursorLength) : null,
                Rows = [new TableRow(call == 1 ? "A" : "B", new Dictionary<string, ViewNode>
                { ["value"] = new FieldNode(call == 1 ? "a" : "b", "L", new SaveToolkit.Sdk.Values.TextSpec(),
                    SaveToolkit.Sdk.Values.FieldValue.Of(changed && call == 2 ? "y" : "x")) })],
            },
        };
        var review = new SemanticReview(new() { MaxOriginalBytes = 2 * fieldCostWithoutCursor + 2 * cursorLength + adjustment });
        review.EnsureOriginal(() => Document(false));
        using var current = Document(true);
        var log = review.Query(current, _ => { }, _ => { }, 0);
        Assert.Equal(complete, log.Complete);
        Assert.Equal(complete ? 1 : 0, log.Changes.Count);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ExhaustedEagerBudgetsPreventQueuedCollectionQueries(bool bytes)
    {
        ViewNode[] padding = bytes
            ? [new FieldNode("v", "V", new SaveToolkit.Sdk.Values.TextSpec(), SaveToolkit.Sdk.Values.FieldValue.Of("x"))]
            : Enumerable.Repeat<ViewNode>(new TextNode("padding"), 99_998).ToArray();
        ViewNode view = new SectionNode("Root", [new QueryableTableNode("items", "Items", [], 2), .. padding]);
        TraversalProbe Document() => new() { View = view };
        const int fieldCost = 256 + 2 * (1 + 1 + 1 + 12); // Includes the section:Root path.
        var review = new SemanticReview(new() { MaxOriginalBytes = bytes ? fieldCost : 16 * 1024 * 1024 });
        review.EnsureOriginal(Document);
        using var current = Document();
        Assert.False(review.Query(current, _ => { }, _ => { }, 0).Complete);
        Assert.Equal(0, current.Calls);
    }
    [Fact]
    public void InvalidDeclaredTotalsNeverReachTheProvider()
    {
        using var document = new TraversalProbe();
        var traversal = new SemanticQueryableTraversal(document, new());
        traversal.Visit(new("items", "Items", [], -1), (_, _, _) => true);
        Assert.Equal(0, document.Calls);
        Assert.Single(traversal.Warnings);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(4096, true)]
    [InlineData(4097, false)]
    public void CursorLengthsRespectBothEndpoints(int length, bool valid)
    {
        using var document = new TraversalProbe
        {
            Read = (_, call) => Page(call - 1, call.ToString()) with
                { NextCursor = call == 1 ? new string('x', length) : null },
        };
        var traversal = new SemanticQueryableTraversal(document, new());
        traversal.Visit(new("items", "Items", [], 2), (_, _, _) => true);
        Assert.Equal(valid ? 2 : 1, document.Calls);
        Assert.Equal(valid, traversal.Warnings.Count == 0);
    }

    [Fact]
    public void AnInitialPositiveOffsetMismatchCannotSkipRows()
    {
        using var document = new TraversalProbe { Read = (_, _) => Page(1, "second") };
        var traversal = new SemanticQueryableTraversal(document, new());
        var visits = 0;
        traversal.Visit(new("items", "Items", [], 2), (_, _, _) => { visits++; return true; });
        Assert.Equal(0, visits);
        Assert.Single(traversal.Warnings);
    }

    [Fact]
    public void LaterPagesCannotExceedTheRemainingDeclaredRows()
    {
        using var document = new TraversalProbe
        {
            Read = (_, call) => call == 1 ? Page(0, "first") : Page(1, "second") with
            { Rows = [Row("second"), Row("extra")] },
        };
        var traversal = new SemanticQueryableTraversal(document, new());
        var visits = 0;
        traversal.Visit(new("items", "Items", [], 2), (_, _, _) => { visits++; return true; });
        Assert.Equal(1, visits);
        Assert.Single(traversal.Warnings);
    }

    [Fact]
    public void RejectedCellsStopTraversalAndRetainATruncationWarning()
    {
        using var document = new TraversalProbe();
        var traversal = new SemanticQueryableTraversal(document, new());
        var visits = 0;
        traversal.Visit(new("items", "Items", [], 2), (_, _, _) => { visits++; return false; });
        Assert.Equal(1, visits);
        Assert.Equal(1, document.Calls);
        Assert.Single(traversal.Warnings);
    }

    [Theory]
    [InlineData("bytes")]
    [InlineData("bytes-twice")]
    [InlineData("nodes")]
    [InlineData("depth")]
    [InlineData("depth-twice")]
    [InlineData("tabs")]
    [InlineData("rows")]
    public void QueryCellTraversalCapsStopSubsequentProviderCalls(string cap)
    {
        ViewNode cell = new FieldNode("value", "Value", new SaveToolkit.Sdk.Values.TextSpec(), SaveToolkit.Sdk.Values.FieldValue.Of("x"));
        if (cap == "nodes") cell = new SectionNode("Wide", Enumerable.Repeat<ViewNode>(new TextNode("padding"), 100_000).ToArray());
        if (cap.StartsWith("depth", StringComparison.Ordinal))
            for (var i = 0; i < 128; i++) cell = new SectionNode("Nested", [cell]);
        if (cap is "bytes-twice" or "depth-twice") cell = new SectionNode("Twice", [cell, cell]);
        if (cap == "tabs") cell = new TabsNode(Enumerable.Repeat(new Tab("Tab", []), 100_000).ToArray());
        if (cap == "rows") cell = new TableNode([], Enumerable.Repeat(new TableRow("row", new Dictionary<string, ViewNode>()), 100_000).ToArray());
        TraversalProbe Document() => new()
        {
            Read = (_, call) => Page(call - 1, call.ToString()) with
            { Rows = [new TableRow(call.ToString(), new Dictionary<string, ViewNode> { ["value"] = cell })] },
        };
        var review = new SemanticReview(new() { MaxOriginalBytes = cap.StartsWith("bytes", StringComparison.Ordinal) ? 1 : 16 * 1024 * 1024 });
        review.EnsureOriginal(Document);
        using var current = Document();
        var log = review.Query(current, _ => { }, _ => { }, 0);
        Assert.False(log.Complete);
        Assert.Equal(1, current.Calls);
        Assert.NotEmpty(log.Warnings);
    }

    private static TableRow Row(string key) => new(key, new Dictionary<string, ViewNode> { ["text"] = new TextNode(key) });
    private static QueryableCollectionPage Page(int offset, string key) => new("items", [], [Row(key)], 2, 2, offset, 200);

    private sealed class TraversalProbe : BoundSaveDocument, IQueryableDocument
    {
        public Func<QueryableCollectionQuery, int, QueryableCollectionPage> Read { get; init; } = (_, call) => Page(call - 1, call.ToString());
        public int Calls { get; private set; }
        public ViewNode? View { get; init; }
        public override string Title => "Traversal";
        public IReadOnlyList<QueryableCollection> Collections => [new("items", "Items", [], 2)];
        public override ViewNode BuildView() => View ?? new QueryableTableNode("items", "Items", [], 2);
        public QueryableCollectionPage Query(string collectionId, QueryableCollectionQuery query) => Read(query, ++Calls);
        public override IReadOnlyList<FileOutput> Serialize() => [];
    }
}
