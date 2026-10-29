using System.Text;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.GenericJson.Tests;

public sealed class JsonPlanningCharacterizationTests
{
    private static readonly string[] SkippedIds = ["value/3", "value/4", "value/0"];
    private const string Profile = """
        {"Profile":{"Minigame.A":"True","MinigameCounter.A_Wins":"0012","Minigame.Bad":"?"},"CloudSettings":{},"LocalSettings":{}}
        """;

    [Theory]
    [InlineData("semantic/defaults", "Preset does not match this title.")]
    [InlineData("semantic/progress", "Progress tuple requires all three existing, unambiguous fields.")]
    [InlineData("semantic/bad/set", "Unknown semantic action.")]
    [InlineData("semantic/999/set", "Field no longer has a supported semantic adapter.")]
    [InlineData("semantic/2/unknown", "No supported recorded default/action.")]
    [InlineData("semantic/present-flags", "Supply a boolean flag value.")]
    public void SemanticFailuresRetainExactDiagnosticAndWarning(string action, string error)
    {
        using var document = OpenProfile();
        var plan = ((IActionPlanningDocument)document).PlanAction(action, new Dictionary<string, FieldValue>(), new());
        Assert.Equal(new FieldError(action, error), Assert.Single(plan.Errors));
        Assert.Equal(JsonSemanticActions.Warning, Assert.Single(plan.Warnings));
        Assert.Empty(plan.Changes);
        Assert.Equal(Encoding.UTF8.GetBytes(Profile), document.Serialize()[0].Content.ToArray());
    }

    [Theory]
    [InlineData(false, "Selection includes missing nodes or exceeds the action bound.")]
    [InlineData(true, "Use a resolved JSON tree selection.")]
    public void ScopeValidationPrecedesActionAndParameterValidation(bool unresolvedQuery, string error)
    {
        using var document = OpenProfile();
        var scope = unresolvedQuery ? new ActionScope { QueryScope = new("foreign", new(), 0) } : new ActionScope(["missing"]);
        var plan = ((IActionPlanningDocument)document).PlanAction("semantic/invalid", new Dictionary<string, FieldValue>(), scope);
        Assert.Equal(error, Assert.Single(plan.Errors).Message);
    }

    [Fact]
    public void PresetKeepsTargetOrderThenSelectedNonTargetsAndReportsOnlyRealChanges()
    {
        using var document = OpenProfile();
        var planner = (IActionPlanningDocument)document;
        var plan = planner.PlanAction("semantic/present-flags", new Dictionary<string, FieldValue> { ["enabled"] = FieldValue.Of(false) },
            new(["value/0", "value/4", "value/3", "value/2"]));
        Assert.Empty(plan.Errors);
        Assert.Equal(SkippedIds, plan.SkippedRows.Select(row => row.RowKey));
        var change = Assert.Single(plan.Changes);
        Assert.Equal("value/2", change.ValueId);
        Assert.Equal(JsonEditingTests.Text("True"), change.OldValue);
        Assert.Equal(JsonEditingTests.Text("False"), change.NewValue);
        Assert.False(change.Derived);
        Assert.Equal(3, plan.Warnings.Count);
        Assert.Equal(Encoding.UTF8.GetBytes(Profile), document.Serialize()[0].Content.ToArray());
    }

    private static ISaveDocument OpenProfile() => new GenericJsonPlugin().Open(
        MemorySaveSet.SingleFile("ue4savegame.dpx.sav", Encoding.UTF8.GetBytes(Profile), "PPSA02343"));
}
