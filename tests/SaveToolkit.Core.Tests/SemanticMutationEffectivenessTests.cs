using System.Text.Json;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Core.Tests.Fakes;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Core.Tests;

public sealed class SemanticMutationEffectivenessTests
{
    [Fact]
    public void TabChildrenAreReviewedWithTheirFullNavigationPath()
    {
        static ViewNode View(string value) => new SectionNode("Outer", [new TabsNode([new("Tab",
            [new FieldNode("value", "Value", new TextSpec(), FieldValue.Of(value))])])]);
        var review = new SemanticReview(new());
        review.EnsureOriginal(() => new ReviewProbe(View("x")));
        using var current = new ReviewProbe(View("y"));
        var change = Assert.Single(review.Query(current, _ => { }, _ => { }, 0).Changes);
        Assert.Equal(["section:Outer", "tab:Tab"], change.Path);
    }

    [Fact]
    public void UnreadableCurrentViewsCannotProveThatOriginalFieldsWereRemoved()
    {
        ViewNode view = new FieldNode("value", "Value", new TextSpec(), FieldValue.Of("x"));
        var review = new SemanticReview(new());
        review.EnsureOriginal(() => new ReviewProbe(view));
        using var current = new ReviewProbe(view) { FailBuild = true };
        var log = review.Query(current, _ => { }, _ => { }, 0);
        Assert.False(log.Complete);
        Assert.Empty(log.Changes);
    }

    [Fact]
    public void UnknownOriginalValuesAreNeverReportedAsRestorable()
    {
        var review = new SemanticReview(new());
        review.EnsureOriginal(() => new ReviewProbe(new SectionNode("Root", [])));
        Assert.False(review.TryOriginal("missing", out _));
    }

    [Fact]
    public void RenderedFieldsShareOneCombinedMemoryBudget()
    {
        const int fieldCost = 256 + 2 * (1 + 1 + 1 + 12);
        ViewNode view = new SectionNode("Root", [new FieldNode("a", "A", new TextSpec(), FieldValue.Of("x")),
            new FieldNode("b", "B", new TextSpec(), FieldValue.Of("y"))]);
        var review = new SemanticReview(new() { MaxOriginalBytes = 2 * fieldCost - 1 });
        review.EnsureOriginal(() => new ReviewProbe(view));
        using var current = new ReviewProbe(view);
        Assert.False(review.Query(current, _ => { }, _ => { }, 0).Complete);
    }

    [Theory]
    [InlineData("rows")]
    [InlineData("pages")]
    [InlineData("bytes")]
    public void NegativeTraversalBudgetsAreRejected(string kind)
    {
        var options = kind switch
        {
            "rows" => new DocumentSessionOptions { MaxReviewQueryRows = -1 },
            "pages" => new() { MaxReviewQueryPages = -1 },
            _ => new() { MaxReviewQueryBytes = -1 },
        };
        Assert.Throws<ArgumentOutOfRangeException>(() => new SemanticReview(options));
    }

    [Theory]
    [InlineData("v", "Label", "A", false)]
    [InlineData("long-value-id", "L", "A", false)]
    [InlineData("v", "L", "long text", false)]
    [InlineData("v", "L", "A", true)]
    public void FieldMemoryIncludesIdsLabelsValuesAndNavigationPaths(string id, string label, string value, bool nested)
    {
        const int fieldOverhead = 256;
        const int utf16Bytes = 2;
        const string sectionTitle = "Long section title";
        var pathLength = nested ? ("section:" + sectionTitle).Length : 0;
        var cost = fieldOverhead + utf16Bytes * (id.Length + label.Length + value.Length + pathLength);
        ViewNode View(string text)
        {
            var field = new FieldNode(id, label, new TextSpec(), FieldValue.Of(text));
            return nested ? new SectionNode(sectionTitle, [field]) : field;
        }
        foreach (var excess in new[] { 0, 1 })
        {
            var review = new SemanticReview(new() { MaxOriginalBytes = cost - excess });
            review.EnsureOriginal(() => new ReviewProbe(View(value)));
            using var current = new ReviewProbe(View(new string('B', value.Length)));
            var log = review.Query(current, _ => { }, _ => { }, revision: 0);
            Assert.Equal(excess == 0, log.Complete);
            if (excess == 0) Assert.Single(log.Changes);
            else Assert.Empty(log.Changes);
        }
    }

    [Fact]
    public void ReportedHiddenValuesShareTheCurrentSnapshotBudgetWithRenderedValues()
    {
        const int hiddenFieldBytes = 256 + 2 * (2 + 2 + 1);
        var review = new SemanticReview(new() { MaxOriginalBytes = 3 * hiddenFieldBytes });
        review.EnsureOriginal(() => new ReviewProbe(new SectionNode("Root", [])));
        ValueChange[] changes = Enumerable.Range(0, 3).Select(i => new ValueChange($"h{i}", FieldValue.Of("A"), FieldValue.Of("B"))).ToArray();
        review.Observe(changes, structural: false, _ => false);
        using var current = new ReviewProbe(new FieldNode("visible", "Visible", new TextSpec(), FieldValue.Of("B")));
        foreach (var change in changes) current.BindHidden(change.ValueId, change.NewValue);

        var log = review.Query(current, _ => { }, _ => { }, revision: 1);

        Assert.False(log.Complete);
        Assert.Single(log.Changes, change => change.ValueId.StartsWith('h'));
    }

