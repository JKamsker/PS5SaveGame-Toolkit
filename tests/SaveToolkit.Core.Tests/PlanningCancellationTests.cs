using System.Text.Json;
using SaveToolkit.Core.Plugins;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Core.Storage;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Core.Tests;

public sealed class PlanningCancellationTests : IDisposable
{
    private static readonly Dictionary<string, JsonElement> NoParameters = [];
    private readonly WorkspaceStore _store = new(new PluginCatalog([]),
        new WorkspaceStoreOptions { DataRoot = TestZips.TempDir() });

    public void Dispose() => _store.Dispose();

    private DocumentSession Open(PlanningDocument document, Func<ISaveDocument>? openOriginal = null)
    {
        var workspace = _store.Create("cancel planning", [new ImportSource("save.dat", () => new MemoryStream([0]))]);
        var session = new DocumentSession("cancel", workspace.Set.AllDiskFiles.Single(), new Fakes.PlanningPlugin(),
            document, openOriginal: openOriginal ?? (() => new PlanningDocument()));
        session.BuildView();
        return session;
    }

    [Fact]
    public void CancelledReplacementPlanRetainsPreviousPreviewAndSessionState()
    {
        using var cancellation = new CancellationTokenSource();
        var document = new PlanningDocument();
        using var session = Open(document);
        var previous = session.PlanAction("fill", NoParameters);
        var before = session.State;
        document.OnPlan = cancellation.Cancel;

        Assert.Throws<OperationCanceledException>(() => session.PlanAction("fill", NoParameters, null, null, cancellation.Token));

        Assert.Equal(before, session.State);
        Assert.Equal(0, document.ApplyCount);
        document.OnPlan = null;
        Assert.True(session.ApplyActionPlan(previous.PlanId!, previous.Revision).Ok);
        Assert.Equal(10, document.Value);
    }

    [Fact]
    public void CancelledRevalidationRetainsPreviewAndDoesNotMutate()
    {
        using var cancellation = new CancellationTokenSource();
        var document = new PlanningDocument();
        using var session = Open(document);
        var preview = session.PlanAction("fill", NoParameters);
        var before = session.State;
        document.OnPlan = cancellation.Cancel;

        Assert.Throws<OperationCanceledException>(() => session.ApplyActionPlan(preview.PlanId!, preview.Revision, cancellation.Token));

        Assert.Equal(before, session.State);
        Assert.Equal(2, document.Value);
        Assert.Equal(0, document.ApplyCount);
        document.OnPlan = null;
        Assert.True(session.ApplyActionPlan(preview.PlanId!, preview.Revision).Ok);
    }

    [Fact]
    public void CancelledQueryScopeStopsAfterFirstPageAndRetainsPreviousPreview()
    {
        using var cancellation = new CancellationTokenSource();
        var document = new PlanningDocument { OnQuery = cancellation.Cancel };
        using var session = Open(document);
        var previous = session.PlanAction("fill", NoParameters);
        var scope = new ActionScope { QueryScope = new ActionQueryScope("items", new QueryableCollectionQuery(), 0) };
        var before = session.State;

        Assert.Throws<OperationCanceledException>(() => session.PlanAction("fill", NoParameters, scope, 0, cancellation.Token));

        Assert.Equal(1, document.QueryCount);
        Assert.Equal(before, session.State);
        Assert.Equal(0, document.ApplyCount);
        Assert.True(session.ApplyActionPlan(previous.PlanId!, previous.Revision).Ok);
    }

    [Fact]
    public void CancelledQueryRevalidationDoesNotConsumePreview()
    {
        using var cancellation = new CancellationTokenSource();
        var document = new PlanningDocument();
        using var session = Open(document);
        var scope = new ActionScope { QueryScope = new ActionQueryScope("items", new QueryableCollectionQuery(), 0) };
        var preview = session.PlanAction("fill", NoParameters, scope);
        var queriesBefore = document.QueryCount;
        var before = session.State;
        document.OnQuery = () => { if (document.QueryCount == queriesBefore + 2) cancellation.Cancel(); };

        Assert.Throws<OperationCanceledException>(() => session.ApplyActionPlan(preview.PlanId!, preview.Revision, cancellation.Token));

        Assert.Equal(queriesBefore + 2, document.QueryCount);
        Assert.Equal(before, session.State);
        Assert.Equal(2, document.Value);
        Assert.Equal(0, document.ApplyCount);
        document.OnQuery = null;
        Assert.True(session.ApplyActionPlan(preview.PlanId!, preview.Revision).Ok);
    }

    [Fact]
    public void CancelledMetadataValidationDoesNotConsumePreview()
    {
        using var cancellation = new CancellationTokenSource();
        var document = new PlanningDocument();
        using var session = Open(document);
        var preview = session.PlanAction("fill", NoParameters);
        var before = session.State;
        document.OnMetadata = cancellation.Cancel;

        Assert.Throws<OperationCanceledException>(() => session.ApplyActionPlan(preview.PlanId!, preview.Revision, cancellation.Token));

        Assert.Equal(before, session.State);
        Assert.Equal(2, document.Value);
        Assert.Equal(0, document.ApplyCount);
        document.OnMetadata = null;
        Assert.True(session.ApplyActionPlan(preview.PlanId!, preview.Revision).Ok);
    }

