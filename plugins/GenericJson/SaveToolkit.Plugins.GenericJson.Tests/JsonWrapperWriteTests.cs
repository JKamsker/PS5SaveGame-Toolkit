using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;

namespace SaveToolkit.Plugins.GenericJson.Tests;

public sealed class JsonWrapperWriteTests
{
    private const string Source = """
        {"options":{"cubic_A":-0.5,"upscaling_factor":5,"enable_fx_smoothing":false,"p":{"gamma":1.2000000476837158}},
        "settings":{"--has-shown-start-select-help":"true"},"unknown":"\u0041"}
        """;

    [Theory]
    [InlineData("cubic_A", "-0.25")]
    [InlineData("upscaling_factor", "4")]
    [InlineData("gamma", "1.2000000476837157")]
    public void RawNumericWrapperAdaptersPreserveExactValuesWithinConservativeRanges(string label, string number)
    {
        using var fixture = Open();
        JsonSemanticTestHelper.ApplyAndVerify(fixture, JsonSemanticTestHelper.SetFor(fixture, label).ActionId, ("value", FieldValue.ExactNumber(number)));
    }

    [Fact]
    public void WrapperBooleanAndImportedPresetUsePlannedExistingValuesOnly()
    {
        using var fixture = Open();
        JsonSemanticTestHelper.ApplyAndVerify(fixture, JsonSemanticTestHelper.SetFor(fixture, "enable_fx_smoothing").ActionId, ("value", FieldValue.Of(true)));
        JsonSemanticTestHelper.ApplyAndVerify(fixture, JsonSemanticTestHelper.SetFor(fixture, "--has-shown-start-select-help").ActionId, ("value", FieldValue.Of(false)));
        JsonSemanticTestHelper.ApplyAndVerify(fixture, "semantic/wrapper-imported");
        Assert.Equal(System.Text.Encoding.UTF8.GetBytes(Source), fixture.Bytes);
    }

    [Theory]
    [InlineData("cubic_A", "-0.6")]
    [InlineData("upscaling_factor", "6")]
    [InlineData("upscaling_factor", "1.5")]
    public void UnknownNumericEnumDomainsRemainConservativeAndRejectOverflow(string label, string number)
    {
        using var fixture = Open();
        var action = JsonSemanticTestHelper.SetFor(fixture, label);
        var preview = fixture.Session.PlanAction(action.ActionId, JsonArrayEditingTests.Parameters(("value", FieldValue.ExactNumber(number))), expectedRevision: 0);
        Assert.NotEmpty(preview.Plan.Errors);
        Assert.False(fixture.Session.State.CanUndo);
    }

    [Fact]
    public void ForeignTitleAndUnknownActionsCannotUseTitlePresets()
    {
        using var fixture = new JsonSessionFixture(Source);
        var planner = Assert.IsAssignableFrom<IActionPlanningDocument>(fixture.Document);
        Assert.NotEmpty(planner.PlanAction("semantic/wrapper-imported", new Dictionary<string, FieldValue>(), new()).Errors);
    }

    private static JsonSessionFixture Open() => new(Source, title: "PPSA07696", fileName: "imgui_emu_option.json");
}
