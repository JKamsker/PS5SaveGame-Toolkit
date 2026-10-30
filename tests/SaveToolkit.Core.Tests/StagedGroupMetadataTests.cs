using SaveToolkit.Core.Sessions.Groups;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Core.Tests;

public sealed class StagedGroupMetadataTests
{
    [Fact]
    public void HiddenMetadataIsReboundOnTheRestoredMutationCandidate()
    {
        using var staged = new StagedGroupDocument(() => new CandidateDocument(), _ => [], () => { }, 4096);
        Assert.Single(staged.ResolveFieldMetadata(["hidden"]));

        var result = staged.ApplyEdits([new ValueEdit("hidden", FieldValue.Of(2))]);

        Assert.True(result.Succeeded);
        Assert.Equal(2, staged.GetValue("hidden").AsInt32());
    }

    [Fact]
    public void RetiredMetadataDoesNotBlockLaterCandidateRestore()
    {
        var state = new CandidateState();
        using var staged = new StagedGroupDocument(() => new CandidateDocument(state), candidate => candidate.Serialize(),
            () => { }, 4096);
        Assert.Single(staged.ResolveFieldMetadata(["hidden"]));

        var retired = staged.InvokeAction("retire", new Dictionary<string, FieldValue>());
        Assert.True(retired.StructureChanged);

        var repeated = staged.InvokeAction("retire", new Dictionary<string, FieldValue>());
        Assert.True(repeated.StructureChanged);
        Assert.Equal(new byte[] { 1 }, staged.Serialize().Single().Content.ToArray());
    }

    [Fact]
    public void ForeignCandidateMetadataIsRejectedBeforeMutation()
    {
        var foreign = false;
        using var staged = new StagedGroupDocument(() => new CandidateDocument(new CandidateState(), () => foreign),
            _ => [], () => { }, 4096);
        Assert.Single(staged.ResolveFieldMetadata(["hidden"]));
        foreign = true;

        Assert.Throws<InvalidOperationException>(() => staged.ApplyEdits([new ValueEdit("hidden", FieldValue.Of(2))]));
        Assert.Equal(1, staged.GetValue("hidden").AsInt32());
    }

    [Fact]
    public void RestoreMetadataFailureDoesNotPublishOrLeakCandidate()
    {
        var state = new CandidateState();
        using var staged = new StagedGroupDocument(() => new CandidateDocument(state), _ => [new FileOutput("save.dat", new byte[] { 1 })], () => { }, 4096);
        Assert.Single(staged.Serialize());
        Assert.Single(staged.ResolveFieldMetadata(["hidden"]));
        var changed = staged.InvokeAction("touch", new Dictionary<string, FieldValue>());
        Assert.True(changed.StructureChanged);
        Assert.Single(staged.Serialize());
        state.ThrowOnSecondResolve = true;
        var disposedBefore = state.DisposedCandidates;

        Assert.Throws<InvalidOperationException>(() => staged.RestoreMemento(changed.Revert!));
        Assert.Equal(new byte[] { 1 }, staged.Serialize().Single().Content.ToArray());
        Assert.Equal(disposedBefore + 1, state.DisposedCandidates);
    }

    private sealed class CandidateState
    {
        public bool Hidden = true;
        public bool ThrowOnSecondResolve;
        public int DisposedCandidates;
    }

    private sealed class CandidateDocument : BoundSaveDocument, IGroupedSaveDocument, IFieldMetadataDocument
    {
        private readonly CandidateState _state;
        private readonly Func<bool> _foreign;
        private int _value = 1;
        private int _resolveCalls;

        public CandidateDocument() : this(new CandidateState()) { }

        public CandidateDocument(CandidateState state, Func<bool>? foreign = null)
        {
            _state = state;
            _foreign = foreign ?? (static () => false);
        }

        public override string Title => "Candidate";
        public override bool CanEdit => true;
        public override ViewNode BuildView() => new TextNode("No rendered metadata");
        public override IReadOnlyList<FileOutput> Serialize() => [new("save.dat", new byte[] { (byte)_value })];
        public DocumentMemento CaptureMemento() => CaptureSnapshot()!;

        public override ActionOutcome InvokeAction(string actionId, IReadOnlyDictionary<string, FieldValue> parameters)
        {
            if (actionId == "retire") _state.Hidden = false;
            else if (actionId != "touch") throw new KeyNotFoundException(actionId);
            return new ActionOutcome { StructureChanged = true };
        }

        public override IReadOnlyList<FieldMetadata> ResolveFieldMetadata(IReadOnlyList<string> valueIds)
        {
            if (++_resolveCalls == 2 && _state.ThrowOnSecondResolve)
                throw new InvalidOperationException("second candidate metadata resolution failed");
            if (_foreign())
                return [new FieldMetadata("foreign", "Foreign", IntegerSpec.Int32)];
            if (!_state.Hidden) return [];
            RegisterField(new FieldMetadata("hidden", "Hidden", IntegerSpec.Int32),
                () => FieldValue.Of(_value), value => _value = value.AsInt32());
            return base.ResolveFieldMetadata(valueIds);
        }

        protected override DocumentMemento CaptureSnapshot() => DocumentMemento.Snapshot((_value, _state.Hidden), 64);

        protected override void LoadSnapshot(DocumentMemento snapshot) =>
            (_value, _state.Hidden) = ((SnapshotMemento<(int, bool)>)snapshot).State;

        protected override void Dispose(bool disposing)
        {
            if (disposing) _state.DisposedCandidates++;
            base.Dispose(disposing);
        }
    }
}
