using System.Text;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Values;

namespace SaveToolkit.Plugins.GenericJson.Tests;

public sealed class JsonSemanticGuardTests
{
    [Fact]
    public void AmbiguousProfileFieldsHaveNoSemanticSetterAndCannotBeInsertedByPreset()
    {
        const string source = """
            {"Profile":{"Minigame.A":"true","Minigame.A":"True"},"CloudSettings":{},"LocalSettings":{}}
            """;
        using var fixture = new JsonSessionFixture(source, title: "PPSA02343", fileName: "ue4savegame.dpx.sav");
        var actions = JsonSemanticTestHelper.Actions(fixture);
        Assert.DoesNotContain(actions, a => a.ActionId.StartsWith("semantic/", StringComparison.Ordinal) && a.ActionId.EndsWith("/set", StringComparison.Ordinal));
        var preview = fixture.Session.PlanAction("semantic/present-flags", JsonArrayEditingTests.Parameters(("enabled", FieldValue.Of(false))), expectedRevision: 0);
        Assert.NotNull(preview.PlanId);
        Assert.Empty(preview.Plan.Changes);
        Assert.Equal(2, preview.Plan.SkippedRows.Count);
        Assert.Equal(Encoding.UTF8.GetBytes(source), fixture.Bytes);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("1e1000000001")]
    public void UnsupportedChoiceMetadataRetainsGenericAccessInsteadOfBreakingTheView(string choice)
    {
        var source = "{\"version\":136,\"data\":[{\"group_name\":\"/x\",\"options\":[{\"name\":\"Choice\",\"type\":\"int_list\",\"value\":1,\"index\":0,\"values\":[" + choice + "]}]}]}";
        using var fixture = new JsonSessionFixture(source, title: "PPSA04027", fileName: "settings.json");
        _ = JsonSemanticTestHelper.Actions(fixture);
        var preview = fixture.Session.PlanAction("semantic/defaults", JsonArrayEditingTests.Parameters(), expectedRevision: 0);
        Assert.NotNull(preview.PlanId);
        Assert.Empty(preview.Plan.Changes);
        Assert.Single(preview.Plan.SkippedRows);
        Assert.Equal(Encoding.UTF8.GetBytes(source), fixture.Bytes);
    }

    [Fact]
    public void ForgedScopesAndParameterTypesRejectWithoutMutatingBytes()
    {
        using var fixture = new JsonSessionFixture("{\"options\":{\"enabled\":true},\"settings\":{}}", title: "PPSA07696", fileName: "imgui_emu_option.json");
        var action = JsonSemanticTestHelper.SetFor(fixture, "enabled");
        var planner = Assert.IsAssignableFrom<IActionPlanningDocument>(fixture.Document);
        var wrong = planner.PlanAction(action.ActionId, new Dictionary<string, FieldValue> { ["value"] = FieldValue.Of("false") }, new());
        Assert.NotEmpty(wrong.Errors);
        ActionScope[] scopes = [new(["missing"]), new(["value/2", "value/2"]),
            new() { QueryScope = new("foreign", new QueryableCollectionQuery(), 0) },
            new() { QueryScope = new("json-tree", new QueryableCollectionQuery(), 0) }];
        foreach (var scope in scopes)
            Assert.NotEmpty(planner.PlanAction("semantic/wrapper-imported", new Dictionary<string, FieldValue>(), scope).Errors);
        Assert.False(fixture.Session.State.CanUndo);
    }

    [Fact]
    public void OversizedTitleCoverageRejectsDocumentPresetAndAllowsBoundedSelection()
    {
        var source = "{\"options\":{" + string.Join(',', Enumerable.Range(0, 5001).Select(i => $"\"b{i}\":true")) + "},\"settings\":{}}";
        using var fixture = new JsonSessionFixture(source, title: "PPSA07696", fileName: "imgui_emu_option.json");
        _ = fixture.Session.BuildView();
        var planner = Assert.IsAssignableFrom<IActionPlanningDocument>(fixture.Document);
        Assert.NotEmpty(planner.PlanAction("semantic/wrapper-imported", new Dictionary<string, FieldValue>(), new()).Errors);
        var selected = planner.PlanAction("semantic/wrapper-imported", new Dictionary<string, FieldValue>(), new(["value/2"]));
        Assert.Empty(selected.Errors);
        Assert.Empty(selected.Changes);
        Assert.Equal(Encoding.UTF8.GetBytes(source), fixture.Bytes);
    }

    [Fact]
    public void OverflowingSettingMetadataCannotBreakOpeningOrAlterGenericBytes()
    {
        const string source = """
            {"version":136,"data":[{"group_name":"/x","options":[{"name":"Extreme","type":"int","value":0,
            "min_value":-79228162514264337593543950335,"max_value":79228162514264337593543950335,"step_value":2,"default_value":1}]}]}
            """;
        using var fixture = new JsonSessionFixture(source, title: "PPSA04027", fileName: "settings.json");
        _ = JsonSemanticTestHelper.Actions(fixture);
        Assert.Equal(Encoding.UTF8.GetBytes(source), fixture.Bytes);
        Assert.Contains(fixture.Fields(), f => f.Label == "value");
    }
}
