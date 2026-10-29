using System.Text;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.GenericJson.Tests;

public sealed class JsonMetadataStructureTests
{
    private const string Source = """
        {"version":136,"untouched":"\u0041","data":[{"group_name":"/x","options":[
        {"name":"Mode","type":"string_list","value":"Quality","index":1,"default_index":0,"values":["Performance","Quality"]},
        {"name":"Volume","type":"int","value":50,"min_value":0,"max_value":100,"step_value":5,"default_value":100}]}]}
        """;

    [Theory]
    [InlineData("move")]
    [InlineData("remove")]
    public void ChangedChoiceMembershipOrOrderDisablesAdapterUntilExactStructuralUndo(string operation)
    {
        using var fixture = Open();
        var setter = JsonSemanticTestHelper.Actions(fixture).Single(a => a.Parameters.FirstOrDefault()?.Spec is EnumSpec);
        var choices = Container(fixture, "array", "[\"values\"]#1");
        var parameters = operation == "move"
            ? new[] { ("from", FieldValue.Of(0L)), ("to", FieldValue.Of(1L)) }
            : new[] { ("at", FieldValue.Of(0L)) };
        JsonArrayEditingTests.Apply(fixture, choices + "/" + operation, parameters);
        AssertDeclined(fixture, setter.ActionId, FieldValue.Of("0"));
        Assert.True(fixture.Session.Undo().Ok);
        Assert.Equal(Encoding.UTF8.GetBytes(Source), fixture.Bytes);
        Assert.True(fixture.Session.Redo().Ok);
        AssertDeclined(fixture, setter.ActionId, FieldValue.Of("0"));
        Assert.True(fixture.Session.Undo().Ok);
        JsonSemanticTestHelper.ApplyAndVerify(fixture, setter.ActionId, ("value", FieldValue.Of("0")));
    }

    [Fact]
    public void RemovingRecordedStepDoesNotPromoteEditedMetadataToAnAuthoritativeSchema()
    {
        using var fixture = Open();
        var setter = JsonSemanticTestHelper.Actions(fixture).Single(a => a.Parameters.FirstOrDefault()?.Spec is ExactNumberSpec);
        var option = Container(fixture, "object", "[1]");
        JsonArrayEditingTests.Apply(fixture, option + "/remove", ("at", FieldValue.Of(5L)));
        AssertDeclined(fixture, setter.ActionId, FieldValue.ExactNumber("51"));
        Assert.True(fixture.Session.Undo().Ok);
        Assert.Equal(Encoding.UTF8.GetBytes(Source), fixture.Bytes);
        var invalid = fixture.Session.PlanAction(setter.ActionId,
            JsonArrayEditingTests.Parameters(("value", FieldValue.ExactNumber("51"))), scope: null, expectedRevision: null, cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotEmpty(invalid.Plan.Errors);
        JsonSemanticTestHelper.ApplyAndVerify(fixture, setter.ActionId, ("value", FieldValue.ExactNumber("55")));
    }

    [Fact]
    public void DefaultsSkipChangedChoiceSchemaWhileApplyingAnUnchangedOptionWithExactUndo()
    {
        using var fixture = Open();
        var choice = JsonSemanticTestHelper.Actions(fixture).Single(a => a.Parameters.FirstOrDefault()?.Spec is EnumSpec);
        var choices = Container(fixture, "array", "[\"values\"]#1");
        JsonArrayEditingTests.Apply(fixture, choices + "/move", ("from", FieldValue.Of(0L)), ("to", FieldValue.Of(1L)));
        var before = fixture.Bytes;
        var preview = fixture.Session.PlanAction("semantic/defaults", JsonArrayEditingTests.Parameters(), scope: null, expectedRevision: null, cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotNull(preview.PlanId);
        Assert.Single(preview.Plan.Changes);
        Assert.Equal("100", preview.Plan.Changes[0].NewValue.AsUnion().Value.AsExactNumber());
        Assert.Contains(preview.Plan.SkippedRows, row => row.RowKey == "value/" + choice.ActionId.Split('/')[1]);
        Assert.Contains(preview.Plan.Warnings, warning => warning.Contains("game may reject", StringComparison.Ordinal));
        Assert.Equal(before, fixture.Bytes);
        var outcome = fixture.Session.ApplyActionPlan(preview.PlanId, preview.Revision, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(outcome.Ok);
        Assert.Contains(outcome.Warnings, warning => warning.Contains("game may reject", StringComparison.Ordinal));
        var expected = Encoding.UTF8.GetString(before).Replace("\"value\":50", "\"value\":100", StringComparison.Ordinal);
        Assert.Equal(Encoding.UTF8.GetBytes(expected), fixture.Bytes);
        Assert.True(fixture.Session.Undo().Ok);
        Assert.Equal(before, fixture.Bytes);
        Assert.True(fixture.Session.Undo().Ok);
        Assert.Equal(Encoding.UTF8.GetBytes(Source), fixture.Bytes);
        Assert.False(fixture.Session.State.IsDirty);
    }

    private static void AssertDeclined(JsonSessionFixture fixture, string actionId, FieldValue parameter)
    {
        var before = fixture.Bytes;
        var revision = fixture.Session.State.Revision;
        var actions = JsonSemanticTestHelper.Actions(fixture);
        var planner = Assert.IsAssignableFrom<IActionPlanningDocument>(fixture.Document);
        var values = new Dictionary<string, FieldValue> { ["value"] = parameter };
        var rejected = planner.PlanAction(actionId, values, new());
        Assert.NotEmpty(rejected.Errors);
        Assert.DoesNotContain(actions, a => a.ActionId == actionId);
        Assert.False(planner.ApplyActionPlan(actionId, values, new(), new()).Succeeded);
        var defaults = fixture.Session.PlanAction("semantic/defaults", JsonArrayEditingTests.Parameters());
        Assert.NotNull(defaults.PlanId);
        Assert.Contains(defaults.Plan.SkippedRows, row => row.RowKey == "value/" + actionId.Split('/')[1]);
        Assert.Equal(revision, fixture.Session.State.Revision);
        Assert.Equal(before, fixture.Bytes);
        Assert.NotEmpty(fixture.Fields()); // Syntax-only editing remains available.
    }

    private static string Container(JsonSessionFixture fixture, string type, string suffix)
    {
        var row = Assert.Single(fixture.Session.QueryCollection("json-tree", new QueryableCollectionQuery
        {
            Filters = new Dictionary<string, string> { ["type"] = type },
        }).Rows, row => Assert.IsType<TextNode>(row.Cells["path"]).Text.EndsWith(suffix, StringComparison.Ordinal));
        return row.Key.Replace("value/", type + "/", StringComparison.Ordinal);
    }

    private static JsonSessionFixture Open() => new(Source, title: "PPSA04027", fileName: "settings.json");
}