    [Fact]
    public void QueryPageBytesAccumulateAcrossCollectionsBeforeTheFinalPageFits()
    {
        using var probe = new SemanticQueryableDocument(1);
        var page = probe.Query(SemanticQueryableDocument.Collection(0), new() { IncludeEmpty = true, Limit = 200 });
        var pageBytes = JsonSerializer.SerializeToUtf8Bytes(page).LongLength;
        var review = new SemanticReview(new() { MaxReviewQueryBytes = pageBytes * 2 - 1 });
        review.EnsureOriginal(() => new SemanticQueryableDocument(1, 2));
        using var current = new SemanticQueryableDocument(1, 2);
        var log = review.Query(current, _ => { }, _ => { }, revision: 0);
        Assert.False(log.Complete);
        Assert.Contains(log.Warnings, warning => warning.Contains("byte cap", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ReviewStopsAtTheDeclaredDepthAndNodeBounds(bool depth)
    {
        const int maxDepth = 128;
        const int maxNodes = 100_000;
        ViewNode view = new FieldNode("value", "Value", new TextSpec(), FieldValue.Of("A"));
        if (depth)
            for (var i = 0; i <= maxDepth; i++) view = new SectionNode("Nested", [view]);
        else
            view = new SectionNode("Wide", Enumerable.Repeat<ViewNode>(new TextNode("padding"), maxNodes).Append(view).ToArray());
        var review = new SemanticReview(new());
        review.EnsureOriginal(() => new ReviewProbe(view));
        using var current = new ReviewProbe(view);
        var log = review.Query(current, _ => { }, _ => { }, revision: 0);
        Assert.False(log.Complete);
        Assert.Empty(log.Changes);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ExactDepthAndNodeEndpointsRemainReviewable(bool depth)
    {
        const int maxDepth = 128;
        const int maxNodes = 100_000;
        ViewNode view = new FieldNode("value", "Value", new TextSpec(), FieldValue.Of("A"));
        if (depth)
            for (var i = 0; i < maxDepth; i++) view = new SectionNode("Nested", [view]);
        else
            view = new SectionNode("Wide", Enumerable.Repeat<ViewNode>(new TextNode("padding"), maxNodes - 2).Append(view).ToArray());
        var review = new SemanticReview(new());
        review.EnsureOriginal(() => new ReviewProbe(view));
        using var current = new ReviewProbe(view);
        Assert.True(review.Query(current, _ => { }, _ => { }, revision: 0).Complete);
    }

    [Theory]
    [InlineData(true, 99_999, true)]
    [InlineData(false, 99_999, true)]
    [InlineData(true, 100_000, false)]
    [InlineData(false, 100_000, false)]
    public void EmptyTabsAndRowsStillConsumeTheTraversalNodeBudget(bool tabs, int count, bool complete)
    {
        ViewNode view = tabs ? new TabsNode(Enumerable.Repeat(new Tab("Tab", []), count).ToArray())
            : new TableNode([], Enumerable.Repeat(new TableRow("row", new Dictionary<string, ViewNode>()), count).ToArray());
        var review = new SemanticReview(new());
        review.EnsureOriginal(() => new ReviewProbe(view));
        using var current = new ReviewProbe(view);
        Assert.Equal(complete, review.Query(current, _ => { }, _ => { }, revision: 0).Complete);
    }

    [Fact]
    public void FailedOriginalOpenProducesAnIncompleteReviewInsteadOfInventingAdditions()
    {
        var review = new SemanticReview(new());
        review.EnsureOriginal(() => throw new InvalidOperationException("Cannot reopen."));
        using var current = new ReviewProbe(new FieldNode("value", "Value", new TextSpec(), FieldValue.Of("A")));
        var log = review.Query(current, _ => { }, _ => { }, 0);
        Assert.False(log.Complete);
        Assert.Empty(log.Changes);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void HiddenReportedFieldsRespectTheExactBudgetAndUnreadableBindings(bool readable)
    {
        const int cost = 256 + 2 * (1 + 1 + 1);
        var review = new SemanticReview(new() { MaxOriginalBytes = cost });
        review.EnsureOriginal(() => new ReviewProbe(new SectionNode("Root", [])));
        review.Observe([new("h", FieldValue.Of("A"), FieldValue.Of("B"))], false, _ => false);
        using var current = new ReviewProbe(new SectionNode("Root", []));
        if (readable) current.BindHidden("h", FieldValue.Of("B"));
        var log = review.Query(current, _ => { }, _ => { }, 0);
        Assert.Equal(readable, log.Complete);
        Assert.Equal(readable ? 1 : 0, log.Changes.Count);
    }

    private sealed class ReviewProbe(ViewNode view) : BoundSaveDocument
    {
        public bool FailBuild { get; init; }
        public override string Title => "Review probe";
        public void BindHidden(string id, FieldValue value) => RegisterField(id, new TextSpec(), () => value);
        public override ViewNode BuildView() => FailBuild ? throw new InvalidOperationException("Cannot read view.") : view;
        public override IReadOnlyList<FileOutput> Serialize() => [];
    }
}
