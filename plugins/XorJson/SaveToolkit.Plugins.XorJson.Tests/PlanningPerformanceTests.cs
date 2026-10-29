using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;

namespace SaveToolkit.Plugins.XorJson.Tests;

[Collection(AllocationMeasurements.Name)]
public sealed class PlanningPerformanceTests
{
    private const int RowCount = 10_000;
    private const int SelectionBudget = 32 * 1024;
    private static readonly Dictionary<string, FieldValue> Parameters = new() { ["value"] = FieldValue.Of("42.000E+00") };

    [Fact]
    public void SingleRowPreviewAllocatesForTheSelectionIncludingItsFirstLookup()
    {
        var bytes = XorJsonTests.Encode("{\"rows\":[" + string.Join(',', Enumerable.Repeat("0", RowCount)) + "]}");
        // Warm parser/planner code on another document, never the measured document's lookup cache.
        using (var warmup = XorJsonTests.Open(bytes))
            _ = ((IActionPlanningDocument)warmup).PlanAction("json/bulk-set", Parameters, new ActionScope(["value/2"]));
        using var document = XorJsonTests.Open(bytes);
        var planner = Assert.IsAssignableFrom<IActionPlanningDocument>(document);
        var scope = new ActionScope(["value/10001"]);
        var before = GC.GetAllocatedBytesForCurrentThread();
        var plan = planner.PlanAction("json/bulk-set", Parameters, scope);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.InRange(allocated, 1, SelectionBudget);
        Assert.Empty(plan.Errors);
        var change = Assert.Single(plan.Changes);
        Assert.Equal("value/10001", change.ValueId);
        Assert.Equal("0", change.OldValue.AsExactNumber());
        Assert.Equal("42.000E+00", change.NewValue.AsExactNumber());
        Assert.Equal(bytes, document.Serialize().Single().Content.ToArray());
    }

    [Fact]
    public void ExplicitSelectionKeepsCanonicalKeysOrderDuplicatesAndRetiredIds()
    {
        var bytes = XorJsonTests.Encode("{\"rows\":[0,1,2]}");
        using var document = XorJsonTests.Open(bytes);
        var planner = Assert.IsAssignableFrom<IActionPlanningDocument>(document);
        var scope = new ActionScope(["value/4", "value/2", "value/4", "value/1", "value/0002", "missing", "value/99"]);
        var plan = planner.PlanAction("json/bulk-set", Parameters, scope);
        Assert.Empty(plan.Errors);
        Assert.Equal(["value/4", "value/2"], plan.Changes.Select(change => change.ValueId));
        Assert.Equal(["value/1", "value/0002", "missing", "value/99"], plan.SkippedRows.Select(row => row.RowKey));
        Assert.Empty(planner.PlanAction("json/bulk-set", Parameters, new ActionScope([])).Changes);
        Assert.Equal(3, planner.PlanAction("json/bulk-set", Parameters, new ActionScope()).Changes.Count);

        var removal = document.InvokeAction("json/remove/4", new Dictionary<string, FieldValue>());
        var stale = planner.PlanAction("json/bulk-set", Parameters, scope);
        Assert.Equal("value/2", Assert.Single(stale.Changes).ValueId);
        Assert.Contains(stale.SkippedRows, row => row.RowKey == "value/4");
        Assert.False(planner.ApplyActionPlan("json/bulk-set", Parameters, scope, plan).Succeeded);
        _ = document.RestoreMemento(removal.Revert!);
        Assert.Equal(plan.Changes, planner.PlanAction("json/bulk-set", Parameters, scope).Changes);
        Assert.Equal(bytes, document.Serialize().Single().Content.ToArray());
    }
}

