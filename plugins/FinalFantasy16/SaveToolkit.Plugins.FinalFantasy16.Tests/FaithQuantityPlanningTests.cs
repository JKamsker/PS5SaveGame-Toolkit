using System.Text.Json;
using SaveToolkit.Core.References;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;

namespace SaveToolkit.Plugins.FinalFantasy16.Tests;

public sealed class FaithQuantityPlanningTests
{
    private readonly FinalFantasy16Plugin _plugin = new();
    private static Dictionary<string, FieldValue> Parameters(int target) => new() { [FaithQuantityPlanning.QuantityParameter] = FieldValue.Of(target) };
    private static Dictionary<string, JsonElement> Incoming(int target) => new()
    {
        [FaithQuantityPlanning.QuantityParameter] = JsonSerializer.SerializeToElement(target),
    };

    [Theory]
    [InlineData("PPSA10665")]
    [InlineData("PPSA13803")]
    public void PlannedSelectedStacksArePureAndPersistWithOneSessionUndoRedo(string title)
    {
        var file = FaithWriteTests.SampleFile(title);
        var bytes = file.ReadAllBytes();
        var inventory = FaithInventory.Parse(bytes, FaithContainer.Parse(bytes));
        var references = new ReferencePackStore([_plugin]).Pin(_plugin, title);
        using var document = _plugin.Open(file, references);
        using var session = new DocumentSession("ff16-plan", file, _plugin, document, references: references);
        _ = session.BuildView();
        var planner = Assert.IsAssignableFrom<IActionPlanningDocument>(document);
        var scope = new ActionScope(["inventory/0", "inventory/1"]);
        var plan = planner.PlanAction(FaithQuantityPlanning.ActionId, Parameters(1), scope);
        Assert.Empty(plan.Errors);
        Assert.Equal(2, plan.Changes.Count(change => !change.Derived));
        Assert.Contains(plan.Changes, change => change.ValueId == "integrity/crc32" && change.Derived);
        Assert.Contains(plan.Warnings, warning => warning.Contains("Not validated on console", StringComparison.Ordinal));
        Assert.Equal(bytes, Assert.Single(document.Serialize()).Content.ToArray());
        Assert.False(session.InvokeAction(FaithQuantityPlanning.ActionId, Incoming(1)).Ok);
        var preview = session.PlanAction(FaithQuantityPlanning.ActionId, Incoming(1), scope);
        Assert.NotNull(preview.PlanId);
        Assert.Equal(plan.Changes, preview.Plan.Changes);
        Assert.True(session.ApplyActionPlan(preview.PlanId, preview.Revision).Ok);
        var output = Assert.Single(document.Serialize()).Content.ToArray();
        FaithWriteTests.AssertOnlyRanges(bytes, output, inventory.Items.Take(2).Select(item => (item.Offset + sizeof(int), sizeof(int))).ToArray());
        using var reopened = _plugin.Open(FaithSamples.WithBytes(file, output), references);
        _ = reopened.BuildView();
        Assert.Equal(1, reopened.GetValue("inventory/0/quantity").AsInt32());
        Assert.Equal(1, reopened.GetValue("inventory/1/quantity").AsInt32());
        Assert.Equal(plan.Changes.Single(change => change.Derived).NewValue, reopened.GetValue("integrity/crc32"));
        Assert.True(session.Undo().Ok);
        Assert.Equal(bytes, Assert.Single(document.Serialize()).Content.ToArray());
        Assert.False(session.State.CanUndo);
        Assert.True(session.Redo().Ok);
        Assert.Equal(output, Assert.Single(document.Serialize()).Content.ToArray());
        Assert.True(session.ApplyActionPlan(preview.PlanId, preview.Revision).Stale);
    }

