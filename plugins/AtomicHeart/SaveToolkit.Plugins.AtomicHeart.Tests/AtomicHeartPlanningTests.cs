using System.Text.Json;
using SaveToolkit.Core.References;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Core.Storage;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;

namespace SaveToolkit.Plugins.AtomicHeart.Tests;

public sealed class AtomicHeartPlanningTests
{
    [Fact]
    public void BulkPreviewIsPureBoundedAndIncludesWarningsAndSkippedRows()
    {
        var file = WriteTestData.Sample();
        using var document = WriteTestData.Plugin.Open(file);
        var planner = Assert.IsAssignableFrom<IActionPlanningDocument>(document);
        var plan = planner.PlanAction(InventoryPlanning.ActionId, Parameters(1), new());
        Assert.Empty(plan.Errors);
        Assert.NotEmpty(plan.Changes);
        Assert.NotEmpty(plan.SkippedRows);
        Assert.All(plan.Changes, change => Assert.EndsWith("/amount", change.ValueId, StringComparison.Ordinal));
        Assert.Contains(plan.Warnings, w => w.Contains("game may reject", StringComparison.Ordinal));
        Assert.Equal(file.ReadAllBytes(), WriteTestData.Output(document));
        var none = planner.PlanAction(InventoryPlanning.ActionId, Parameters(1), new([]));
        Assert.Empty(none.Changes);
        Assert.Empty(none.SkippedRows);
        Assert.NotEmpty(planner.PlanAction(InventoryPlanning.ActionId, Parameters(-1), new()).Errors);
        Assert.NotEmpty(planner.PlanAction(InventoryPlanning.ActionId, Parameters(1), new(["unknown"])).Errors);
        var key = WriteTestData.Table(document).Rows[0].Key;
        Assert.NotEmpty(planner.PlanAction(InventoryPlanning.ActionId, Parameters(1), new([key, key])).Errors);
        var over = planner.PlanAction(InventoryPlanning.ActionId, Parameters(long.MaxValue), new());
        Assert.Empty(over.Changes);
        Assert.NotEmpty(over.SkippedRows);
        Assert.False(planner.ApplyActionPlan(InventoryPlanning.ActionId, Parameters(0), new(), plan).Succeeded);
        Assert.Equal(file.ReadAllBytes(), WriteTestData.Output(document));
    }

    [Fact]
    public void SelectedBulkApplyPersistsAndUndoesAsOneGroupThenRedoes()
    {
        var file = Assert.IsType<DiskSaveFile>(WriteTestData.Sample());
        var context = Assert.IsType<ReferenceContext>(WriteTestData.References());
        var document = WriteTestData.Plugin.Open(file, context);
        using var session = new DocumentSession("atomic-bulk", file, WriteTestData.Plugin, document, references: context);
        _ = session.BuildView();
        _ = session.Expand("inventory/0");
        var rows = WriteTestData.Table(document).Rows.Where(r => !WriteTestData.Field(r, "amount").Spec.ReadOnly).Take(3).ToArray();
        Assert.Equal(3, rows.Length);
        var selected = new ActionScope(rows.Select(r => r.Key).ToArray());
        Assert.False(session.InvokeAction(InventoryPlanning.ActionId, new Dictionary<string, JsonElement> { ["amount"] = JsonSerializer.SerializeToElement(1) }).Ok);
        var preview = session.PlanAction(InventoryPlanning.ActionId,
            new Dictionary<string, JsonElement> { ["amount"] = JsonSerializer.SerializeToElement(0) }, selected, session.State.Revision);
        Assert.NotNull(preview.PlanId);
        Assert.Equal(3, preview.Plan.Changes.Count);
        Assert.True(session.ApplyActionPlan(preview.PlanId!, session.State.Revision).Ok);
        var edited = WriteTestData.Output(document);
        _ = WriteTestData.IndependentDecode(file.ReadAllBytes(), edited);
        using (var reopened = WriteTestData.Open(edited))
            foreach (var change in preview.Plan.Changes) Assert.Equal(change.NewValue, reopened.GetValue(change.ValueId));
        Assert.True(session.Undo().Ok);
        Assert.False(session.State.CanUndo);
        Assert.Equal(file.ReadAllBytes(), WriteTestData.Output(document));
        Assert.True(session.Redo().Ok);
        Assert.Equal(edited, WriteTestData.Output(document));
        Assert.False(session.ApplyActionPlan(preview.PlanId!, session.State.Revision).Ok);
    }

    [Theory]
    [InlineData("amount")]
    [InlineData("asset")]
    public void ScalarSessionUndoRedoRestoresExactOriginalAndRejectsStalePreview(string column)
    {
        var file = Assert.IsType<DiskSaveFile>(WriteTestData.Sample());
        var context = Assert.IsType<ReferenceContext>(WriteTestData.References());
        var document = WriteTestData.Plugin.Open(file, context);
        using var session = new DocumentSession("atomic-scalar", file, WriteTestData.Plugin, document, references: context);
        _ = session.BuildView();
        _ = session.Expand("inventory/0");
        var row = WriteTestData.Table(document).Rows.First(r => column == "asset"
            ? WriteTestData.Field(r, column).Value.AsString() == "DA_Item_Shved"
            : !WriteTestData.Field(r, column).Spec.ReadOnly);
        var field = WriteTestData.Field(row, column);
        var next = column == "asset" ? FieldValue.Of("DA_Item_ShotgunKS23") : FieldValue.Of(0);
        var preview = session.PlanAction(InventoryPlanning.ActionId,
            new Dictionary<string, JsonElement> { ["amount"] = JsonSerializer.SerializeToElement(1) });
        Assert.NotNull(preview.PlanId);
        Assert.True(session.ApplyEdits([new(field.ValueId, JsonSerializer.SerializeToElement(next))], session.State.Revision).Ok);
        Assert.False(session.ApplyActionPlan(preview.PlanId!, preview.Revision).Ok);
        var output = WriteTestData.Output(document);
        using (var reopened = WriteTestData.Open(output)) Assert.Equal(next, reopened.GetValue(field.ValueId));
        Assert.True(session.Undo().Ok);
        Assert.Equal(field.Value, document.GetValue(field.ValueId));
        Assert.Equal(file.ReadAllBytes(), WriteTestData.Output(document));
        Assert.True(session.Redo().Ok);
        Assert.Equal(next, document.GetValue(field.ValueId));
        Assert.Equal(output, WriteTestData.Output(document));
    }

    private static Dictionary<string, FieldValue> Parameters(long amount) => new() { ["amount"] = FieldValue.Of(amount) };
}
