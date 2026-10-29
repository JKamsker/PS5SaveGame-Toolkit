using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.AtomicHeart.Tests;

public sealed class AtomicHeartRestoreTests
{
    internal const string RestoreId = "inventory/restore-imported-amounts";
    private static readonly IReadOnlyDictionary<string, FieldValue> NoParameters = new Dictionary<string, FieldValue>();

    [Fact]
    public void RestoreIsPublishedForDocumentAndRowsWithNoManualTarget()
    {
        using var document = WriteTestData.Open(InventoryFixture.Create());
        var tabs = Assert.IsType<TabsNode>(document.BuildView());
        var inventory = Assert.Single(tabs.Tabs, tab => tab.Title == "Inventory inspection");
        var action = Assert.Single(inventory.Children.OfType<ActionNode>(), node => node.ActionId == RestoreId);
        Assert.Equal("Restore imported quantities (preview)", action.Label);
        Assert.True(action.SupportsPlanning);
        Assert.Empty(action.Parameters);
        Assert.Contains("opening import", action.Description, StringComparison.Ordinal);
        Assert.Contains(InventoryState.WriteWarning, action.Description, StringComparison.Ordinal);
        Assert.All(WriteTestData.Table(document).Rows, row =>
            Assert.Contains(row.Cells.Values.OfType<ActionNode>(), node => node == action));
        Assert.Throws<KeyNotFoundException>(() => document.InvokeAction(RestoreId, NoParameters));
    }

