using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Values;

namespace SaveToolkit.Plugins.SuckerPunch.Tests;

public sealed class SyntheticPlanningTests
{
    private static Dictionary<string, FieldValue> Target(long value) => new() { ["quantity"] = FieldValue.Of(value) };

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PreviewIsPureAndApplyMatchesExactPrimaryDerivedAndPreservedBytes(bool yotei)
    {
        var save = new SyntheticSave(yotei);
        using var document = save.Open();
        var planner = (IActionPlanningDocument)document;
        var scope = new ActionScope([save.ResourceName, "ArrowNormal", "TestItem"]);
        var plan = planner.PlanAction(save.ActionId, Target(5), scope);
        Assert.Empty(plan.Errors);
        Assert.Equal(new[] { save.ResourceId, save.ArrowId }.Order(StringComparer.Ordinal),
            plan.Changes.Where(change => !change.Derived).Select(change => change.ValueId).Order(StringComparer.Ordinal));
        Assert.Equal(4, plan.Changes.Count);
        Assert.Equal("TestItem", Assert.Single(plan.SkippedRows).RowKey);
        Assert.Contains(plan.Changes, change => change.ValueId == save.ArrowId + "-copy" && change.Derived);
        Assert.Contains(plan.Changes, change => change.ValueId == $"container/word{save.ChecksumOffset / sizeof(uint)}" && change.Derived);
        Assert.Contains(plan.Warnings, warning => warning.Contains("not validated on console", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(save.Bytes, Assert.Single(document.Serialize()).Content.ToArray());
        var outcome = planner.ApplyActionPlan(save.ActionId, Target(5), scope, plan);
        Assert.True(outcome.Succeeded);
        Assert.Equal(plan.Warnings, outcome.Warnings);
        Assert.Equal(plan.Changes.OrderBy(change => change.ValueId, StringComparer.Ordinal)
                .Select(change => (change.ValueId, change.OldValue, change.NewValue)),
            outcome.Changes.OrderBy(change => change.ValueId, StringComparer.Ordinal)
                .Select(change => (change.ValueId, change.OldValue, change.NewValue)));
        WriteAssertions.EditedQuantitiesMatch(save, document, 5);
        var noOp = planner.PlanAction(save.ActionId, Target(5), scope);
        Assert.Empty(noOp.Errors);
        Assert.Empty(noOp.Changes);
        Assert.Equal(3, noOp.SkippedRows.Count);
        var edited = Assert.Single(document.Serialize()).Content.ToArray();
        Assert.True(planner.ApplyActionPlan(save.ActionId, Target(5), scope, noOp).Succeeded);
        Assert.Equal(edited, Assert.Single(document.Serialize()).Content.ToArray());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InvalidRequestsEmptySelectionAndForgedOrStalePlansCannotWidenOrMutate(bool yotei)
    {
        var save = new SyntheticSave(yotei);
        using var document = save.Open();
        var planner = (IActionPlanningDocument)document;
        var emptyScope = new ActionScope([]);
        var empty = planner.PlanAction(save.ActionId, Target(5), emptyScope);
        Assert.Empty(empty.Errors);
        Assert.Empty(empty.Changes);
        Assert.True(planner.ApplyActionPlan(save.ActionId, Target(5), emptyScope, empty).Succeeded);
        Assert.NotEmpty(planner.PlanAction(save.ActionId, Target(5), new()).Changes);
        ActionScope[] invalidScopes = [new(["unknown"]), new()
            { QueryScope = new ActionQueryScope("inventory", new QueryableCollectionQuery(), 0) }];
        foreach (var scope in invalidScopes)
        {
            var plan = planner.PlanAction(save.ActionId, Target(5), scope);
            Assert.NotEmpty(plan.Errors);
            Assert.False(planner.ApplyActionPlan(save.ActionId, Target(5), scope, plan).Succeeded);
            Assert.Equal(save.Bytes, Assert.Single(document.Serialize()).Content.ToArray());
        }
        Dictionary<string, FieldValue>[] invalidParameters = [[], Target(-1), Target(yotei ? 112_100 : 32768),
            new() { ["quantity"] = FieldValue.Null }, new() { ["quantity"] = FieldValue.Of("5") },
            new() { ["quantity"] = FieldValue.Of(5L), ["unlock"] = FieldValue.Of(true) }];
        foreach (var parameters in invalidParameters)
        {
            var invalid = planner.PlanAction(save.ActionId, parameters, new());
            Assert.NotEmpty(invalid.Errors);
            Assert.False(planner.ApplyActionPlan(save.ActionId, parameters, new(), invalid).Succeeded);
            Assert.Equal(save.Bytes, Assert.Single(document.Serialize()).Content.ToArray());
        }
        var selected = new ActionScope([save.ResourceName, "ArrowNormal"]);
        var valid = planner.PlanAction(save.ActionId, Target(5), selected);
        Assert.Empty(valid.Errors);
        Assert.False(planner.ApplyActionPlan(save.ActionId, Target(5), selected, valid with { Warnings = [] }).Succeeded);
        Assert.False(planner.ApplyActionPlan(save.ActionId, Target(5), selected, valid with { Changes = [] }).Succeeded);
        Assert.Equal(save.Bytes, Assert.Single(document.Serialize()).Content.ToArray());
        Assert.True(document.ApplyEdits([new(save.ResourceId, FieldValue.Of(3L))]).Succeeded);
        var before = Assert.Single(document.Serialize()).Content.ToArray();
        Assert.False(planner.ApplyActionPlan(save.ActionId, Target(5), selected, valid).Succeeded);
        Assert.Equal(before, Assert.Single(document.Serialize()).Content.ToArray());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LockedInconsistentAndUnmappedRowsAreSkippedWhileEligibleRowsApply(bool yotei)
    {
        var save = new SyntheticSave(yotei);
        save.Write(save.Inventory[save.ResourceName], 0);
        save.Write(save.Inventory["ArrowNormal"] + save.QuantityRelativeOffset + sizeof(uint), 3);
        save.Repair();
        using var document = save.Open();
        var planner = (IActionPlanningDocument)document;
        var selected = new ActionScope([save.ResourceName, "ArrowNormal", "TestItem", "Flowers"]);
        var plan = planner.PlanAction(save.ActionId, Target(5), selected);
        Assert.Empty(plan.Errors);
        Assert.Equal(new[] { save.ResourceName, "ArrowNormal", "TestItem" }.Order(StringComparer.Ordinal),
            plan.SkippedRows.Select(row => row.RowKey).Order(StringComparer.Ordinal));
        Assert.Equal(save.Prefix + "Flowers/quantity", Assert.Single(plan.Changes, change => !change.Derived).ValueId);
        Assert.Equal(save.Bytes, Assert.Single(document.Serialize()).Content.ToArray());
        Assert.True(planner.ApplyActionPlan(save.ActionId, Target(5), selected, plan).Succeeded);
        var output = Assert.Single(document.Serialize()).Content.ToArray();
        Assert.Equal(5U, document.GetValue(save.Prefix + "Flowers/quantity").AsUInt32());
        Assert.Equal(SyntheticSave.InitialQuantity, document.GetValue(save.ResourceId).AsUInt32());
        Assert.Equal(SyntheticSave.InitialQuantity, document.GetValue(save.ArrowId).AsUInt32());
        Assert.Equal(3U, document.GetValue(save.ArrowId + "-copy").AsUInt32());
        var flowers = save.Inventory["Flowers"] + save.QuantityRelativeOffset;
        WriteAssertions.OnlyRangesChanged(save.Bytes, output, [(save.ChecksumOffset, sizeof(uint)), (flowers, sizeof(uint))]);
        Assert.Equal(yotei ? YoteiTestData.Checksum(output) : SuckerPunchTestData.Checksum(output),
            SuckerPunchTestData.Word(output, save.ChecksumOffset));
    }
}
