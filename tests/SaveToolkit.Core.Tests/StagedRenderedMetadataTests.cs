using SaveToolkit.Core.Sessions.Groups;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Core.Tests;

public sealed class StagedRenderedMetadataTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void PreviouslyExposedBindingMustBeProvedAgainOnTheRestoredCandidate(bool available)
    {
        var state = new ProbeState();
        using var staged = Open(state);
        _ = staged.BuildView();
        Assert.Single(staged.ResolveFieldMetadata(["rendered"]));
        state.Events.Clear();
        state.Expose = available;

        if (available)
        {
            var outcome = staged.ApplyEdits([new("rendered", FieldValue.Of(5))]);
            Assert.True(outcome.Succeeded);
            Assert.Equal(5, staged.GetValue("rendered").AsInt32());
            Assert.Equal(new byte[] { 5 }, Assert.Single(staged.Serialize()).Content.ToArray());
            Assert.Equal(["restore", "metadata", "view", "metadata", "apply"], state.Events.Take(5));
            Assert.Equal(1, state.Applies);
            Assert.Equal(2, state.Verifications);
            Assert.True(state.Documents[0].Disposed);
        }
        else
        {
            Assert.Throws<InvalidOperationException>(() => staged.ApplyEdits([new("rendered", FieldValue.Of(5))]));
            Assert.Equal(["restore", "metadata", "view", "metadata"], state.Events);
            Assert.Equal(0, state.Applies);
            Assert.Equal(1, state.Verifications);
            Assert.False(state.Documents[0].Disposed);
            Assert.True(state.Documents[1].Disposed);
            Assert.Equal(0, staged.GetValue("rendered").AsInt32());
            Assert.Equal(new byte[] { 0 }, Assert.Single(staged.Serialize()).Content.ToArray());
        }
    }

    [Fact]
    public void MissingUnexposedIdentityCannotAcquireAuthorityFromCandidateRendering()
    {
        var state = new ProbeState();
        using var staged = Open(state);
        _ = staged.BuildView();
        state.Events.Clear();

        Assert.Throws<InvalidOperationException>(() => staged.ApplyEdits([new("rendered", FieldValue.Of(5))]));

        Assert.Equal(["restore", "metadata"], state.Events);
        Assert.Equal(0, state.Applies);
        Assert.Equal(1, state.Verifications);
        Assert.True(state.Documents[1].Disposed);
        Assert.Equal(0, staged.GetValue("rendered").AsInt32());
    }

    [Fact]
    public void ForeignColdResponseRejectsBeforeCandidateViewOrSetter()
    {
        var state = new ProbeState();
        using var staged = Open(state);
        _ = staged.BuildView();
        Assert.Single(staged.ResolveFieldMetadata(["rendered"]));
        state.Events.Clear();
        state.Foreign = true;

        Assert.Throws<InvalidOperationException>(() => staged.ApplyEdits([new("rendered", FieldValue.Of(5))]));

        Assert.Equal(["restore", "metadata"], state.Events);
        Assert.Equal(0, state.Applies);
        Assert.Equal(1, state.Verifications);
        Assert.True(state.Documents[1].Disposed);
        Assert.False(state.Documents[0].Disposed);
        Assert.Equal(new byte[] { 0 }, Assert.Single(staged.Serialize()).Content.ToArray());
    }

    [Fact]
    public void CancellationDuringCandidateExposureKeepsLiveBytesAndRememberedBinding()
    {
        using var cancellation = new CancellationTokenSource();
        var state = new ProbeState();
        using var staged = Open(state);
        _ = staged.BuildView();
        Assert.Single(staged.ResolveFieldMetadata(["rendered"]));
        state.Events.Clear();
        state.CandidateView = cancellation.Cancel;

        var failure = Assert.Throws<OperationCanceledException>(() => staged.PlanForSession(
            "unused", new Dictionary<string, FieldValue>(), new(), cancellation.Token));

        Assert.Equal(cancellation.Token, failure.CancellationToken);
        Assert.Equal(["restore", "metadata", "view"], state.Events);
        Assert.True(state.Documents[1].Disposed);
        Assert.False(state.Documents[0].Disposed);
        Assert.Equal(0, state.Applies);
        Assert.Equal(1, state.Verifications);
        Assert.Equal(new byte[] { 0 }, Assert.Single(staged.Serialize()).Content.ToArray());
        state.CandidateView = null;
        Assert.True(staged.ApplyEdits([new("rendered", FieldValue.Of(5))]).Succeeded);
        Assert.Equal(5, staged.GetValue("rendered").AsInt32());
    }

    private static StagedGroupDocument Open(ProbeState state) => new(() => new RenderedDocument(state),
        candidate => { state.Verifications++; return candidate.Serialize(); }, () => { }, 4096);

    private sealed class ProbeState
    {
        public bool Expose = true;
        public bool Foreign;
        public Action? CandidateView;
        public int Applies;
        public int Verifications;
        public List<string> Events { get; } = [];
        public List<RenderedDocument> Documents { get; } = [];
    }

    private sealed class RenderedDocument : BoundSaveDocument, IGroupedSaveDocument, IFieldMetadataDocument
    {
        private readonly ProbeState _state;
        private int _value;

        public RenderedDocument(ProbeState state) { _state = state; state.Documents.Add(this); }
        public bool Disposed { get; private set; }
        public override string Title => "Rendered-only metadata";
        public DocumentMemento CaptureMemento() => DocumentMemento.Snapshot(_value, 1);
        public override ViewNode BuildView()
        {
            _state.Events.Add("view");
            if (_state.Documents.Count > 1) _state.CandidateView?.Invoke();
            if (!_state.Expose) return new TextNode("Unavailable");
            RegisterField(new FieldMetadata("rendered", "Rendered", IntegerSpec.Int32),
                () => FieldValue.Of(_value), value => _value = value.AsInt32());
            return Field("rendered", "Rendered");
        }
        public override IReadOnlyList<FieldMetadata> ResolveFieldMetadata(IReadOnlyList<string> ids)
        {
            _state.Events.Add("metadata");
            if (_state.Foreign) return [new("foreign", "Foreign", IntegerSpec.Int32)];
            return base.ResolveFieldMetadata(ids);
        }
        public override EditOutcome ApplyEdits(IReadOnlyList<ValueEdit> edits)
        {
            _state.Events.Add("apply");
            _state.Applies++;
            return base.ApplyEdits(edits);
        }
        protected override DocumentMemento CaptureSnapshot() => CaptureMemento();
        protected override void LoadSnapshot(DocumentMemento snapshot)
        {
            _state.Events.Add("restore");
            _value = ((SnapshotMemento<int>)snapshot).State;
        }
        public override IReadOnlyList<FileOutput> Serialize() => [new("save.dat", new byte[] { (byte)_value })];
        protected override void Dispose(bool disposing)
        {
            Disposed = true;
            base.Dispose(disposing);
        }
    }
}
