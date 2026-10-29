using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Values;

namespace SaveToolkit.Plugins.SuckerPunch.Tests;

public sealed class PlanningCharacterizationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PlansRetainTitleSpecificPrimaryMirrorAndChecksumOrder(bool yotei)
    {
        var save = new SyntheticSave(yotei);
        using var document = save.Open();
        var plan = ((IActionPlanningDocument)document).PlanAction(save.ActionId,
            new Dictionary<string, FieldValue> { ["quantity"] = FieldValue.Of(1L) },
            new(["Flowers", save.ResourceName, "ArrowNormal"]));
        Assert.Empty(plan.Errors);
        string[] expected = yotei
            ? [save.ArrowId, save.ArrowId + "-copy", save.ResourceId, save.Prefix + "Flowers/quantity", "container/word3"]
            : [save.ArrowId, save.ResourceId, save.Prefix + "Flowers/quantity", save.ArrowId + "-copy", "container/word2"];
        Assert.Equal(expected, plan.Changes.Select(change => change.ValueId));
        bool[] expectedDerived = yotei ? [false, true, false, false, true] : [false, false, false, true, true];
        Assert.Equal(expectedDerived, plan.Changes.Select(change => change.Derived));
        Assert.Empty(plan.SkippedRows);
        Assert.Single(plan.Warnings);
        Assert.Equal(save.Bytes, Assert.Single(document.Serialize()).Content.ToArray());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PlanValidationRetainsParameterScopeAndSelectionPrecedence(bool yotei)
    {
        var save = new SyntheticSave(yotei);
        using var document = save.Open();
        var planner = (IActionPlanningDocument)document;
        var scope = new ActionScope(["missing"]) { QueryScope = new("gear", new QueryableCollectionQuery(), 0) };
        var parameters = new Dictionary<string, FieldValue> { ["quantity"] = FieldValue.Null };
        Assert.Equal(yotei ? "Supply exactly one integer quantity between 0 and 112099." :
            "Supply exactly one integer target quantity between 0 and 32767.",
            Assert.Single(planner.PlanAction(save.ActionId, parameters, scope).Errors).Message);
        parameters["quantity"] = FieldValue.Of(1L);
        Assert.Equal(yotei ? "The small inventory action does not accept query scopes; gear registries are read-only." :
            "This bounded inventory action does not accept query scopes.",
            Assert.Single(planner.PlanAction(save.ActionId, parameters, scope).Errors).Message);
        Assert.Equal(yotei ? "Selection contains an unknown Yōtei inventory row." : "Selection contains an unknown inventory row.",
            Assert.Single(planner.PlanAction(save.ActionId, parameters, new(["missing"])).Errors).Message);
        Assert.Equal(save.Bytes, Assert.Single(document.Serialize()).Content.ToArray());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EveryComparedPlanComponentRejectsForgeryWithoutChangingBytes(bool yotei)
    {
        var save = new SyntheticSave(yotei);
        using var document = save.Open();
        var planner = (IActionPlanningDocument)document;
        var scope = new ActionScope([save.ResourceName, "ArrowNormal", "TestItem", SyntheticSave.FirstUnknown]);
        var parameters = new Dictionary<string, FieldValue> { ["quantity"] = FieldValue.Of(1L) };
        var plan = planner.PlanAction(save.ActionId, parameters, scope);
        Assert.Empty(plan.Errors);
        Assert.Equal(2, plan.SkippedRows.Count);
        var first = plan.Changes[0];
        ActionPlan[] forgeries =
        [
            plan with { Errors = [new("quantity", "forged")] },
            plan with { Changes = plan.Changes.Reverse().ToArray() },
            plan with { Changes = [first with { Label = "forged" }, .. plan.Changes.Skip(1)] },
            plan with { Changes = [first with { Derived = true }, .. plan.Changes.Skip(1)] },
            plan with { Changes = [first with { OldValue = FieldValue.Of(999L) }, .. plan.Changes.Skip(1)] },
            plan with { Changes = [first with { NewValue = FieldValue.Of(999L) }, .. plan.Changes.Skip(1)] },
            plan with { SkippedRows = plan.SkippedRows.Reverse().ToArray() },
            plan with { Warnings = ["forged"] },
        ];
        foreach (var forged in forgeries)
        {
            Assert.False(planner.ApplyActionPlan(save.ActionId, parameters, scope, forged).Succeeded);
            Assert.Equal(save.Bytes, Assert.Single(document.Serialize()).Content.ToArray());
        }
        Assert.True(planner.ApplyActionPlan(save.ActionId, parameters, scope, plan).Succeeded);
        WriteAssertions.EditedQuantitiesMatch(save, document, 1);
    }
}
