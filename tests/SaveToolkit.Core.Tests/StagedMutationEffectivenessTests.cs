using SaveToolkit.Core.Sessions.Groups;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Core.Tests;

public sealed class StagedMutationEffectivenessTests
{
    [Fact]
    public void CleanupFailureAfterPublicationDoesNotRevertACommittedCandidate()
    {
        var opens = 0;
        using var staged = new StagedGroupDocument(() => new StageProbe { ThrowDisposal = opens++ == 0 },
            d => d.Serialize(), () => { }, 10);
        Assert.True(staged.ApplyEdits([new("eager", FieldValue.Of(2L))]).Succeeded);
        Assert.Equal(new byte[] { 2 }, Assert.Single(staged.Serialize()).Content.ToArray());
    }

    [Fact]
    public void RestoringAnEarlierModelDoesNotExpandLazySectionsFromALaterModel()
    {
        using var staged = new StagedGroupDocument(() => new StageProbe(), d => d.Serialize(), () => { }, 10);
        Assert.True(staged.ApplyEdits([new("eager", FieldValue.Of(2L))]).Succeeded);
        Assert.NotEmpty(staged.ExpandNode("future"));
        staged.RestoreMemento(new StageToken(1, 1));
        Assert.Equal(new byte[] { 1 }, Assert.Single(staged.Serialize()).Content.ToArray());
    }

    [Fact]
    public void PublishedActionsReturnTheirGroupUndoTokenAndChanges()
    {
        using var staged = new StagedGroupDocument(() => new StageProbe(), d => d.Serialize(), () => { }, 10);
        var outcome = staged.InvokeAction("increment", new Dictionary<string, FieldValue>());
        Assert.NotNull(outcome.Revert);
        Assert.True(outcome.StructureChanged);
        Assert.Single(outcome.Changes);
        Assert.Equal(new byte[] { 2 }, Assert.Single(staged.Serialize()).Content.ToArray());
    }

    [Fact]
    public void AnOversizedBeforeTokenCannotBePublishedEvenWhenTheCandidateShrinks()
    {
        using var staged = new StagedGroupDocument(() => new StageProbe(11) { SizeFollowsValue = true },
            d => d.Serialize(), () => { }, 10);
        Assert.Throws<SaveFormatException>(() => staged.ApplyEdits([new("eager", FieldValue.Of(2L))]));
        Assert.Equal(new byte[] { 11 }, Assert.Single(staged.Serialize()).Content.ToArray());
    }

    [Fact]
    public void AnInvalidCandidateViewIsRejectedBeforePublication()
    {
        var opened = new List<StageProbe>();
        StageProbe Open() { var document = new StageProbe { RejectChangedView = true }; opened.Add(document); return document; }
        using var staged = new StagedGroupDocument(Open, d => d.Serialize(), () => { }, 10);
        Assert.Throws<InvalidOperationException>(() => staged.ApplyEdits([new("eager", FieldValue.Of(2L))]));
        Assert.Equal(new byte[] { 1 }, Assert.Single(staged.Serialize()).Content.ToArray());
        Assert.Equal(1, opened[1].Disposals);
    }
    [Fact]
    public void FailedInitialVerificationDisposesTheUnpublishedDocument()
    {
        var document = new StageProbe();
        Assert.Throws<InvalidOperationException>(() => new StagedGroupDocument(() => document,
            _ => throw new InvalidOperationException("Invalid outputs."), () => { }, 10));
        Assert.Equal(1, document.Disposals);
    }

    [Fact]
    public void CandidateCreationAndPublicationRebindQueriesAndReleaseThePreviousModel()
    {
        var opened = new List<StageProbe>();
        StageProbe Open() { var document = new StageProbe(); opened.Add(document); return document; }
        var preflights = 0;
        using var staged = new StagedGroupDocument(Open, d => d.Serialize(), () => preflights++, 10);
        staged.Query("items", new());
        var outcome = staged.ApplyEdits([new("queried", FieldValue.Of(2L))]);
        Assert.True(outcome.Succeeded);
        Assert.True(outcome.StructureChanged);
        Assert.Equal(1, preflights);
        Assert.Equal(1, opened[0].Disposals);
        Assert.Equal(0, opened[1].Disposals);
        Assert.Equal(1, opened[1].Queries);
        Assert.Equal(new byte[] { 2 }, Assert.Single(staged.Serialize()).Content.ToArray());

        // Publication retires remembered pages: the next independent candidate must not query them.
        Assert.True(staged.ApplyEdits([new("eager", FieldValue.Of(3L))]).Succeeded);
        Assert.Equal(0, opened[2].Queries);
        Assert.Equal(1, opened[1].Disposals);
    }

    [Fact]
    public void NoOpActionsDiscardTheirCandidateWithoutPublishingOrInventingHistory()
    {
        var opened = new List<StageProbe>();
        StageProbe Open() { var document = new StageProbe(); opened.Add(document); return document; }
        using var staged = new StagedGroupDocument(Open, d => d.Serialize(), () => { }, 10);
        var outcome = staged.InvokeAction("noop", new Dictionary<string, FieldValue>());
        Assert.Null(outcome.Revert);
        Assert.False(outcome.StructureChanged);
        Assert.Equal(0, opened[0].Disposals);
        Assert.Equal(1, opened[1].Disposals);
    }