    [Fact]
    public void CancelledBaselineCaptureCanRetrySamePreviewWithoutCachedPartialBaseline()
    {
        using var cancellation = new CancellationTokenSource();
        var document = new PlanningDocument();
        var opens = 0;
        using var session = Open(document, () =>
        {
            opens++;
            return new PlanningDocument { OnBuild = opens == 1 ? cancellation.Cancel : null };
        });
        var preview = session.PlanAction("fill", NoParameters);
        var before = session.State;

        Assert.Throws<OperationCanceledException>(() => session.ApplyActionPlan(preview.PlanId!, preview.Revision, cancellation.Token));

        Assert.Equal(before, session.State);
        Assert.Equal(2, document.Value);
        Assert.Equal(0, document.ApplyCount);
        Assert.True(session.ApplyActionPlan(preview.PlanId!, preview.Revision).Ok);
        Assert.Equal(2, opens);
        Assert.True(session.GetChanges().Complete);
        Assert.Equal("2", Assert.Single(session.GetChanges().Changes).OriginalValue.ToString());
    }

    [Fact]
    public void CancellationAfterAtomicApplyBoundaryCompletesMutationAndHistory()
    {
        using var cancellation = new CancellationTokenSource();
        var document = new PlanningDocument { OnApply = cancellation.Cancel };
        using var session = Open(document);
        var preview = session.PlanAction("fill", NoParameters);

        var result = session.ApplyActionPlan(preview.PlanId!, preview.Revision, cancellation.Token);

        Assert.True(cancellation.IsCancellationRequested);
        Assert.True(result.Ok);
        Assert.Equal(10, document.Value);
        Assert.Equal(1, document.ApplyCount);
        Assert.True(result.State.CanUndo);
        Assert.Equal(preview.Revision + 1, result.State.Revision);
        Assert.True(session.Undo().Ok);
        Assert.Equal(2, document.Value);
        Assert.False(session.State.IsDirty);
    }

    [Fact]
    public void PreCancelledApplyDoesNotConsumePreview()
    {
        using var cancellation = new CancellationTokenSource();
        var document = new PlanningDocument();
        using var session = Open(document);
        var preview = session.PlanAction("fill", NoParameters);
        cancellation.Cancel();

        Assert.Throws<OperationCanceledException>(() => session.ApplyActionPlan(preview.PlanId!, preview.Revision, cancellation.Token));

        Assert.Equal(0, document.ApplyCount);
        Assert.True(session.ApplyActionPlan(preview.PlanId!, preview.Revision).Ok);
    }

    private sealed class PlanningDocument : BoundSaveDocument, IActionPlanningDocument, IQueryableDocument, IFieldMetadataDocument
    {
        private const int RowCount = 401;
        private static readonly QueryableColumn[] Columns = [new("name", "Name")];
        public long Value { get; private set; } = 2;
        public int ApplyCount { get; private set; }
        public int QueryCount { get; private set; }
        public Action? OnPlan { get; set; }
        public Action? OnMetadata { get; set; }
        public Action? OnBuild { get; init; }
        public Action? OnApply { get; init; }
        public Action? OnQuery { get; set; }
        public override string Title => "Cancellation planning fixture";
        public IReadOnlyList<QueryableCollection> Collections => [new("items", "Items", Columns, RowCount)];

        public PlanningDocument() => RegisterField(new FieldMetadata("value", "Value", IntegerSpec.Int32),
            () => FieldValue.Of(Value), v => Value = v.AsInt64());

        public override IReadOnlyList<FieldMetadata> ResolveFieldMetadata(IReadOnlyList<string> valueIds)
        {
            OnMetadata?.Invoke();
            return base.ResolveFieldMetadata(valueIds);
        }

        public override ViewNode BuildView()
        {
            OnBuild?.Invoke();
            return new SectionNode("Root",
        [
            Field("value", "Value"),
            new ActionNode("fill", "Fill") { SupportsPlanning = true },
            new QueryableTableNode("items", "Items", Columns, RowCount),
        ]);
        }

        public QueryableCollectionPage Query(string collectionId, QueryableCollectionQuery query)
        {
            QueryCount++;
            OnQuery?.Invoke();
            var rows = Enumerable.Range(query.Offset, Math.Min(query.Limit, RowCount - query.Offset))
                .Select(i => new TableRow(i.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    new Dictionary<string, ViewNode> { ["name"] = new TextNode("item") })).ToArray();
            return new(collectionId, Columns, rows, RowCount, RowCount, query.Offset, query.Limit);
        }

        public ActionPlan PlanAction(string actionId, IReadOnlyDictionary<string, FieldValue> parameters, ActionScope scope)
        {
            OnPlan?.Invoke();
            return new ActionPlan { Changes = [new PlannedChange("value", "Value", FieldValue.Of(Value), FieldValue.Of(10L))] };
        }

        public EditOutcome ApplyActionPlan(string actionId, IReadOnlyDictionary<string, FieldValue> parameters,
            ActionScope scope, ActionPlan plan)
        {
            ApplyCount++;
            OnApply?.Invoke();
            return ApplyEdits([new ValueEdit("value", FieldValue.Of(10L))]);
        }

        public override IReadOnlyList<FileOutput> Serialize() => [];
    }
}
