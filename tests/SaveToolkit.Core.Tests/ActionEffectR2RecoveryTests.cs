using System.Collections;
using System.Text.Json;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;

namespace SaveToolkit.Core.Tests;

public sealed class ActionEffectR2RecoveryTests
{
    [Fact]
    public void IndexedOutcomeWithThrowingEnumerationRollsBackExactlyOnceAndPreservesPriorHistory()
    {
        using var fixture = new ActionEffectR2Fixture();
        var session = fixture.Session;
        Assert.True(session.ApplyEdits([new IncomingEdit("live", JsonSerializer.SerializeToElement(3))]).Ok);
        var before = session.State;
        var preview = session.PlanAction("change", new Dictionary<string, JsonElement>());
        fixture.Document.Outcome = plan => new EditOutcome { Changes = new ThrowingChanges(plan) };

        var result = session.ApplyActionPlan(preview.PlanId!, preview.Revision);

        Assert.False(result.Ok);
        Assert.Contains(result.Errors, error => error.Message.Contains("enumerator failed", StringComparison.Ordinal));
        Assert.Equal(1, fixture.Document.RestoreCalls);
        Assert.Equal(3, fixture.Document.Value);
        Assert.Equal(new byte[] { 3 }, session.Serialize().Single().Content.ToArray());
        Assert.Equal(FieldValue.Of(3), fixture.Document.GetValue("live"));
        Assert.Equal(before, session.State);
        Assert.True(session.Undo().Ok);
        Assert.Equal(0, fixture.Document.Value);
    }

    [Fact]
    public void OversizedInverseCompensationWithFailedRequiredRebindQuarantinesAllUnsafePaths()
    {
        using var fixture = new ActionEffectR2Fixture();
        var session = fixture.Session;
        var preview = session.PlanAction("change", new Dictionary<string, JsonElement>());
        Assert.True(session.ApplyActionPlan(preview.PlanId!, preview.Revision).Ok);
        fixture.Document.OversizedInverse = true;
        fixture.Document.FailRebindAfterRestore = true;

        var undo = session.Undo();

        Assert.False(undo.Ok);
        Assert.Contains(undo.Errors, error => error.Message.Contains("could not restore", StringComparison.Ordinal));
        Assert.Equal(1, fixture.Document.Value); // Compensation restored bytes, but bindings are unusable.
        Assert.Equal(2, fixture.Document.RestoreCalls);
        Assert.False(session.CanEdit);
        Assert.False(session.State.CanUndo);
        Assert.False(session.State.CanRedo);
        Assert.False(session.ApplyEdits([new IncomingEdit("live", JsonSerializer.SerializeToElement(4))]).Ok);
        Assert.False(session.Undo().Ok);
        Assert.False(session.Redo().Ok);
        Assert.False(session.InvokeAction("change", new Dictionary<string, JsonElement>()).Ok);
        Assert.False(session.ApplyActionPlan("discarded", session.State.Revision).Ok);
        Assert.False(session.RevertChange("live", session.State.Revision).Ok);
        Assert.Null(session.PlanAction("change", new Dictionary<string, JsonElement>()).PlanId);
        Assert.Throws<InvalidOperationException>(() => session.Serialize());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void MalformedOutcomeDiagnosticsRejectAndRestoreBeforeHistory(int mode)
    {
        using var fixture = new ActionEffectR2Fixture();
        fixture.Document.Outcome = plan => new EditOutcome
        {
            Changes = [new("effect", plan.Changes[0].OldValue, plan.Changes[0].NewValue)],
            Warnings = mode switch { 0 => null!, 1 => [new string('x', 4097)], 2 => new MiscountedList<string>(0, ["hidden"]),
                4 => Enumerable.Repeat(new string('w', 4096), 1025).ToArray(), _ => [] },
            Errors = mode == 3 ? [new("effect", new string('e', 100_000))] : [],
        };
        var preview = fixture.Session.PlanAction("change", new Dictionary<string, JsonElement>());
        Assert.False(fixture.Session.ApplyActionPlan(preview.PlanId!, preview.Revision).Ok);
        Assert.Equal(0, fixture.Document.Value);
        Assert.Equal(1, fixture.Document.RestoreCalls);
        Assert.False(fixture.Session.State.CanUndo);
        Assert.True(fixture.Session.CanEdit);
    }

    private sealed class ThrowingChanges(ActionPlan plan) : IReadOnlyList<ValueChange>
    {
        public int Count => plan.Changes.Count;
        public ValueChange this[int index] => new(plan.Changes[index].ValueId, plan.Changes[index].OldValue, plan.Changes[index].NewValue);
        public IEnumerator<ValueChange> GetEnumerator() => throw new InvalidOperationException("outcome enumerator failed");
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
