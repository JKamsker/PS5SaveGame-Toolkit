using System.Buffers.Binary;
using System.Text.Json;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.EldenRing.Tests;

public sealed class RunePlanningTests
{
    private const string ActionId = "slots/0/runes/set";
    private static Dictionary<string, FieldValue> Parameters(long value) => new() { ["runes"] = FieldValue.Of(value) };
    private static Dictionary<string, JsonElement> Incoming(long value) => new() { ["runes"] = JsonSerializer.SerializeToElement(value) };

    [Fact]
    public void SamplePreviewApplyReviewUndoRedo_OnlyTouchTheExistingFourByteRuneField()
    {
        var file = WriteTestSupport.Sample();
        var original = file.ReadAllBytes();
        var expected = original.ToArray();
        BinaryPrimitives.WriteUInt32LittleEndian(expected.AsSpan(0xA2FF + 0x64), 1148);
        var plugin = new EldenRingPlugin();
        var document = plugin.Open(file);
        using var session = new DocumentSession("rune-plan", file, plugin, document);
        _ = session.BuildView();
        var action = Assert.Single(session.Expand("characters/0").OfType<ActionNode>(), action => action.ActionId == ActionId);
        Assert.Equal(ActionId, action.ActionId);
        Assert.True(action.SupportsPlanning);
        var preview = session.PlanAction(ActionId, Incoming(1148));
        Assert.NotNull(preview.PlanId);
        Assert.Empty(preview.Plan.Errors);
        Assert.Single(preview.Plan.Changes);
        Assert.Equal(1147U, preview.Plan.Changes[0].OldValue.AsUInt32());
        Assert.False(session.State.IsDirty);
        Assert.False(session.State.CanUndo);
        Assert.True(original.AsSpan().SequenceEqual(Assert.Single(document.Serialize()).Content.Span));
        Assert.True(session.ApplyActionPlan(preview.PlanId, preview.Revision).Ok);
        Assert.True(expected.AsSpan().SequenceEqual(Assert.Single(document.Serialize()).Content.Span));
        Assert.Equal(504U, document.GetValue("slots/0/vitals/hp/current").AsUInt32());
        Assert.Equal(504U, document.GetValue("slots/0/vitals/hp/effective-max").AsUInt32());
        Assert.Equal(476U, document.GetValue("slots/0/vitals/hp/base-max").AsUInt32());
        Assert.Equal(4427U, document.GetValue("slots/0/rune-memory").AsUInt32());
        Assert.Equal("slots/0/runes", Assert.Single(session.GetChanges().Changes).ValueId);
        Assert.True(session.Undo().Ok);
        Assert.False(session.State.CanUndo); // One action, one undo step.
        Assert.True(original.AsSpan().SequenceEqual(Assert.Single(document.Serialize()).Content.Span));
        Assert.True(session.Redo().Ok);
        Assert.True(expected.AsSpan().SequenceEqual(Assert.Single(document.Serialize()).Content.Span));
        Assert.True(session.Undo().Ok);
        Assert.Empty(session.Serialize());
    }

    [Fact]
    public void DirectApplyRegistersUnopenedBindingsAndRejectsTamperedOrStaleEffects()
    {
        var fixture = new SyntheticSave([0, 9]);
        using var document = fixture.Open();
        var planner = (IActionPlanningDocument)document;
        var parameters = Parameters(101);
        var preview = planner.PlanAction(ActionId, parameters, new());
        var forged = preview with { Changes = [preview.Changes[0] with { NewValue = FieldValue.Of(999L) }] };
        Assert.False(planner.ApplyActionPlan(ActionId, parameters, new(), forged).Succeeded);
        Assert.True(fixture.Bytes.AsSpan().SequenceEqual(Assert.Single(document.Serialize()).Content.Span));
        var applied = planner.ApplyActionPlan(ActionId, parameters, new(), preview);
        Assert.True(applied.Succeeded);
        Assert.Single(applied.Changes);
        Assert.False(planner.ApplyActionPlan(ActionId, parameters, new(), preview).Succeeded);
        Assert.True(document.ApplyEdits(applied.Changes.Select(change => new ValueEdit(change.ValueId, change.OldValue)).ToArray()).Succeeded);
        Assert.True(fixture.Bytes.AsSpan().SequenceEqual(Assert.Single(document.Serialize()).Content.Span));
    }

