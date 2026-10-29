using SaveToolkit.Core.Sessions;
using SaveToolkit.Core.Sessions.Groups;
using SaveToolkit.Core.Storage;
using SaveToolkit.Core.Tests.Fakes;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Core.Tests;

public sealed class GroupTraversalCancellationTests
{
    [Fact]
    public void Cancelled_query_preserves_only_previously_retained_pages()
    {
        using var fixture = new Fixture();
        using var cancellation = new CancellationTokenSource();
        _ = fixture.Session.QueryCollection("items", new() { Text = "kept", Limit = 1 });
        var state = fixture.Session.State;
        fixture.Current.Callback = stage => { if (stage == "query:cancelled") cancellation.Cancel(); };

        Assert.ThrowsAny<OperationCanceledException>(() => fixture.Session.QueryCollection("items",
            new() { Text = "cancelled", Limit = 1 }, null, cancellation.Token));

        fixture.Current.Callback = null;
        _ = fixture.Staged.PlanAction("plan", new Dictionary<string, FieldValue>(), new());
        Assert.Equal(["kept"], fixture.Documents[^1].Queries);
        Assert.Equal(state, fixture.Session.State);
        Assert.True(fixture.Documents[^1].Disposed);
    }

    [Fact]
    public void Cancelled_review_preserves_only_previously_retained_lazy_sections()
    {
        using var fixture = new Fixture();
        using var cancellation = new CancellationTokenSource();
        _ = fixture.Staged.ExpandNode("kept");
        fixture.Current.Callback = stage => { if (stage == "expand:cancelled") cancellation.Cancel(); };

        Assert.ThrowsAny<OperationCanceledException>(() => fixture.Session.GetChanges(cancellation.Token));

        fixture.Current.Callback = null;
        _ = fixture.Staged.PlanAction("plan", new Dictionary<string, FieldValue>(), new());
        Assert.Equal(["kept"], fixture.Documents[^1].LazyIds);
        Assert.Empty(fixture.Documents[^1].Queries);
        Assert.False(fixture.Session.State.IsDirty);
    }

    [Theory]
    [InlineData("capture")]
    [InlineData("preflight")]
    [InlineData("open")]
    [InlineData("restore")]
    [InlineData("view")]
    [InlineData("expand:kept")]
    [InlineData("query:kept")]
    [InlineData("plan")]
    public void Cancelled_planning_stops_candidate_traversal_and_disposes_candidate(string stage)
    {
        using var fixture = new Fixture();
        using var cancellation = new CancellationTokenSource();
        _ = fixture.Staged.ExpandNode("kept");
        _ = fixture.Staged.Query("items", new() { Text = "kept", Limit = 1 });
        _ = fixture.Staged.Query("items", new() { Text = "later", Limit = 1 });
        fixture.Current.Callback = current => { if (stage == "capture" && current == stage) cancellation.Cancel(); };
        fixture.CandidateCallback = current => { if (current == stage) cancellation.Cancel(); };
        fixture.Preflight = token =>
        {
            if (stage == "preflight") cancellation.Cancel();
            token.ThrowIfCancellationRequested();
        };

        Assert.ThrowsAny<OperationCanceledException>(() => fixture.Staged.PlanForSession("plan",
            new Dictionary<string, FieldValue>(), new(), cancellation.Token));

        Assert.False(fixture.Current.Disposed);
        if (stage is "capture" or "preflight") Assert.Single(fixture.Documents);
        else
        {
            Assert.Equal(2, fixture.Documents.Count);
            Assert.True(fixture.Documents[^1].Disposed);
            if (stage == "query:kept") Assert.Equal(["kept"], fixture.Documents[^1].Queries);
        }
        Assert.False(fixture.Session.State.IsDirty);
    }

    private sealed class Fixture : IDisposable
    {
        private readonly string _root = TestZips.TempDir();
        public List<TrackingDocument> Documents { get; } = [];
        public TrackingDocument Current => Documents[0];
        public Action<string>? CandidateCallback { get; set; }
        public Action<CancellationToken>? Preflight { get; set; }
        public StagedGroupDocument Staged { get; }
        public DocumentSession Session { get; }

        public Fixture()
        {
            var set = SaveSetImporter.Import([new ImportSource("save.dat", () => new MemoryStream(CounterPlugin.Create(5)))], _root);
            TrackingDocument Open()
            {
                var document = new TrackingDocument { Callback = stage => CandidateCallback?.Invoke(stage) };
                Documents.Add(document);
                document.Callback?.Invoke("open");
                return document;
            }
            Staged = new StagedGroupDocument(Open, _ => [], token => Preflight?.Invoke(token), 1024);
            Session = new DocumentSession("fixture", Assert.Single(set.AllDiskFiles), new CounterPlugin(), Staged, openOriginal: Open);
            _ = Session.BuildView();
        }

        public void Dispose()
        {
            Session.Dispose();
            Directory.Delete(_root, recursive: true);
        }
    }

    private sealed class TrackingDocument : BoundSaveDocument, IGroupedSaveDocument, IQueryableDocument, IActionPlanningDocument
    {
        private static readonly QueryableColumn[] Columns = [new("value", "Value", IntegerSpec.Int32)];
        public Action<string>? Callback { get; set; }
        public List<string> Queries { get; } = [];
        public List<string> LazyIds { get; } = [];
        public bool Disposed { get; private set; }
        public override string Title => "Cancellation fixture";
        public override ViewNode BuildView()
        {
            Callback?.Invoke("view");
            return new SectionNode(Title, [
                Field("counter", "Counter", IntegerSpec.Int32, () => FieldValue.Of(5), _ => { }),
                new SectionNode("Kept", []) { LazyId = "kept" },
                new SectionNode("Cancelled", []) { LazyId = "cancelled" },
                new QueryableTableNode("items", "Items", Columns, 1),
                new ActionNode("plan", "Plan") { SupportsPlanning = true }]);
        }
        public override IReadOnlyList<ViewNode> ExpandNode(string lazyId)
        {
            LazyIds.Add(lazyId);
            Callback?.Invoke("expand:" + lazyId);
            return [];
        }
        public IReadOnlyList<QueryableCollection> Collections => [new("items", "Items", Columns, 1)];
        public QueryableCollectionPage Query(string collectionId, QueryableCollectionQuery query)
        {
            Queries.Add(query.Text ?? "");
            Callback?.Invoke("query:" + query.Text);
            return new(collectionId, Columns, [new TableRow("1", new Dictionary<string, ViewNode>())], 1, 1, query.Offset, query.Limit);
        }
        public ActionPlan PlanAction(string actionId, IReadOnlyDictionary<string, FieldValue> parameters, ActionScope scope)
        {
            Callback?.Invoke("plan");
            return new();
        }
        public EditOutcome ApplyActionPlan(string actionId, IReadOnlyDictionary<string, FieldValue> parameters, ActionScope scope, ActionPlan plan) => new();
        public DocumentMemento CaptureMemento() { Callback?.Invoke("capture"); return DocumentMemento.Snapshot(0, 1); }
        protected override DocumentMemento CaptureSnapshot() => DocumentMemento.Snapshot(0, 1);
        protected override void LoadSnapshot(DocumentMemento snapshot) => Callback?.Invoke("restore");
        public override IReadOnlyList<FileOutput> Serialize() => [];
        void IDisposable.Dispose() => Disposed = true;
    }
}