    [Fact]
    public void FailedRestoresDisposeTheCandidateAndPreservePublishedOutputs()
    {
        var opened = new List<StageProbe>();
        StageProbe Open() { var document = new StageProbe { RejectRestore = opened.Count > 0 }; opened.Add(document); return document; }
        using var staged = new StagedGroupDocument(Open, d => d.Serialize(), () => { }, 10);
        Assert.Throws<InvalidOperationException>(() => staged.RestoreMemento(DocumentMemento.Snapshot(2, 1)));
        Assert.Equal(1, opened[1].Disposals);
        Assert.Equal(new byte[] { 1 }, Assert.Single(staged.Serialize()).Content.ToArray());
    }

    [Fact]
    public void FailedOutputVerificationAfterRestoreDisposesTheCandidate()
    {
        var opened = new List<StageProbe>();
        StageProbe Open() { var document = new StageProbe(); opened.Add(document); return document; }
        var verifies = 0;
        using var staged = new StagedGroupDocument(Open, d => ++verifies == 1 ? d.Serialize()
            : throw new InvalidOperationException("Invalid restored output."), () => { }, 10);
        Assert.Throws<InvalidOperationException>(() => staged.RestoreMemento(DocumentMemento.Snapshot(2, 1)));
        Assert.Equal(1, opened[1].Disposals);
        Assert.Equal(0, opened[0].Disposals);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    public void SnapshotSizesAreNonnegativeAndZeroIsAccepted(long size)
    {
        using var staged = new StagedGroupDocument(() => new StageProbe { SnapshotSize = size },
            d => d.Serialize(), () => { }, 10);
        if (size < 0) Assert.Throws<SaveFormatException>(() => staged.ApplyEdits([new("eager", FieldValue.Of(2L))]));
        else Assert.True(staged.ApplyEdits([new("eager", FieldValue.Of(2L))]).Succeeded);
    }

    private sealed class StageProbe(int initial = 1) : BoundSaveDocument, IGroupedSaveDocument, IQueryableDocument
    {
        private readonly int _initial = initial;
        private int _value = initial;
        public bool SizeFollowsValue { get; init; }
        public bool RejectChangedView { get; init; }
        public long SnapshotSize { get; init; } = 1;
        public bool RejectRestore { get; init; }
        public bool ThrowDisposal { get; init; }
        public int Disposals { get; private set; }
        public int Queries { get; private set; }
        public override string Title => "Staged model";
        public IReadOnlyList<QueryableCollection> Collections => [new("items", "Items", [], 1)];
        public override ViewNode BuildView() => RejectChangedView && _value != _initial
            ? throw new InvalidOperationException("Invalid candidate view.")
            : new SectionNode(Title,
                [Field("eager", "Eager", IntegerSpec.UInt8, () => FieldValue.Of(_value), value => _value = value.AsInt32()),
                    .. (_value >= 2 ? new ViewNode[] { SectionNode.Lazy("Future", "future") } : [])]);
        public QueryableCollectionPage Query(string collectionId, QueryableCollectionQuery query)
        {
            Queries++;
            return new(collectionId, [], [new TableRow("one", new Dictionary<string, ViewNode>
            { ["value"] = Field("queried", "Queried", IntegerSpec.UInt8, () => FieldValue.Of(_value), value => _value = value.AsInt32()) })],
                1, 1, query.Offset, query.Limit);
        }
        public override IReadOnlyList<ViewNode> ExpandNode(string lazyId) => _value < 2
            ? throw new InvalidOperationException("This section does not exist in the earlier model.")
            : [new TextNode("Future section")];
        public DocumentMemento CaptureMemento() => new StageToken(_value, SizeFollowsValue ? _value : SnapshotSize);
        protected override DocumentMemento CaptureSnapshot() => CaptureMemento();
        protected override void LoadSnapshot(DocumentMemento memento)
        {
            if (RejectRestore) throw new InvalidOperationException("Cannot restore.");
            _value = memento is StageToken token ? token.Value : ((SnapshotMemento<int>)memento).State;
        }
        public override ActionOutcome InvokeAction(string actionId, IReadOnlyDictionary<string, FieldValue> parameters)
        {
            if (actionId != "increment") return new();
            var before = _value++;
            return new() { Changes = [new("eager", FieldValue.Of(before), FieldValue.Of(_value))] };
        }
        public override IReadOnlyList<FileOutput> Serialize() => [new("data.bin", new byte[] { (byte)_value })];
        protected override void Dispose(bool disposing)
        {
            Disposals++;
            base.Dispose(disposing);
            if (ThrowDisposal) throw new InvalidOperationException("Cleanup failed after publication.");
        }
    }

    private sealed class StageToken(int value, long size) : DocumentMemento
    {
        public int Value { get; } = value;
        public override long SizeBytes => size;
    }
}
