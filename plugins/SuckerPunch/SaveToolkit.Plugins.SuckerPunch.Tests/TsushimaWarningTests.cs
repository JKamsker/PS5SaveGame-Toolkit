using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.SuckerPunch.Tests;

public sealed class TsushimaWarningTests
{
    private const string ConsoleWarning =
        "Not validated on console. The game may reject or overwrite the edited save.";
    private const string ClampWarning = "The game may clamp quantities to its capacity limits.";

    [Theory]
    [InlineData("inventory/set-goods-quantity")]
    [InlineData("inventory/set-quantity")]
    public void QuantityViewsPlansAndOutcomesKeepConsoleRiskAndCapacityQualifications(string actionId)
    {
        using var document = new SyntheticSave(yotei: false).Open();
        var inventory = Assert.IsType<TabsNode>(document.BuildView()).Tabs.Single(tab => tab.Title == "Inventory");
        var introduction = Assert.IsType<TextNode>(inventory.Children[0]);
        Assert.StartsWith(ConsoleWarning, introduction.Text);
        Assert.Contains(ClampWarning, introduction.Text);
        Assert.Contains("only existing unlocked records can change", introduction.Text);
        Assert.Contains("Ammo updates both count copies", introduction.Text);

        var fields = Assert.IsType<TableNode>(inventory.Children[1]).Rows
            .Select(row => Assert.IsType<FieldNode>(row.Cells["quantity"]))
            .Where(field => !field.Spec.ReadOnly).ToArray();
        Assert.NotEmpty(fields);
        Assert.All(fields, field => AssertCapacityWarning(field.Warning));
        var action = inventory.Children.OfType<ActionNode>().Single(node => node.ActionId == actionId);
        Assert.Contains(ConsoleWarning, action.Description);
        var planner = Assert.IsAssignableFrom<IActionPlanningDocument>(document);
        var target = new Dictionary<string, FieldValue> { ["quantity"] = FieldValue.Of(5L) };
        var scope = new ActionScope(["Supplies"]);
        var plan = planner.PlanAction(actionId, target, scope);
        Assert.Empty(plan.Errors);
        Assert.NotEmpty(plan.Changes);
        var warning = Assert.Single(plan.Warnings);
        AssertCapacityWarning(warning);
        Assert.Contains(warning, action.Description);
        Assert.All(fields, field => Assert.Equal(warning, field.Warning));
        var applied = planner.ApplyActionPlan(actionId, target, scope, plan);
        Assert.True(applied.Succeeded);
        Assert.Equal(plan.Warnings, applied.Warnings);
        var scalar = document.ApplyEdits([new("inventory/Supplies/quantity", FieldValue.Of(3L))]);
        Assert.True(scalar.Succeeded);
        Assert.Equal(warning, Assert.Single(scalar.Warnings));
    }

    private static void AssertCapacityWarning(string? warning)
    {
        Assert.StartsWith(ConsoleWarning, warning);
        Assert.Contains(ClampWarning, warning);
        Assert.Contains("Game carrying capacities and console persistence are unverified.", warning);
        Assert.Contains("This sets a target quantity, not a capacity refill.", warning);
        Assert.Contains("Hide retains its raw name because its friendly mapping is unproven.", warning);
    }
}
