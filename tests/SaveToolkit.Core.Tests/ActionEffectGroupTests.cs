using SaveToolkit.Core.Sessions.Groups;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Saves;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Core.Tests;

public sealed class ActionEffectGroupTests
{
    [Fact]
    public void CandidateEffectOutcomeIsValidatedBeforeVerifyAndPublication()
    {
        var control = new GroupControl { Malformed = true };
        using var staged = Open(control);
        control.VerifyCalls = 0;
        var plan = staged.PlanAction("transfer", new Dictionary<string, FieldValue>(), new());
        Assert.Equal(["member-a/value", "member-b/value"], plan.Changes.Select(change => change.ValueId));

        Assert.Throws<InvalidOperationException>(() => staged.ApplyActionPlan("transfer",
            new Dictionary<string, FieldValue>(), new(), plan));
        Assert.Equal(0, control.VerifyCalls);
        Assert.Equal([1, 2], Values(staged));
        Assert.True(control.DisposedCandidates > 0);

        control.Malformed = false;
        var outcome = staged.ApplyActionPlan("transfer", new Dictionary<string, FieldValue>(), new(), plan);
        Assert.True(outcome.Succeeded);
        Assert.Equal(1, control.VerifyCalls);
        Assert.Equal([2, 3], Values(staged));
    }

    [Fact]
    public void GroupPlanningCancellationDoesNotPublishCandidateState()
    {
        var control = new GroupControl();
        using var staged = Open(control);
        control.VerifyCalls = 0;
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => staged.PlanForSession("transfer",
            new Dictionary<string, FieldValue>(), new(), cancellation.Token));
        Assert.Equal([1, 2], Values(staged));
        Assert.Equal(0, control.VerifyCalls);
    }

    private static StagedGroupDocument Open(GroupControl control) => new(
        () => new GroupEffectDocument(control, 1, 2),
        document =>
        {
            control.VerifyCalls++;
            return document.Serialize();
        },
        _ => { },
        maxSnapshotBytes: 1024);

    private static int[] Values(StagedGroupDocument staged) =>
        staged.Serialize().Select(file => file.Content.Span[0]).Select(value => (int)value).ToArray();

    private sealed class GroupControl
    {
        public bool Malformed { get; set; }
        public int VerifyCalls { get; set; }
        public int DisposedCandidates { get; set; }
    }

    private sealed class GroupEffectDocument : BoundSaveDocument, IGroupedSaveDocument,
        IActionPlanningDocument, IActionEffectDocument
    {
        private readonly GroupControl _control;
        private int _first;
        private int _second;

        public GroupEffectDocument(GroupControl control, int first, int second)
        {
            _control = control;
            _first = first;
            _second = second;
        }

        public override string Title => "Effect group";
        public override ViewNode BuildView() => new SectionNode(Title,
            [new ActionNode("transfer", "Transfer") { SupportsPlanning = true }]);
        public override IReadOnlyList<FileOutput> Serialize() =>
            [new("member-a", new byte[] { (byte)_first }), new("member-b", new byte[] { (byte)_second })];
        public override IReadOnlyList<FieldMetadata> ResolveFieldMetadata(IReadOnlyList<string> valueIds) => [];
        public DocumentMemento CaptureMemento() => CaptureSnapshot();

        public ActionPlan PlanAction(string actionId, IReadOnlyDictionary<string, FieldValue> parameters, ActionScope scope) => new()
        {
            Changes =
            [
                EffectChange("member-a/value", _first),
                EffectChange("member-b/value", _second),
            ],
        };

        public EditOutcome ApplyActionPlan(string actionId, IReadOnlyDictionary<string, FieldValue> parameters,
            ActionScope scope, ActionPlan plan)
        {
            var before = CaptureSnapshot();
            _first++;
            _second++;
            if (_control.Malformed)
                return new EditOutcome { Changes = [new("member-a/value", FieldValue.Of(99), FieldValue.Of(100))], StructureChanged = true, Revert = before };
            return new EditOutcome
            {
                Changes = plan.Changes.Select(change => new ValueChange(change.ValueId, change.OldValue, change.NewValue)).ToArray(),
                StructureChanged = true,
                Revert = before,
            };
        }

        protected override void Dispose(bool disposing)
        {
            _control.DisposedCandidates++;
            base.Dispose(disposing);
        }

        protected override DocumentMemento CaptureSnapshot() => DocumentMemento.Snapshot(
            new GroupState(_first, _second), 16);

        protected override void LoadSnapshot(DocumentMemento snapshot)
        {
            var state = ((SnapshotMemento<GroupState>)snapshot).State;
            _first = state.First;
            _second = state.Second;
        }

        private static PlannedChange EffectChange(string id, int oldValue) => new(id, id, FieldValue.Of(oldValue), FieldValue.Of(oldValue + 1))
        {
            Effect = new(ActionEffectKind.StructuralMemento, new IntegerSpec(0, 10) { ReadOnly = true }),
        };

        private sealed record GroupState(int First, int Second);
    }
}