    [Fact]
    public void PreviewUsesEachOpeningAmountAndPreservesEqualAndHigherValues()
    {
        var state = PerformanceInventory.Create(4);
        state.Set(state.Items[0].Quantity, FieldValue.Of(0));
        state.Set(state.Items[1].Quantity, FieldValue.Of(3)); // Above its own opening 2, within container 4.
        state.Set(state.Items[3].Quantity, FieldValue.Of(1));
        var plan = InventoryPlanning.Plan(state, RestoreId, NoParameters,
            new(state.Items.Reverse().Select(item => item.Key).ToArray()));
        Assert.Empty(plan.Errors);
        Assert.Equal([state.Items[0].Key + "/amount", state.Items[3].Key + "/amount"], plan.Changes.Select(c => c.ValueId));
        Assert.Equal([0L, 1L], plan.Changes.Select(c => c.OldValue.AsInt64()));
        Assert.Equal([1L, 4L], plan.Changes.Select(c => c.NewValue.AsInt64()));
        Assert.All(plan.Changes, change => Assert.Contains("Inferred", change.Label, StringComparison.Ordinal));
        Assert.Equal([state.Items[1].Key, state.Items[2].Key], plan.SkippedRows.Select(row => row.RowKey));
        Assert.All(plan.SkippedRows, row => Assert.Contains("opening", row.Reason, StringComparison.Ordinal));
        Assert.Equal(3, state.Current(state.Items[1].Quantity).AsInt64());
        Assert.Contains(InventoryState.WriteWarning, plan.Warnings);
        Assert.Contains(plan.Warnings, warning => warning.Contains("observed maxima", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("IntProperty", (long)int.MaxValue)]
    [InlineData("Int64Property", long.MaxValue)]
    public void RestoreRetainsSignedWidthAndOpeningCeilings(string type, long opening)
    {
        var bytes = InventoryFixture.Create(amountType: type, firstAmount: opening, secondAmount: 1);
        using var document = WriteTestData.Open(bytes);
        var field = WriteTestData.Field(WriteTestData.Table(document).Rows.First(), "amount");
        Assert.True(document.ApplyEdits([new(field.ValueId, FieldValue.Of(0))]).Succeeded);
        var planner = Assert.IsAssignableFrom<IActionPlanningDocument>(document);
        var plan = planner.PlanAction(RestoreId, NoParameters, new());
        Assert.Equal(FieldValue.Of(opening), Assert.Single(plan.Changes).NewValue);
        Assert.True(planner.ApplyActionPlan(RestoreId, NoParameters, new(), plan).Succeeded);
        Assert.Equal(bytes, WriteTestData.Output(document));
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void RestoreNeverTargetsDuplicateContainersOrCollections(bool duplicateContainer, bool duplicateCollection)
    {
        var bytes = InventoryFixture.Create(duplicateContainer, duplicateCollection);
        using var document = WriteTestData.Open(bytes);
        var plan = Assert.IsAssignableFrom<IActionPlanningDocument>(document).PlanAction(RestoreId, NoParameters, new());
        Assert.Empty(plan.Errors);
        Assert.Empty(plan.Changes);
        Assert.Empty(WriteTestData.Table(document).Rows);
        Assert.Equal(bytes, WriteTestData.Output(document));
    }

    [Theory]
    [InlineData(0, null)]
    [InlineData(-1, null)]
    [InlineData(4, "Amount")]
    public void RestoreSkipsOriginallyEmptyNegativeOrUnsupportedAmounts(long opening, string? unsignedField)
    {
        var bytes = InventoryFixture.Create(firstAmount: opening, secondAmount: opening,
            unsignedField: unsignedField, unsignedValue: 4);
        using var document = WriteTestData.Open(bytes);
        var plan = Assert.IsAssignableFrom<IActionPlanningDocument>(document).PlanAction(RestoreId, NoParameters, new());
        Assert.Empty(plan.Errors);
        Assert.Empty(plan.Changes);
        Assert.Equal(2, plan.SkippedRows.Count);
        Assert.All(WriteTestData.Table(document).Rows, row => Assert.True(WriteTestData.Field(row, "amount").Spec.ReadOnly));
        Assert.Equal(bytes, WriteTestData.Output(document));
    }

    [Fact]
    public void RestoreRejectsParametersInvalidSelectionsAndUnresolvedOrForeignQueries()
    {
        var state = PerformanceInventory.Create(2);
        var key = state.Items[0].Key;
        foreach (var scope in new ActionScope[] { new(["missing"]), new([key, key]),
            new() { QueryScope = new(InventoryCollection.Id, new(), 0) },
            new([key]) { QueryScope = new(RecordCollection.Id, new(), 0) } })
            Assert.NotEmpty(InventoryPlanning.Plan(state, RestoreId, NoParameters, scope).Errors);
        Assert.NotEmpty(InventoryPlanning.Plan(state, RestoreId,
            new Dictionary<string, FieldValue> { ["amount"] = FieldValue.Of(1) }, new()).Errors);
        var none = InventoryPlanning.Plan(state, RestoreId, NoParameters, new([]));
        Assert.Empty(none.Errors);
        Assert.Empty(none.Changes);
        Assert.Empty(none.SkippedRows);
    }

    [Fact]
    public void PreviewCanBeDiscardedAndStaleOrTamperedPlansNeverApply()
    {
        using var document = WriteTestData.Open(InventoryFixture.Create());
        var field = WriteTestData.Field(WriteTestData.Table(document).Rows.First(), "amount");
        Assert.True(document.ApplyEdits([new(field.ValueId, FieldValue.Of(0))]).Succeeded);
        var before = WriteTestData.Output(document);
        var planner = Assert.IsAssignableFrom<IActionPlanningDocument>(document);
        var plan = planner.PlanAction(RestoreId, NoParameters, new());
        Assert.Single(plan.Changes);
        // Cancel discards the preview; planning alone must leave all exported bytes intact.
        Assert.Equal(before, WriteTestData.Output(document));
        Assert.False(planner.ApplyActionPlan(RestoreId, NoParameters, new(), plan with { Warnings = [] }).Succeeded);
        var forged = plan with { Changes = [plan.Changes[0] with { NewValue = FieldValue.Of(3) }] };
        Assert.False(planner.ApplyActionPlan(RestoreId, NoParameters, new(), forged).Succeeded);
        Assert.Equal(before, WriteTestData.Output(document));
        Assert.True(document.ApplyEdits([new(field.ValueId, FieldValue.Of(1))]).Succeeded);
        var changed = WriteTestData.Output(document);
        Assert.False(planner.ApplyActionPlan(RestoreId, NoParameters, new(), plan).Succeeded);
        Assert.Equal(changed, WriteTestData.Output(document));
        Assert.Equal(FieldValue.Of(4), Assert.Single(planner.PlanAction(RestoreId, NoParameters, new()).Changes).NewValue);
    }

    [Fact]
    public void ReimportEstablishesANewOpeningBaseline()
    {
        using var original = WriteTestData.Open(InventoryFixture.Create());
        var field = WriteTestData.Field(WriteTestData.Table(original).Rows.First(), "amount");
        Assert.True(original.ApplyEdits([new(field.ValueId, FieldValue.Of(2))]).Succeeded);
        using var imported = WriteTestData.Open(WriteTestData.Output(original));
        var planner = Assert.IsAssignableFrom<IActionPlanningDocument>(imported);
        Assert.Empty(planner.PlanAction(RestoreId, NoParameters, new()).Changes);
        Assert.True(imported.ApplyEdits([new(field.ValueId, FieldValue.Of(0))]).Succeeded);
        Assert.Equal(FieldValue.Of(2), Assert.Single(planner.PlanAction(RestoreId, NoParameters, new()).Changes).NewValue);
        Assert.Equal(FieldValue.Of(4), Assert.Single(Assert.IsAssignableFrom<IActionPlanningDocument>(original)
            .PlanAction(RestoreId, NoParameters, new()).Changes).NewValue);
    }
}