    [Fact]
    public void EmptySelectionNeverMeansAllAndSkippedRowsExplainNoChange()
    {
        var bytes = FaithTestData.Container((FaithSchema.ItemType, FaithSchema.ItemVersion, FaithTestData.Items()));
        using var document = _plugin.Open(FaithTestData.File(bytes));
        _ = document.BuildView();
        var planner = Assert.IsAssignableFrom<IActionPlanningDocument>(document);
        var empty = planner.PlanAction(FaithQuantityPlanning.ActionId, Parameters(0), new ActionScope([]));
        Assert.Empty(empty.Changes);
        Assert.Empty(empty.SkippedRows);
        var same = planner.PlanAction(FaithQuantityPlanning.ActionId, Parameters(1234), new ActionScope(["inventory/0"]));
        Assert.Empty(same.Changes);
        Assert.Contains("Already", Assert.Single(same.SkippedRows).Reason);
        var all = planner.PlanAction(FaithQuantityPlanning.ActionId, Parameters(0), new ActionScope());
        Assert.Equal(2, all.Changes.Count(change => !change.Derived));
        Assert.True(planner.ApplyActionPlan(FaithQuantityPlanning.ActionId, Parameters(0), new ActionScope([]), empty).Succeeded);
        Assert.Equal(bytes, Assert.Single(document.Serialize()).Content.ToArray());
    }

    [Fact]
    public void BadTargetsScopesAndForgedPlansNeverMutate()
    {
        var bytes = FaithTestData.Container((FaithSchema.ItemType, FaithSchema.ItemVersion, FaithTestData.Items()));
        using var document = _plugin.Open(FaithTestData.File(bytes));
        _ = document.BuildView();
        var planner = Assert.IsAssignableFrom<IActionPlanningDocument>(document);
        foreach (var target in new[] { -1, FaithQuantityPlanning.MaximumTarget + 1 })
            Assert.NotEmpty(planner.PlanAction(FaithQuantityPlanning.ActionId, Parameters(target), new ActionScope()).Errors);
        foreach (var keys in new[] { new[] { "secondary/0" }, new[] { "inventory/999" }, new[] { "inventory/0", "inventory/0" } })
            Assert.NotEmpty(planner.PlanAction(FaithQuantityPlanning.ActionId, Parameters(0), new ActionScope(keys)).Errors);
        var plan = planner.PlanAction(FaithQuantityPlanning.ActionId, Parameters(0), new ActionScope());
        var forged = plan with { Changes = [new("inventory/0/item", "Forged identity", FieldValue.Of("1"), FieldValue.Of("5"))] };
        Assert.False(planner.ApplyActionPlan(FaithQuantityPlanning.ActionId, Parameters(0), new ActionScope(), forged).Succeeded);
        Assert.Equal(bytes, Assert.Single(document.Serialize()).Content.ToArray());
        Assert.True(document.ApplyEdits([new("inventory/0/quantity", FieldValue.Of(2))]).Succeeded);
        var changed = Assert.Single(document.Serialize()).Content.ToArray();
        Assert.False(planner.ApplyActionPlan(FaithQuantityPlanning.ActionId, Parameters(0), new ActionScope(), plan).Succeeded);
        Assert.Equal(changed, Assert.Single(document.Serialize()).Content.ToArray());
    }

    [Fact]
    public void NegativeStacksAreListedAsSkippedAndUnknownSchemasCannotPlan()
    {
        var payload = FaithTestData.Items();
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(sizeof(int)), -1);
        using var document = _plugin.Open(FaithTestData.File(FaithTestData.Container((FaithSchema.ItemType, FaithSchema.ItemVersion, payload))));
        _ = document.BuildView();
        var plan = ((IActionPlanningDocument)document).PlanAction(FaithQuantityPlanning.ActionId, Parameters(0), new ActionScope());
        Assert.Contains(plan.SkippedRows, row => row.RowKey == "inventory/0" && row.Reason.Contains("Negative", StringComparison.Ordinal));
        Assert.DoesNotContain(plan.Changes, change => change.ValueId == "inventory/0/quantity");
        using var unsupported = _plugin.Open(FaithTestData.File(FaithTestData.Container((FaithSchema.ItemType, FaithSchema.ItemVersion + 1, payload))));
        Assert.NotEmpty(((IActionPlanningDocument)unsupported).PlanAction(FaithQuantityPlanning.ActionId, Parameters(0), new ActionScope()).Errors);
    }
}