    [Theory]
    [InlineData(-1L)]
    [InlineData(1001L)]
    [InlineData(long.MaxValue)]
    public void OutOfHistoryBoundsRejectsWithoutAnyEffects(long requested)
    {
        var fixture = new SyntheticSave([0]);
        using var document = fixture.Open();
        var planner = (IActionPlanningDocument)document;
        var parameters = Parameters(requested);
        var preview = planner.PlanAction(ActionId, parameters, new());
        Assert.NotEmpty(preview.Errors);
        Assert.Empty(preview.Changes);
        Assert.False(planner.ApplyActionPlan(ActionId, parameters, new(), preview).Succeeded);
        Assert.True(fixture.Bytes.AsSpan().SequenceEqual(Assert.Single(document.Serialize()).Content.Span));
    }

    [Fact]
    public void MalformedParametersScopesAndReadOnlyVersionsCannotBypassPlanning()
    {
        var fixture = new SyntheticSave([0]);
        using var document = fixture.Open();
        var planner = (IActionPlanningDocument)document;
        Assert.Throws<InvalidOperationException>(() => document.InvokeAction(ActionId, Parameters(101)));
        Assert.Throws<KeyNotFoundException>(() => planner.PlanAction("slots/1/runes/set", Parameters(101), new()));
        Assert.NotEmpty(planner.PlanAction(ActionId, new Dictionary<string, FieldValue>(), new()).Errors);
        Assert.NotEmpty(planner.PlanAction(ActionId, new Dictionary<string, FieldValue> { ["runes"] = FieldValue.Of("101") }, new()).Errors);
        Assert.NotEmpty(planner.PlanAction(ActionId, new Dictionary<string, FieldValue>
            { ["runes"] = FieldValue.Of(101L), ["extra"] = FieldValue.Of(1L) }, new()).Errors);
        Assert.NotEmpty(planner.PlanAction(ActionId, Parameters(101), new([])).Errors);
        Assert.NotEmpty(planner.PlanAction(ActionId, Parameters(101), new(["arbitrary-row"])).Errors);
        Assert.NotEmpty(planner.PlanAction(ActionId, Parameters(101), new()
            { QueryScope = new("slots/0/inventory/held", new(), 0) }).Errors);
        using var readOnly = new SyntheticSave([0], version: 151).Open();
        var readOnlyPreview = ((IActionPlanningDocument)readOnly).PlanAction(ActionId, Parameters(101), new());
        Assert.NotEmpty(readOnlyPreview.Errors);
        Assert.False(((IActionPlanningDocument)readOnly).ApplyActionPlan(ActionId, Parameters(101), new(), readOnlyPreview).Succeeded);
        Assert.True(fixture.Bytes.AsSpan().SequenceEqual(Assert.Single(document.Serialize()).Content.Span));
    }

    [Fact]
    public void SessionRejectsStalePlansAndNoopDoesNotCreateUndoHistory()
    {
        var file = WriteTestSupport.Sample();
        var plugin = new EldenRingPlugin();
        using var session = new DocumentSession("stale-runes", file, plugin, plugin.Open(file));
        _ = session.BuildView();
        _ = session.Expand("characters/0");
        var noop = session.PlanAction(ActionId, Incoming(1147));
        Assert.Empty(noop.Plan.Changes);
        Assert.True(session.ApplyActionPlan(noop.PlanId!, noop.Revision).Ok);
        Assert.False(session.State.CanUndo);
        var preview = session.PlanAction(ActionId, Incoming(1148));
        Assert.True(session.ApplyEdits([new IncomingEdit("slots/0/runes", JsonSerializer.SerializeToElement(1149))]).Ok);
        Assert.True(session.ApplyActionPlan(preview.PlanId!, preview.Revision).Stale);
        Assert.True(session.Undo().Ok);
        Assert.False(session.State.IsDirty);
        Assert.False(session.InvokeAction(ActionId, Incoming(101)).Ok);
    }
}
