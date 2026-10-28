using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;

namespace SaveToolkit.Plugins.GenericJson.Tests;

public sealed class JsonCyberpunkWriteTests
{
    private const string Source = """
        {"version":136,"unknown":"\u0041","data":[{"group_name":"/audio","options":[
        {"name":"Volume","type":"int","value":50,"min_value":0,"max_value":100,"step_value":5,"default_value":100},
        {"name":"Enabled","type":"bool","value":true,"default_value":false},
        {"name":"Mode","type":"string_list","value":"Quality","index":1,"default_index":0,"values":["Performance","Quality"]},
        {"name":"Dynamic","type":"string_list","value":"raw","index":2,"is_dynamic":true}]}]}
        """;

    [Fact]
    public void ChoiceChangesValueAndDerivedIndexWithSingleUndoAndExactUnrelatedBytes()
    {
        using var fixture = Open();
        var action = JsonSemanticTestHelper.Actions(fixture).Single(a => a.Parameters.FirstOrDefault()?.Spec is EnumSpec);
        var preview = fixture.Session.PlanAction(action.ActionId, JsonArrayEditingTests.Parameters(("value", FieldValue.Of("0"))), expectedRevision: 0);
        Assert.Equal(2, preview.Plan.Changes.Count);
        Assert.Single(preview.Plan.Changes, c => c.Derived);
        Assert.False(fixture.Session.InvokeAction(action.ActionId, JsonArrayEditingTests.Parameters()).Ok);
        JsonSemanticTestHelper.ApplyAndVerify(fixture, action.ActionId, ("value", FieldValue.Of("0")));
    }

    [Fact]
    public void RecordedDefaultsPresetChangesOnlyEligibleValuesAndReportsSkippedDynamicChoice()
    {
        using var fixture = Open();
        _ = JsonSemanticTestHelper.Actions(fixture);
        var preview = fixture.Session.PlanAction("semantic/defaults", JsonArrayEditingTests.Parameters(), expectedRevision: 0);
        Assert.Equal(4, preview.Plan.Changes.Count);
        Assert.Single(preview.Plan.SkippedRows);
        JsonSemanticTestHelper.ApplyAndVerify(fixture, "semantic/defaults");
    }

    [Fact]
    public void SingleRecordedDefaultAndSelectedDefaultPresetPersistAndUndoTogether()
    {
        using var fixture = Open();
        var actions = JsonSemanticTestHelper.Actions(fixture);
        var choice = actions.Single(a => a.Parameters.FirstOrDefault()?.Spec is EnumSpec);
        JsonSemanticTestHelper.ApplyAndVerify(fixture, choice.ActionId.Replace("/set", "/default", StringComparison.Ordinal));
        var volume = actions.Single(a => a.Parameters.FirstOrDefault()?.Spec is ExactNumberSpec);
        var row = "value/" + volume.ActionId.Split('/')[1];
        var preview = fixture.Session.PlanAction("semantic/defaults", JsonArrayEditingTests.Parameters(), new([row]), fixture.Session.State.Revision);
        Assert.NotNull(preview.PlanId);
        Assert.Single(preview.Plan.Changes);
        Assert.Equal(row, preview.Plan.Changes[0].ValueId);
        Assert.True(fixture.Session.ApplyActionPlan(preview.PlanId, preview.Revision).Ok);
    }

    [Fact]
    public void ApproximateFloatStepsDoNotDisableRecordedDefaultsOrRoundExactNumbers()
    {
        const string source = """
            {"version":136,"data":[{"group_name":"/controls","options":[
            {"name":"Deadzone","type":"float","value":0.1500000059,"min_value":0.0,"max_value":0.4900000095,"step_value":0.0099999997,"default_value":0.1500000059}]}]}
            """;
        using var fixture = new JsonSessionFixture(source, title: "PPSA04027", fileName: "settings.json");
        var action = JsonSemanticTestHelper.Actions(fixture).Single(a => a.Parameters.FirstOrDefault()?.Spec is ExactNumberSpec);
        JsonSemanticTestHelper.ApplyAndVerify(fixture, action.ActionId, ("value", FieldValue.ExactNumber("0.16000000000000000000000000001")));
        JsonSemanticTestHelper.ApplyAndVerify(fixture, "semantic/defaults");
    }

    [Theory]
    [InlineData("105")]
    [InlineData("-5")]
    [InlineData("51")]
    [InlineData("50.00000000000000000000000000000000001")]
    [InlineData("5e-100")]
    public void ScalarBoundsIntegralStepAndExactArithmeticRejectInvalidInput(string number)
    {
        using var fixture = Open();
        var action = JsonSemanticTestHelper.Actions(fixture).Single(a => a.Parameters.FirstOrDefault()?.Spec is ExactNumberSpec);
        var preview = fixture.Session.PlanAction(action.ActionId, JsonArrayEditingTests.Parameters(("value", FieldValue.ExactNumber(number))), expectedRevision: 0);
        Assert.NotEmpty(preview.Plan.Errors);
        Assert.False(fixture.Session.State.CanUndo);
    }

    [Fact]
    public void BoundedScalarAndDynamicExplicitPairPersistWithWarnings()
    {
        using var fixture = Open();
        var scalar = JsonSemanticTestHelper.Actions(fixture).Single(a => a.Parameters.FirstOrDefault()?.Spec is ExactNumberSpec);
        JsonSemanticTestHelper.ApplyAndVerify(fixture, scalar.ActionId, ("value", FieldValue.ExactNumber("55")));
        var dynamicChoice = JsonSemanticTestHelper.Actions(fixture).Single(a => a.ActionId.StartsWith("semantic/", StringComparison.Ordinal) && a.Parameters.Count == 2);
        JsonSemanticTestHelper.ApplyAndVerify(fixture, dynamicChoice.ActionId, ("value", FieldValue.Of("new")), ("index", FieldValue.Of(1L)));
        var invalid = fixture.Session.PlanAction(dynamicChoice.ActionId, JsonArrayEditingTests.Parameters(("value", FieldValue.Of("long")), ("index", FieldValue.Of(3L))),
            expectedRevision: fixture.Session.State.Revision);
        Assert.NotEmpty(invalid.Plan.Errors);
    }

    [Fact]
    public void StalePlanAndChangedRawMetadataCannotBypassRevalidation()
    {
        using var fixture = Open();
        var action = JsonSemanticTestHelper.Actions(fixture).Single(a => a.Parameters.FirstOrDefault()?.Spec is ExactNumberSpec);
        var preview = fixture.Session.PlanAction(action.ActionId, JsonArrayEditingTests.Parameters(("value", FieldValue.ExactNumber("55"))), expectedRevision: 0);
        var bound = fixture.Fields().Single(f => f.Label == "max_value");
        Assert.True(fixture.Edit(bound.ValueId, JsonEditingTests.Number("1000")).Ok);
        Assert.True(fixture.Session.ApplyActionPlan(preview.PlanId!, preview.Revision).Stale);
        var planner = Assert.IsAssignableFrom<IActionPlanningDocument>(fixture.Document);
        Assert.NotEmpty(planner.PlanAction(action.ActionId, new Dictionary<string, FieldValue> { ["value"] = FieldValue.ExactNumber("55") }, new()).Errors);
    }

    private static JsonSessionFixture Open() => new("\uFEFF" + Source, title: "PPSA04027", fileName: "settings.json");
}
