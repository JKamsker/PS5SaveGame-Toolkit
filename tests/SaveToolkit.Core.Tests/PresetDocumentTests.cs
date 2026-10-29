using System.Text.Json;
using SaveToolkit.Core.Tests.Fakes;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Saves;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Core.Tests;

public sealed class PresetDocumentTests
{
    [Fact]
    public void AmbiguousResolutionBlocksPlanWithoutMutation()
    {
        using var fixture = new MutationSessionFixture(new PresetFixtureDocument { Resolution = PresetInstanceResolutionStatus.Ambiguous });
        _ = fixture.Session.BuildView();
        var preset = Preset("owned-weapon");
        var preview = fixture.Session.PlanPreset(preset);
        Assert.Null(preview.PlanId);
        Assert.Equal(PresetInstanceResolutionStatus.Ambiguous, preview.Resolution.Instances.Single().Status);
        Assert.NotEmpty(preview.Plan.Errors);
        Assert.False(fixture.Session.State.CanUndo);
    }

    [Fact]
    public void AdapterApplyIsAtomicAndOneUndoStep()
    {
        var document = new PresetFixtureDocument();
        using var fixture = new MutationSessionFixture(document);
        _ = fixture.Session.BuildView();
        var preview = fixture.Session.PlanPreset(Preset("value"));
        Assert.NotNull(preview.PlanId);
        Assert.Equal(0, document.Value);
        Assert.True(fixture.Session.ApplyPresetPlan(preview.PlanId!, preview.Revision).Ok);
        Assert.Equal(9, document.Value);
        Assert.True(fixture.Session.State.CanUndo);
        Assert.True(fixture.Session.Undo().Ok);
        Assert.Equal(0, document.Value);
        Assert.False(fixture.Session.State.CanUndo);
    }

    [Fact]
    public void ExplicitChoiceIsPassedToResolutionInsteadOfGuessing()
    {
        var document = new PresetFixtureDocument { Resolution = PresetInstanceResolutionStatus.Ambiguous };
        using var fixture = new MutationSessionFixture(document);
        _ = fixture.Session.BuildView();
        var preview = fixture.Session.PlanPreset(Preset("value"), choices: new Dictionary<string, string> { ["id"] = "candidate" });
        Assert.NotNull(preview.PlanId);
        Assert.Equal(PresetInstanceResolutionStatus.Resolved, preview.Resolution.Instances.Single().Status);
    }

    private static ToolkitPreset Preset(string id) => new("1", id, id,
        new PresetCompatibility("test", "1", ["PPSA99999"]),
        [new PresetAction("set", new Dictionary<string, JsonElement>(), [new("owned", "id")])], DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

    private sealed class PresetFixtureDocument : BoundSaveDocument, IPresetDocument
    {
        public int Value { get; private set; }
        public PresetInstanceResolutionStatus Resolution { get; init; } = PresetInstanceResolutionStatus.Resolved;
        public override string Title => "Preset fixture";
        public PresetCapability PresetCapability { get; } = new("1", "r19", [new("set", "Set", [])], 4, "fixture", "pack");
        public override ViewNode BuildView() => new SectionNode("Fixture", [Field("value", "Value", IntegerSpec.Int32,
            () => FieldValue.Of((long)Value), v => Value = v.AsInt32())]);
        public PresetResolution ResolvePresetInstances(IReadOnlyList<PresetInstanceReference> instances) =>
            new(instances.Select(instance => new PresetInstanceResolution(instance, Resolution)).ToArray());
        public PresetResolution ResolvePresetInstances(IReadOnlyList<PresetInstanceReference> instances, IReadOnlyDictionary<string, string> choices) =>
            choices.Count > 0
                ? new(instances.Select(instance => new PresetInstanceResolution(instance, PresetInstanceResolutionStatus.Resolved,
                    [new("owned", choices[instance.StableId])])).ToArray())
                : ResolvePresetInstances(instances);
        public ActionPlan PlanPreset(IReadOnlyList<PresetAction> actions, PresetResolution resolution) =>
            new() { Changes = [new PlannedChange("value", "Value", FieldValue.Of((long)Value), FieldValue.Of(9L))] };
        public EditOutcome ApplyPreset(IReadOnlyList<PresetAction> actions, PresetResolution resolution, ActionPlan plan) =>
            ApplyEdits([new ValueEdit("value", FieldValue.Of(9L))]);
        public override IReadOnlyList<FileOutput> Serialize() => [];
    }
}
