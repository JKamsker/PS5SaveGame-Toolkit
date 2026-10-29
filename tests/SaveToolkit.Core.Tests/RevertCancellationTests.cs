using System.Text.Json;
using SaveToolkit.Core.Plugins;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Core.Storage;
using SaveToolkit.Core.Tests.Fakes;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Core.Tests;

public sealed class RevertCancellationTests : IDisposable
{
    private static readonly Dictionary<string, JsonElement> NoParameters = [];
    private readonly WorkspaceStore _store = new(new PluginCatalog([]),
        new WorkspaceStoreOptions { DataRoot = TestZips.TempDir() });

    public void Dispose() => _store.Dispose();

    private DocumentSession Open(RevertDocument current, Func<ISaveDocument>? original = null)
    {
        var workspace = _store.Create("revert cancellation", [new ImportSource("save.dat", () => new MemoryStream([0]))]);
        var session = new DocumentSession("revert", workspace.Set.AllDiskFiles.Single(), new CounterPlugin(), current,
            openOriginal: original ?? (() => new RevertDocument(2)));
        session.BuildView();
        return session;
    }

    [Fact]
    public void CancelledRevertAfterReviewRestoresLearnedBindingsExpansionsAndBaseline()
    {
        using var cancellation = new CancellationTokenSource();
        var current = new RevertDocument(9) { OnMetadataAfterReview = cancellation.Cancel };
        var opens = 0;
        using var session = Open(current, () => { opens++; return new RevertDocument(2); });
        var before = session.State;

        Assert.ThrowsAny<OperationCanceledException>(() => session.RevertChange("value", before.Revision, cancellation.Token));

        Assert.True(current.ReviewCompared);
        Assert.Equal(before, session.State);
        Assert.Equal(9, current.Value);
        Assert.Equal(0, current.ApplyCount);
        Assert.Equal(1, opens);
        Assert.Equal(1, current.Expansions);
        current.OnMetadataAfterReview = null;
        var edit = new IncomingEdit("learned", JsonSerializer.SerializeToElement(7));
        Assert.False(session.ApplyEdits([edit]).Ok); // The plugin bound it, but the cancelled review must not publish it.
        Assert.Equal(before, session.State);
        Assert.True(session.InvokeAction("structure", NoParameters).Ok);
        Assert.Equal(2, opens); // The aborted revert must not retain its original baseline.
        Assert.Equal(1, current.Expansions); // Rebuild must not re-expand a section learned only by the aborted review.
        Assert.Equal("2", Assert.Single(session.GetChanges().Changes).OriginalValue.ToString());
    }

    [Fact]
    public void CancellationInsideRevertApplyCompletesValueAndUndoHistory()
    {
        using var cancellation = new CancellationTokenSource();
        var current = new RevertDocument(9) { OnApply = cancellation.Cancel };
        using var session = Open(current);
        var before = session.State;

        var result = session.RevertChange("value", before.Revision, cancellation.Token);

        Assert.True(cancellation.IsCancellationRequested);
        Assert.True(result.Ok);
        Assert.Equal(2, current.Value);
        Assert.Equal(1, current.ApplyCount);
        Assert.Equal(before.Revision + 1, result.State.Revision);
        Assert.True(result.State.CanUndo);
        Assert.True(session.Undo().Ok);
        Assert.Equal(9, current.Value);
        Assert.False(session.State.IsDirty);
    }

    [Fact]
    public void PlanCallbackCancellationFollowedByOrdinaryFailureRetainsPreviousPreview()
    {
        using var cancellation = new CancellationTokenSource();
        var current = new RevertDocument(2);
        using var session = Open(current);
        var previous = session.PlanAction("fill", NoParameters);
        Assert.NotNull(previous.PlanId);
        var before = session.State;
        current.OnPlan = () =>
        {
            cancellation.Cancel();
            throw new InvalidOperationException("Fixture planning failure after cancellation.");
        };

        Assert.ThrowsAny<OperationCanceledException>(() => session.PlanAction("fill", NoParameters, null, null, cancellation.Token));

        Assert.Equal(before, session.State);
        Assert.Equal(2, current.Value);
        Assert.Equal(0, current.ApplyCount);
        current.OnPlan = null;
        Assert.True(session.ApplyActionPlan(previous.PlanId!, previous.Revision).Ok);
        Assert.Equal(10, current.Value);
    }

    private sealed class RevertDocument : BoundSaveDocument, IFieldMetadataDocument, IActionPlanningDocument
    {
        private long _learned = 5;
        public RevertDocument(long value)
        {
            Value = value;
            RegisterField(new FieldMetadata("value", "Value", IntegerSpec.Int32),
                () => FieldValue.Of(Value), v => Value = v.AsInt64());
        }

        public long Value { get; private set; }
        public int ApplyCount { get; private set; }
        public int Expansions { get; private set; }
        public bool ReviewCompared { get; private set; }
        public Action? OnMetadataAfterReview { get; set; }
        public Action? OnApply { get; init; }
        public Action? OnPlan { get; set; }
        public override string Title => "Revert fixture";
        public override bool CanEdit { get { ReviewCompared = true; return true; } }

        public override ViewNode BuildView() => new SectionNode("Root",
        [
            SectionNode.Lazy("Details", "details"),
            new ActionNode("fill", "Fill") { SupportsPlanning = true },
            new ActionNode("structure", "Rebuild structure"),
        ]);

        public override IReadOnlyList<ViewNode> ExpandNode(string lazyId)
        {
            Expansions++;
            return
            [
                Field("value", "Value"),
                Field("learned", "Learned", IntegerSpec.Int32, () => FieldValue.Of(_learned), v => _learned = v.AsInt64()),
            ];
        }

        public override IReadOnlyList<FieldMetadata> ResolveFieldMetadata(IReadOnlyList<string> valueIds)
        {
            if (ReviewCompared) OnMetadataAfterReview?.Invoke();
            return base.ResolveFieldMetadata(valueIds);
        }

        public override EditOutcome ApplyEdits(IReadOnlyList<ValueEdit> edits)
        {
            ApplyCount++;
            OnApply?.Invoke();
            return base.ApplyEdits(edits);
        }

        public override ActionOutcome InvokeAction(string actionId, IReadOnlyDictionary<string, FieldValue> parameters) =>
            new() { StructureChanged = true };

        public ActionPlan PlanAction(string actionId, IReadOnlyDictionary<string, FieldValue> parameters, ActionScope scope)
        {
            OnPlan?.Invoke();
            return new ActionPlan { Changes = [new PlannedChange("value", "Value", FieldValue.Of(Value), FieldValue.Of(10L))] };
        }

        public EditOutcome ApplyActionPlan(string actionId, IReadOnlyDictionary<string, FieldValue> parameters,
            ActionScope scope, ActionPlan plan) => ApplyEdits([new ValueEdit("value", FieldValue.Of(10L))]);

        public override IReadOnlyList<FileOutput> Serialize() => [];
    }
}
