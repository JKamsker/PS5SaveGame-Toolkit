using System.Text.Json;
using SaveToolkit.Core.Plugins;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Core.Storage;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Core.Tests;

public sealed class EditCancellationTests : IDisposable
{
    private const string FirstId = "first";
    private const string SecondId = "second";
    private const string ActionId = "change-both";
    private const string LazyId = "baseline-details";
    private const long OriginalFirst = 1;
    private const long OriginalSecond = 2;
    private const long ChangedFirst = 10;
    private const long ChangedSecond = 20;
    private static readonly IReadOnlyDictionary<string, JsonElement> NoParameters = new Dictionary<string, JsonElement>();
    private readonly WorkspaceStore _store = new(new PluginCatalog([]), new() { DataRoot = TestZips.TempDir() });

    public void Dispose() => _store.Dispose();

    private DocumentSession Open(EditDocument current, Func<ISaveDocument>? openOriginal = null)
    {
        var workspace = _store.Create("cancel edits", [new ImportSource("save.dat", () => new MemoryStream([0]))]);
        var session = new DocumentSession("edits", workspace.Set.AllDiskFiles.Single(), new Fakes.CounterPlugin(), current,
            openOriginal: openOriginal ?? (() => new EditDocument()));
        session.BuildView();
        return session;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PrecancelledMutationDoesNotOpenBaselineOrChangeValuesHistoryOrRevision(bool invokeAction)
    {
        using var cancellation = new CancellationTokenSource();
        var current = new EditDocument();
        var baselineOpens = 0;
        using var session = Open(current, () => { baselineOpens++; return new EditDocument(); });
        var before = session.State;
        cancellation.Cancel();

        Assert.Throws<OperationCanceledException>(() => Mutate(session, invokeAction, before.Revision, cancellation.Token));

        Assert.Equal(before, session.State);
        AssertOriginalValues(current);
        Assert.Equal(0, baselineOpens);
        Assert.Equal(0, current.ApplyCalls);
        Assert.Equal(0, current.ActionCalls);
        Assert.Equal(0, current.SetterCalls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CancelledBaselineLazyTraversalDoesNotMutateAndCanRetryWithACompleteBaseline(bool invokeAction)
    {
        using var cancellation = new CancellationTokenSource();
        var current = new EditDocument();
        var originals = new List<EditDocument>();
        using var session = Open(current, () =>
        {
            var original = new EditDocument { OnExpand = originals.Count == 0 ? cancellation.Cancel : null };
            originals.Add(original);
            return original;
        });
        var before = session.State;

        Assert.Throws<OperationCanceledException>(() => Mutate(session, invokeAction, before.Revision, cancellation.Token));

        Assert.Equal(before, session.State);
        AssertOriginalValues(current);
        Assert.Equal(0, current.ApplyCalls);
        Assert.Equal(0, current.ActionCalls);
        Assert.Equal(0, current.SetterCalls);
        Assert.Equal(0, current.ExpandCalls);
        var cancelledOriginal = Assert.Single(originals);
        Assert.Equal(1, cancelledOriginal.ExpandCalls);
        Assert.True(cancelledOriginal.Disposed);

        Assert.True(Mutate(session, invokeAction, before.Revision, CancellationToken.None).Ok);
        Assert.Equal(2, originals.Count);
        var changes = session.GetChanges();
        Assert.True(changes.Complete);
        Assert.Equal(2, changes.Changes.Count);
        Assert.Equal(OriginalFirst, changes.Changes.Single(change => change.ValueId == FirstId).OriginalValue.AsInt64());
        Assert.Equal(OriginalSecond, changes.Changes.Single(change => change.ValueId == SecondId).OriginalValue.AsInt64());
        Assert.True(session.Undo().Ok);
        AssertOriginalValues(current);
        Assert.False(session.State.CanUndo);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CancellationDuringAtomicMutationCompletesBothValuesAndPublishesExactlyOneUndoEntry(bool invokeAction)
    {
        using var cancellation = new CancellationTokenSource();
        var current = new EditDocument
        {
            OnFirstSet = invokeAction ? null : cancellation.Cancel,
            OnAction = invokeAction ? cancellation.Cancel : null,
        };
        using var session = Open(current);
        var before = session.State;

        var result = Mutate(session, invokeAction, before.Revision, cancellation.Token);

        Assert.True(cancellation.IsCancellationRequested);
        Assert.True(result.Ok);
        Assert.Equal(ChangedFirst, current.First);
        Assert.Equal(ChangedSecond, current.Second);
        Assert.Equal(2, result.Changes.Count);
        Assert.Equal(invokeAction ? 0 : 1, current.ApplyCalls);
        Assert.Equal(invokeAction ? 1 : 0, current.ActionCalls);
        Assert.Equal(invokeAction ? 0 : 2, current.SetterCalls);
        Assert.True(result.State.IsDirty);
        Assert.True(result.State.CanUndo);
        Assert.False(result.State.CanRedo);
        Assert.Equal(before.Revision + 1, result.State.Revision);
        Assert.Equal(result.State, session.State);

        var undo = session.Undo();

        Assert.True(undo.Ok);
        AssertOriginalValues(current);
        Assert.False(undo.State.IsDirty);
        Assert.False(undo.State.CanUndo);
        Assert.True(undo.State.CanRedo);
        Assert.Equal(before.Revision + 2, undo.State.Revision);
    }

    private static SessionEditResult Mutate(DocumentSession session, bool invokeAction, long revision, CancellationToken cancellationToken)
        => invokeAction
            ? session.InvokeAction(ActionId, NoParameters, revision, cancellationToken)
            : session.ApplyEdits(
                [new(FirstId, JsonSerializer.SerializeToElement(ChangedFirst)), new(SecondId, JsonSerializer.SerializeToElement(ChangedSecond))],
                revision, cancellationToken);

    private static void AssertOriginalValues(EditDocument document)
    {
        Assert.Equal(OriginalFirst, document.First);
        Assert.Equal(OriginalSecond, document.Second);
    }

    private sealed class EditDocument : BoundSaveDocument
    {
        public long First { get; private set; } = OriginalFirst;
        public long Second { get; private set; } = OriginalSecond;
        public int ApplyCalls { get; private set; }
        public int ActionCalls { get; private set; }
        public int SetterCalls { get; private set; }
        public int ExpandCalls { get; private set; }
        public bool Disposed { get; private set; }
        public Action? OnFirstSet { get; init; }
        public Action? OnAction { get; init; }
        public Action? OnExpand { get; init; }
        public override string Title => "Atomic edit cancellation fixture";

        public override ViewNode BuildView() => new SectionNode(Title,
        [
            Field(FirstId, "First", IntegerSpec.Int32, () => FieldValue.Of(First), value =>
            {
                First = value.AsInt64();
                SetterCalls++;
                OnFirstSet?.Invoke();
            }),
            Field(SecondId, "Second", IntegerSpec.Int32, () => FieldValue.Of(Second), value =>
            {
                Second = value.AsInt64();
                SetterCalls++;
            }),
            new ActionNode(ActionId, "Change both values"),
            SectionNode.Lazy("Baseline details", LazyId),
        ]);

        public override IReadOnlyList<ViewNode> ExpandNode(string lazyId)
        {
            Assert.Equal(LazyId, lazyId);
            ExpandCalls++;
            OnExpand?.Invoke();
            return [new TextNode("Original baseline details")];
        }

        public override EditOutcome ApplyEdits(IReadOnlyList<ValueEdit> edits)
        {
            ApplyCalls++;
            return base.ApplyEdits(edits);
        }

        public override ActionOutcome InvokeAction(string actionId, IReadOnlyDictionary<string, FieldValue> parameters)
        {
            Assert.Equal(ActionId, actionId);
            ActionCalls++;
            var oldFirst = First;
            var oldSecond = Second;
            First = ChangedFirst;
            OnAction?.Invoke();
            Second = ChangedSecond;
            return new ActionOutcome
            {
                Changes = [new(FirstId, FieldValue.Of(oldFirst), FieldValue.Of(First)), new(SecondId, FieldValue.Of(oldSecond), FieldValue.Of(Second))],
            };
        }

        public override IReadOnlyList<FileOutput> Serialize() => [];

        protected override void Dispose(bool disposing)
        {
            Disposed = true;
            base.Dispose(disposing);
        }
    }
}
