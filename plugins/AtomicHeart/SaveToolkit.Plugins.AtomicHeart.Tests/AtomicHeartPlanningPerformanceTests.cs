using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.AtomicHeart.Tests;

[Collection(typeof(AllocationTestCollection))]
public sealed class AtomicHeartPlanningPerformanceTests
{
    private static readonly int[] ExpectedOrdinals = [2, 30, 449];
    private static readonly IReadOnlyDictionary<string, FieldValue> Parameters =
        new Dictionary<string, FieldValue> { ["amount"] = FieldValue.Of(0) };

    [Fact]
    public void SingleSelectedPlanDoesNotAllocateForUnselectedInventory()
    {
        var state = PerformanceInventory.Create(10000);
        var scope = new ActionScope([state.Items[^1].Key]);
        _ = InventoryPlanning.Plan(state, InventoryPlanning.ActionId, Parameters, scope);
        var allocated = AllocationProbe.Measure(() => InventoryPlanning.Plan(state, InventoryPlanning.ActionId, Parameters, scope), out var plan);
        Assert.Single(plan.Changes);
        allocated.AssertLessThan(10_000, $"Single selected preview allocated {allocated.MinimumBytes:N0} bytes.");
    }

    [Fact]
    public void SelectionRetainsNativeOrderAndRejectsDuplicateOrUnknownKeys()
    {
        var state = PerformanceInventory.Create(450);
        var selected = new[] { state.Items[449].Key, state.Items[2].Key, state.Items[30].Key };
        var plan = InventoryPlanning.Plan(state, InventoryPlanning.ActionId, Parameters, new(selected));
        Assert.Equal(ExpectedOrdinals.Select(i => state.Items[i].Key + "/amount"), plan.Changes.Select(c => c.ValueId));
        Assert.Equal([3L, 31L, 450L], plan.Changes.Select(c => c.OldValue.AsInt64()));
        Assert.All(plan.Changes, c => Assert.Equal(FieldValue.Of(0), c.NewValue));
        Assert.Empty(state.Edits());
        foreach (var keys in new[] { new[] { selected[0], selected[0] }, new[] { selected[0], "unknown" } })
        {
            var rejected = InventoryPlanning.Plan(state, InventoryPlanning.ActionId, Parameters, new(keys));
            Assert.Equal("Selection must contain distinct existing inventory row keys.", Assert.Single(rejected.Errors).Message);
            Assert.Empty(rejected.Changes);
            Assert.Empty(rejected.SkippedRows);
        }
        var empty = InventoryPlanning.Plan(state, InventoryPlanning.ActionId, Parameters, new([]));
        Assert.Empty(empty.Changes);
        Assert.Empty(empty.SkippedRows);
        state.Set(state.Items[30].Quantity, FieldValue.Of(0));
        var fresh = InventoryPlanning.Plan(state, InventoryPlanning.ActionId, Parameters, new(selected));
        Assert.Equal([state.Items[2].Key + "/amount", state.Items[449].Key + "/amount"], fresh.Changes.Select(c => c.ValueId));
        Assert.Equal(state.Items[30].Key, Assert.Single(fresh.SkippedRows).RowKey);
    }
}
