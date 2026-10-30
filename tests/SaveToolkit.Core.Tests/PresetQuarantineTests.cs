using System.Text.Json;
using SaveToolkit.Core.Tests.Fakes;
using SaveToolkit.Sdk;

namespace SaveToolkit.Core.Tests;

public sealed class PresetQuarantineTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FailedEffectRecoveryBlocksRetainedAndFreshSemanticPresets(bool inverseCompensation)
    {
        using var fixture = new ActionEffectR2Fixture();
        var session = fixture.Session;
        var document = fixture.Document;
        var preset = ControlledPresetDocument.Preset();
        var retained = session.PlanPreset(preset);
        Assert.NotNull(retained.PlanId);
        var action = session.PlanAction("change", new Dictionary<string, JsonElement>());
        if (inverseCompensation)
        {
            Assert.True(session.ApplyActionPlan(action.PlanId!, action.Revision).Ok);
            retained = session.PlanPreset(preset);
            Assert.NotNull(retained.PlanId);
            document.OversizedInverse = true;
            document.FailRebindAfterRestore = true;
            Assert.False(session.Undo().Ok);
            Assert.Equal(2, document.RestoreCalls);
        }
        else
        {
            document.Outcome = _ => new EditOutcome { Errors = [new("effect", "reject mutation")] };
            document.FailRestore = true;
            Assert.False(session.ApplyActionPlan(action.PlanId!, action.Revision).Ok);
            Assert.Equal(1, document.RestoreCalls);
            Assert.Equal(retained.Revision, session.State.Revision);
        }
        Assert.True(document.CanEdit); // Provider writability cannot override host quarantine.
        Assert.False(session.CanEdit);
        var callbacks = (document.CapabilityReads, document.PresetResolveCalls, document.PresetPlanCalls, document.PresetApplyCalls);
        var state = session.State;
        var bytes = document.Serialize().Single().Content.ToArray();

        Assert.False(session.ApplyPresetPlan(retained.PlanId!, retained.Revision).Ok);
        Assert.Null(session.PlanPreset(preset).PlanId);
        Assert.False(session.ApplyPresetPlan("fresh", session.State.Revision).Ok);
        Assert.Null(session.PresetCapability);
        Assert.Throws<ArgumentException>(() => session.CapturePreset(new("set", new Dictionary<string, JsonElement>())));

        Assert.Equal(callbacks, (document.CapabilityReads, document.PresetResolveCalls, document.PresetPlanCalls, document.PresetApplyCalls));
        Assert.Equal(0, document.PresetValue);
        Assert.Equal(bytes, document.Serialize().Single().Content.ToArray());
        Assert.Equal(state, session.State);
        Assert.False(session.State.CanUndo);
        Assert.False(session.State.CanRedo);
        Assert.Throws<InvalidOperationException>(() => session.Serialize());
    }
}
