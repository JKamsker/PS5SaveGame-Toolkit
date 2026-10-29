using System.Text.Json;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Core.Tests.Fakes;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Core.Tests;

public sealed class SessionMutationEffectivenessTests
{
    private static readonly Dictionary<string, JsonElement> NoParameters = [];

    [Fact]
    public void StaleEmptyEditBatchesAreRejectedWithoutThrowing()
    {
        using var fixture = new MutationSessionFixture(new SessionProbe());
        fixture.Session.BuildView();
        var result = fixture.Session.ApplyEdits([], expectedRevision: 1);
        Assert.False(result.Ok);
        Assert.True(result.Stale);
        Assert.Equal("stale", result.Code);
        Assert.False(result.State.CanUndo);
        Assert.Equal(0, result.State.Revision);
    }

    [Fact]
    public void ApplyingAPlanCapturesHiddenOriginalValuesBeforeObservingItsOutcome()
    {
        var document = new SessionProbe
        {
            Plan = new() { Changes = [new("hidden", "Hidden", FieldValue.Of(1L), FieldValue.Of(2L))] },
            PlanOutcome = new() { Changes = [new("hidden", FieldValue.Of(1L), FieldValue.Of(2L))] },
        };
        using var fixture = new MutationSessionFixture(document, openOriginal: () => new SessionProbe());
        fixture.Session.BuildView();
        var preview = fixture.Session.PlanAction("plan", NoParameters);
        Assert.True(fixture.Session.ApplyActionPlan(preview.PlanId!, preview.Revision).Ok);
        var change = Assert.Single(fixture.Session.GetChanges().Changes);
        Assert.Equal("hidden", change.ValueId);
        Assert.Equal(FieldValue.Of(1L), change.OriginalValue);
        Assert.Equal(FieldValue.Of(2L), change.CurrentValue);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FailedViewRebuildsAdvanceOnlyStructuralRevisions(bool failedPlan)
    {
        var document = new SessionProbe { FailRebuild = true, ThrowApplyingPlan = failedPlan,
            Action = new() { StructureChanged = true } };
        using var fixture = new MutationSessionFixture(document);
        fixture.Session.BuildView();
        var preview = fixture.Session.PlanAction("plan", NoParameters);
        var result = failedPlan ? fixture.Session.ApplyActionPlan(preview.PlanId!, preview.Revision)
            : fixture.Session.InvokeAction("action", NoParameters);
        Assert.Equal(!failedPlan, result.Ok);
        Assert.Equal(failedPlan ? 0 : 1, result.State.Revision);
        Assert.NotEmpty(result.Warnings);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PlannedStructuralChangesAndMementosEachRebuildBindings(bool memento)
    {
        var document = new SessionProbe { PlanOutcome = new()
        {
            StructureChanged = !memento,
            Revert = memento ? DocumentMemento.Snapshot(1, 1) : null,
        } };
        using var fixture = new MutationSessionFixture(document);
        fixture.Session.BuildView();
        var preview = fixture.Session.PlanAction("plan", NoParameters);
        var builds = document.Builds;
        var result = fixture.Session.ApplyActionPlan(preview.PlanId!, preview.Revision);
        Assert.True(result.Ok);
        Assert.True(result.StructureChanged);
        Assert.True(document.Builds > builds);
    }

    [Fact]
    public void RevertingBeforeTheFirstReviewLoadsTheOriginal()
    {
        var opens = 0;
        using var fixture = new MutationSessionFixture(new SessionProbe(), openOriginal: () =>
        { opens++; return new SessionProbe(); });
        fixture.Session.BuildView();
        Assert.False(fixture.Session.RevertChange("first", 0).Ok);
        Assert.Equal(1, opens);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void NonpositiveActionRowBudgetsAreRejectedWhenOpeningASession(int maximum)
    {
        using var document = new SessionProbe();
        Assert.Throws<ArgumentOutOfRangeException>(() => new MutationSessionFixture(document,
            new DocumentSessionOptions { MaxQueryActionRows = maximum }));
    }


    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FailedActionsOnlyReportAStructuralChangeWhenBindingsChanged(bool replaceBindings)
    {
        var document = new SessionProbe { ThrowAction = true, ReplaceBindings = replaceBindings };
        using var fixture = new MutationSessionFixture(document);
        fixture.Session.BuildView();

        var result = fixture.Session.InvokeAction("action", NoParameters);

        Assert.False(result.Ok);
        Assert.Equal(replaceBindings, result.StructureChanged);
        Assert.Equal(replaceBindings ? 1 : 0, result.State.Revision);
        if (replaceBindings)
        {
            var stale = fixture.Session.ApplyEdits([new("first", JsonSerializer.SerializeToElement(2))]);
            Assert.True(stale.Stale);
            Assert.Equal("stale", stale.Code);
        }
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public void StructuralAndNonUndoableActionsBothRebuildTheView(bool structural, bool undoable)
    {
        var document = new SessionProbe { Action = new() { StructureChanged = structural, Undoable = undoable } };
        using var fixture = new MutationSessionFixture(document);
        fixture.Session.BuildView();
        var result = fixture.Session.InvokeAction("action", NoParameters);
        Assert.True(result.Ok);
        Assert.True(result.StructureChanged);
        Assert.Equal(1, result.State.Revision);
        Assert.Null(result.Code);
    }

    [Fact]
    public void PublishedReadOnlyParameterDefaultsAreUsableWithoutClientAssignments()
    {
        var document = new SessionProbe { ReadOnlyDefault = true };
        using var fixture = new MutationSessionFixture(document);
        fixture.Session.BuildView();
        Assert.True(fixture.Session.InvokeAction("action", NoParameters).Ok);
        Assert.Equal(FieldValue.Of(""), document.LastParameters!["payload"]);
    }

    [Fact]
    public void UnpublishedBlobIdsAreNeverPassedToThePluginAndDisposalIsIdempotent()
    {
        var document = new SessionProbe();
        var fixture = new MutationSessionFixture(document);
        fixture.Session.BuildView();
        Assert.Throws<KeyNotFoundException>(() => fixture.Session.ReadBlob("unknown", 0, 1));
        Assert.Equal(0, document.BlobReads);
        fixture.Dispose();
        fixture.Dispose();
        Assert.Equal(1, document.Disposals);
    }

    [Fact]
    public void PreviewStalenessUsesTheSameTypedCodeAsEditResults()
    {
        using var fixture = new MutationSessionFixture(new SessionProbe());
        fixture.Session.BuildView();
        var accepted = fixture.Session.PlanAction("plan", NoParameters);
        Assert.NotNull(accepted.PlanId);
        Assert.Null(accepted.Code);
        var stale = fixture.Session.PlanAction("plan", NoParameters, expectedRevision: 1);
        Assert.True(stale.Stale);
        Assert.Equal("stale", stale.Code);
        Assert.Null(stale.PlanId);
    }

    [Theory]
    [InlineData("empty")]
    [InlineData("too-many")]
    public void InvalidRowSelectionsAreRejectedBeforePlanning(string fault)
    {
        var document = new SessionProbe();
        using var fixture = new MutationSessionFixture(document);
        fixture.Session.BuildView();
        var rows = fault == "empty" ? new[] { "" } : Enumerable.Range(0, 5001)
            .Select(i => i.ToString(System.Globalization.CultureInfo.InvariantCulture)).ToArray();
        var preview = fixture.Session.PlanAction("plan", NoParameters, new(rows));
        Assert.Null(preview.PlanId);
        Assert.NotEmpty(preview.Plan.Errors);
        Assert.Equal(0, document.Plans);
    }

    [Fact]
    public void TheMaximumRowSelectionIsAccepted()
    {
        var document = new SessionProbe();
        using var fixture = new MutationSessionFixture(document);
        fixture.Session.BuildView();
        var rows = Enumerable.Range(0, 5000).Select(i => i.ToString(System.Globalization.CultureInfo.InvariantCulture)).ToArray();
        Assert.NotNull(fixture.Session.PlanAction("plan", NoParameters, new(rows)).PlanId);
        Assert.Equal(1, document.Plans);
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(1, false)]
    public void PreviewBudgetIncludesRequestBytesAndAcceptsItsExactLimit(int excess, bool accepted)
    {
        const int maxPreviewBytes = 4 * 1024 * 1024;
        var document = new SessionProbe();
        using var fixture = new MutationSessionFixture(document);
        fixture.Session.BuildView();
        var planBytes = JsonSerializer.SerializeToUtf8Bytes(document.Plan).Length;
        var scopeBytes = JsonSerializer.SerializeToUtf8Bytes(new ActionScope()).Length;
        var parameterBytes = JsonSerializer.SerializeToUtf8Bytes(new Dictionary<string, FieldValue> { ["payload"] = FieldValue.Of("") }).Length;
        var payload = new string('x', maxPreviewBytes - planBytes - scopeBytes - parameterBytes + excess);
        var preview = fixture.Session.PlanAction("plan", new Dictionary<string, JsonElement>
            { ["payload"] = JsonSerializer.SerializeToElement(payload) });
        Assert.Equal(accepted, preview.PlanId is not null);
        Assert.Equal(accepted, preview.Plan.Errors.Count == 0);
    }

    private sealed class SessionProbe : BoundSaveDocument, IActionPlanningDocument
    {
        private string _fieldId = "first";
        private int _hidden = 1;
        private bool _acted;
        public SessionProbe() => RegisterField("hidden", IntegerSpec.UInt8, () => FieldValue.Of(_hidden), value => _hidden = value.AsInt32());
        public bool FailRebuild { get; init; }
        public bool ThrowApplyingPlan { get; init; }
        public bool ThrowAction { get; init; }
        public bool ReplaceBindings { get; init; }
        public bool ReadOnlyDefault { get; init; }
        public ActionOutcome Action { get; init; } = new();
        public EditOutcome PlanOutcome { get; init; } = new();
        public ActionPlan Plan { get; init; } = new();
        public int Builds { get; private set; }
        public int Plans { get; private set; }
        public int Disposals { get; private set; }
        public int BlobReads { get; private set; }
        public IReadOnlyDictionary<string, FieldValue>? LastParameters { get; private set; }
        public override string Title => "Session probe";
        public override ViewNode BuildView()
        {
            Builds++;
            if (_acted && FailRebuild) throw new InvalidOperationException("Injected view failure.");
            FieldNode[] parameters = [new("payload", "Payload", new TextSpec { ReadOnly = ReadOnlyDefault }, FieldValue.Of(""))];
            return new SectionNode(Title, [Field(_fieldId, "Value", IntegerSpec.UInt8, () => FieldValue.Of(1L), _ => { }),
                new ActionNode("action", "Action") { Parameters = parameters },
                new ActionNode("plan", "Plan") { SupportsPlanning = true, Parameters = parameters }, new HexNode("blob", 1)]);
        }
        public override ActionOutcome InvokeAction(string actionId, IReadOnlyDictionary<string, FieldValue> parameters)
        {
            LastParameters = parameters;
            _acted = true;
            if (ReplaceBindings) _fieldId = "replacement";
            if (ThrowAction) throw new InvalidOperationException("Injected action failure.");
            return Action;
        }
        public ActionPlan PlanAction(string actionId, IReadOnlyDictionary<string, FieldValue> parameters, ActionScope scope)
        { Plans++; return Plan; }
        public EditOutcome ApplyActionPlan(string actionId, IReadOnlyDictionary<string, FieldValue> parameters, ActionScope scope, ActionPlan plan)
        {
            _acted = true;
            if (ThrowApplyingPlan) throw new InvalidOperationException("Injected plan failure.");
            if (PlanOutcome.Changes.Any(change => change.ValueId == "hidden")) _hidden = 2;
            return PlanOutcome;
        }
        public override ReadOnlyMemory<byte> ReadBlob(string blobId, long offset, int count) { BlobReads++; return new byte[] { 1 }; }
        public override IReadOnlyList<FileOutput> Serialize() => [];
        protected override void Dispose(bool disposing) { Disposals++; base.Dispose(disposing); }
    }
}
