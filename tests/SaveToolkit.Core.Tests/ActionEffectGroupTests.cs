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
        Assert.Equal(["member-a/effect", "member-b/effect"], plan.Changes.Select(change => change.ValueId));

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

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void EffectDiagnosticsAndInnerProviderTokenAreAdmittedBeforeGroupVerifyOrPublish(int mode)
    {
        var control = new GroupControl { OutcomeMode = mode };
        using var staged = Open(control);
        var plan = staged.PlanAction("transfer", new Dictionary<string, FieldValue>(), new());
        control.VerifyCalls = 0;

        Assert.ThrowsAny<Exception>(() => staged.ApplyActionPlan("transfer", new Dictionary<string, FieldValue>(), new(), plan));

        Assert.Equal(0, control.VerifyCalls);
        Assert.Equal([1, 2], Values(staged));
        Assert.Equal(FieldValue.Of(1), staged.GetValue("member-a/value"));
        Assert.True(control.DisposedCandidates > 0);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    [InlineData(5)]
    public void FullSessionGroupMalformedDiagnosticsKeepBothPublishedMembersAndHistory(int mode)
    {
        using var fixture = new ActionEffectR2Fixture();
        var control = new GroupControl();
        using var staged = Open(control);
        using var session = new SaveToolkit.Core.Sessions.DocumentSession("group-effect-r2", fixture.Session.File,
            fixture.Session.Plugin, staged, openOriginal: () => new GroupEffectDocument(control, 1, 2));
        _ = session.BuildView();
        var preview = session.PlanAction("transfer", new Dictionary<string, System.Text.Json.JsonElement>());
        Assert.NotNull(preview.PlanId);
        control.OutcomeMode = mode;

        var result = session.ApplyActionPlan(preview.PlanId!, preview.Revision);

        Assert.False(result.Ok);
        Assert.Equal([1, 2], Values(staged));
        Assert.False(session.State.CanUndo);
        Assert.True(session.CanEdit);
        Assert.Equal(FieldValue.Of(2), staged.GetValue("member-b/value"));
    }

    [Fact]
    public void BrokenRestoreCandidateKeepsPublishedGroupBytesHistoryAndBindings()
    {
        var control = new GroupControl();
        using var staged = Open(control);
        Assert.True(staged.ApplyEdits([new("member-a/value", FieldValue.Of(4))]).Succeeded);
        var published = staged.CaptureMemento();
        var verifyCalls = control.VerifyCalls;
        control.BrokenRestore = true;

        var error = Assert.Throws<InvalidOperationException>(() => staged.RestoreMemento(published));

        Assert.Contains("restore", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(verifyCalls, control.VerifyCalls);
        Assert.Equal([4, 2], Values(staged));
        Assert.Equal(FieldValue.Of(4), staged.GetValue("member-a/value"));
        Assert.Equal(FieldValue.Of(2), staged.GetValue("member-b/value"));
        Assert.True(control.DisposedCandidates > 0);
        Assert.Equal(2, control.RestoreFailures);
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
        public bool BrokenRestore { get; set; }
        public int RestoreFailures { get; set; }
        public int OutcomeMode { get; set; }
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
            RegisterField("member-a/value", IntegerSpec.Int32, () => FieldValue.Of(_first), value => _first = value.AsInt32());
            RegisterField("member-b/value", IntegerSpec.Int32, () => FieldValue.Of(_second), value => _second = value.AsInt32());
        }

        public override string Title => "Effect group";
        public override ViewNode BuildView()
        {
            RegisterField("member-a/value", IntegerSpec.Int32, () => FieldValue.Of(_first), value => _first = value.AsInt32());
            RegisterField("member-b/value", IntegerSpec.Int32, () => FieldValue.Of(_second), value => _second = value.AsInt32());
            return new SectionNode(Title, [new ActionNode("transfer", "Transfer") { SupportsPlanning = true }]);
        }
        public override IReadOnlyList<FileOutput> Serialize() =>
            [new("member-a", new byte[] { (byte)_first }), new("member-b", new byte[] { (byte)_second })];
        public override IReadOnlyList<FieldMetadata> ResolveFieldMetadata(IReadOnlyList<string> valueIds)
        {
            // Restore clears bindings. Candidate metadata must bind the current model before
            // the host reads it, without relying on the later view-construction phase.
            RegisterField("member-a/value", IntegerSpec.Int32, () => FieldValue.Of(_first), value => _first = value.AsInt32());
            RegisterField("member-b/value", IntegerSpec.Int32, () => FieldValue.Of(_second), value => _second = value.AsInt32());
            return valueIds.Where(id => id is "member-a/value" or "member-b/value")
                .Select(id => new FieldMetadata(id, id, IntegerSpec.Int32)
            {
                BindingKind = FieldBindingKind.Primary, CanonicalValueId = id,
            }).ToArray();
        }
        public DocumentMemento CaptureMemento() => CaptureSnapshot();

        public ActionPlan PlanAction(string actionId, IReadOnlyDictionary<string, FieldValue> parameters, ActionScope scope) => new()
        {
            Changes =
            [
                EffectChange("member-a/effect", _first),
                EffectChange("member-b/effect", _second),
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
                Revert = _control.OutcomeMode switch { 4 => new InvalidSizeToken(-1), 5 => new InvalidSizeToken(1025), _ => before },
                Warnings = _control.OutcomeMode switch
                {
                    1 => null!, 2 => [new string('x', 4097)],
                    3 => new MiscountedList<string>(0, ["unreported"]), _ => [],
                },
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
            if (_control.BrokenRestore)
            {
                _control.RestoreFailures++;
                throw new InvalidOperationException("provider restore failed with primary and restore diagnostics");
            }
            var state = ((SnapshotMemento<GroupState>)snapshot).State;
            _first = state.First;
            _second = state.Second;
        }

        private static PlannedChange EffectChange(string id, int oldValue) => new(id, id, FieldValue.Of(oldValue), FieldValue.Of(oldValue + 1))
        {
            Effect = new(ActionEffectKind.StructuralMemento, new IntegerSpec(0, 10) { ReadOnly = true }),
        };

        private sealed record GroupState(int First, int Second);
        private sealed class InvalidSizeToken(long bytes) : DocumentMemento
        {
            public override long SizeBytes => bytes;
        }
    }
}
