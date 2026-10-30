using SaveToolkit.Core.Tests.Fakes;
using SaveToolkit.Sdk;

namespace SaveToolkit.Core.Tests;

public sealed class PresetInputOwnershipTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RequestedArrayMutationCannotAuthorizeAnotherTarget(bool duringApply)
    {
        var document = new ControlledPresetDocument();
        using var fixture = new MutationSessionFixture(document);
        _ = fixture.Session.BuildView();
        var preset = ControlledPresetDocument.Preset();
        var preview = duringApply ? fixture.Session.PlanPreset(preset) : null;
        var before = fixture.Session.State;
        document.OnResolve = (targets, choices) =>
        {
            Assert.IsType<PresetInstanceReference[]>(targets)[0] = new("owned", "substituted");
            return document.Resolve(targets, choices);
        };

        if (duringApply)
        {
            var result = fixture.Session.ApplyPresetPlan(preview!.PlanId!, preview.Revision);
            Assert.False(result.Ok);
            Assert.NotEmpty(result.Errors);
        }
        else
        {
            var rejected = fixture.Session.PlanPreset(preset);
            Assert.Null(rejected.PlanId);
            Assert.NotEmpty(rejected.Plan.Errors);
            Assert.Equal(0, document.PlanCalls);
        }
        Assert.Equal(ControlledPresetDocument.Target, preset.Actions[0].Instances![0].StableId);
        Assert.Equal(duringApply ? 1 : 0, document.PlanCalls);
        Assert.Equal(before, fixture.Session.State);
        Assert.Equal(0, document.ApplyCalls);
        Assert.Equal(0, document.Value);
        Assert.Equal(new byte[] { 0 }, document.Serialize()[0].Content.ToArray());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ChoiceDictionaryMutationCannotAuthorizeAnotherCandidate(bool duringApply)
    {
        var document = new ControlledPresetDocument { Ambiguous = true };
        using var fixture = new MutationSessionFixture(document);
        _ = fixture.Session.BuildView();
        var choices = new Dictionary<string, string> { [ControlledPresetDocument.Target] = ControlledPresetDocument.Candidate };
        var preview = duringApply ? fixture.Session.PlanPreset(ControlledPresetDocument.Preset(), choices: choices) : null;
        var before = fixture.Session.State;
        document.OnResolve = (targets, selected) =>
        {
            if (selected.Count > 0)
                Assert.IsType<Dictionary<string, string>>(selected)[ControlledPresetDocument.Target] = ControlledPresetDocument.OtherCandidate;
            return document.Resolve(targets, selected);
        };

        if (duringApply)
            Assert.False(fixture.Session.ApplyPresetPlan(preview!.PlanId!, preview.Revision).Ok);
        else
        {
            var rejected = fixture.Session.PlanPreset(ControlledPresetDocument.Preset(), choices: choices);
            Assert.Null(rejected.PlanId);
            Assert.NotEmpty(rejected.Plan.Errors);
            Assert.Equal(0, document.PlanCalls);
        }
        Assert.Equal(ControlledPresetDocument.Candidate, choices[ControlledPresetDocument.Target]);
        Assert.Equal(duringApply ? 1 : 0, document.PlanCalls);
        Assert.Equal(before, fixture.Session.State);
        Assert.Equal(0, document.ApplyCalls);
        Assert.Equal(0, document.Value);
    }

    [Fact]
    public void CallbackRetainedChoicesCannotChangePendingUserIntent()
    {
        var document = new ControlledPresetDocument { Ambiguous = true };
        using var fixture = new MutationSessionFixture(document);
        _ = fixture.Session.BuildView();
        Dictionary<string, string>? exposed = null;
        document.OnResolve = (targets, choices) =>
        {
            if (choices.Count > 0) exposed = Assert.IsType<Dictionary<string, string>>(choices);
            return document.Resolve(targets, choices);
        };
        var preview = fixture.Session.PlanPreset(ControlledPresetDocument.Preset(), choices:
            new Dictionary<string, string> { [ControlledPresetDocument.Target] = ControlledPresetDocument.Candidate });
        Assert.NotNull(preview.PlanId);
        exposed![ControlledPresetDocument.Target] = ControlledPresetDocument.OtherCandidate;
        document.OnResolve = (targets, choices) =>
        {
            if (choices.Count > 0) Assert.Equal(ControlledPresetDocument.Candidate, choices[ControlledPresetDocument.Target]);
            return document.Resolve(targets, choices);
        };

        Assert.True(fixture.Session.ApplyPresetPlan(preview.PlanId!, preview.Revision).Ok);
        Assert.Equal(1, document.ApplyCalls);
        Assert.True(fixture.Session.Undo().Ok);
        Assert.False(fixture.Session.State.CanUndo);
    }
}
