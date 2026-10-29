using System.Text.Json;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Core.Tests.Fakes;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Core.Tests;

public sealed class HistorySessionMutationEffectivenessTests
{
    [Fact]
    public void ReviewDiscoveredLazyBindingsSurviveStructuralUndoBeforeScalarReplay()
    {
        using var fixture = new MutationSessionFixture(new PlanningPlugin.PlanningDocument("save.dat"),
            openOriginal: () => new PlanningPlugin.PlanningDocument("save.dat"));
        var session = fixture.Session;
        session.BuildView();
        Assert.True(session.GetChanges().Complete);
        Assert.True(session.ApplyEdits([new("item/a", JsonSerializer.SerializeToElement(3))]).Ok);
        var preview = session.PlanAction("remove", new Dictionary<string, JsonElement>(), new(["b"]));
        Assert.True(session.ApplyActionPlan(preview.PlanId!, preview.Revision).Ok);
        Assert.True(session.Undo().Ok);
        Assert.True(session.Undo().Ok);
        Assert.Empty(session.GetChanges().Changes);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AThrowingReplayReturnsARejectionWithoutLosingTheUndoEntry(bool metadata)
    {
        var document = new ReplayProbe();
        using var fixture = new MutationSessionFixture(document);
        fixture.Session.BuildView();
        Assert.True(fixture.Session.ApplyEdits([Edit(2)]).Ok);
        document.RejectEdits = !metadata;
        document.FailMetadata = metadata;
        var result = fixture.Session.Undo();
        Assert.False(result.Ok);
        Assert.True(result.State.CanUndo);
        Assert.True(result.State.IsDirty);
        Assert.Equal(FieldValue.Of(2L), document.GetValue("value"));
    }

    [Fact]
    public void FailedGroupCompensationClearsHistoryAndPermanentlyRetainsDirtyState()
    {
        var document = new ReplayProbe();
        using var fixture = new MutationSessionFixture(document);
        fixture.Session.BuildView();
        Assert.True(fixture.Session.ApplyEdits([Edit(2)]).Ok);
        var result = fixture.Session.RunGroup(session =>
        {
            Assert.True(session.ApplyEdits([Edit(3)]).Ok);
            document.RejectEdits = true;
            return session.ApplyEdits([new("missing", JsonSerializer.SerializeToElement(4))]);
        });
        Assert.False(result.Ok);
        Assert.False(result.State.CanUndo);
        Assert.False(result.State.CanRedo);
        Assert.True(result.State.IsDirty);
        Assert.Equal(FieldValue.Of(3L), document.GetValue("value"));
    }

    [Fact]
    public void NonUndoableActionsInsideASuccessfulGroupRetireItsEarlierSteps()
    {
        using var fixture = new MutationSessionFixture(new ReplayProbe());
        fixture.Session.BuildView();
        var result = fixture.Session.RunGroup(session =>
        {
            Assert.True(session.ApplyEdits([Edit(2)]).Ok);
            return session.InvokeAction("irreversible", new Dictionary<string, JsonElement>());
        });
        Assert.True(result.Ok);
        Assert.False(result.State.CanUndo);
        Assert.True(result.State.IsDirty);
    }

    private static IncomingEdit Edit(int value) => new("value", JsonSerializer.SerializeToElement(value));

    private sealed class ReplayProbe : BoundSaveDocument, IFieldMetadataDocument
    {
        private int _value = 1;
        public bool RejectEdits { get; set; }
        public bool FailMetadata { get; set; }
        public override string Title => "Replay";
        public override ViewNode BuildView() => new SectionNode(Title,
            [Field("value", "Value", IntegerSpec.UInt8, () => FieldValue.Of(_value), value => _value = value.AsInt32()),
                new ActionNode("irreversible", "Irreversible")]);
        public override EditOutcome ApplyEdits(IReadOnlyList<ValueEdit> edits) => RejectEdits
            ? throw new InvalidOperationException("Replay rejected.") : base.ApplyEdits(edits);
        public override IReadOnlyList<FieldMetadata> ResolveFieldMetadata(IReadOnlyList<string> valueIds) => FailMetadata
            ? throw new InvalidOperationException("Metadata rejected.")
            : valueIds.Contains("value") ? [new("value", "Value", IntegerSpec.UInt8)] : [];
        public override ActionOutcome InvokeAction(string actionId, IReadOnlyDictionary<string, FieldValue> parameters)
        { _value++; return new() { Undoable = false }; }
        public override IReadOnlyList<FileOutput> Serialize() => [];
    }
}
