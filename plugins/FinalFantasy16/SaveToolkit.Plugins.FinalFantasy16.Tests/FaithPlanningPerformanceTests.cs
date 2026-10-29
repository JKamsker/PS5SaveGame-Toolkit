using System.Buffers.Binary;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Values;

namespace SaveToolkit.Plugins.FinalFantasy16.Tests;

public sealed class FaithPlanningPerformanceTests
{
    [Theory]
    [InlineData("empty")]
    [InlineData("unchanged")]
    [InlineData("one")]
    [InlineData("all")]
    public void PreviewAllocationsAreIndependentOfOpaquePayloadSize(string selection)
    {
        var small = Measure(0, selection);
        var large = Measure(4 * 1024 * 1024, selection);
        // Guard growth, not exact equality: tiered BCL paths/hash-table setup can reduce or
        // vary allocations slightly between fixtures. A whole-payload copy adds 4 MiB.
        const int AllocationGrowthAllowance = 64 * 1024;
        Assert.True(large.Allocated < small.Allocated + AllocationGrowthAllowance,
            $"Adding 4 MiB opaque payload changed preview allocations from {small.Allocated:N0} to {large.Allocated:N0} bytes.");
        Assert.Equal(small.Plan.Changes.Select(change => change.ValueId), large.Plan.Changes.Select(change => change.ValueId));
        Assert.Equal(small.Plan.SkippedRows, large.Plan.SkippedRows);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(FaithQuantityPlanning.MaximumTarget)]
    public void PreviewCrcMatchesIndependentPatchesInArbitrarySelectionOrder(int target)
    {
        const int PayloadOffset = FaithContainer.HeaderSize + FaithBlock.HeaderSize;
        const int RecordSize = 16;
        var bytes = FaithPerformanceData.CapacitySave(mapped: true, opaqueBytes: 1024 * 1024);
        // Include a read-only upgrade alias, plus far-apart ranges and adjacent quantities.
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(PayloadOffset + 500 * RecordSize), 506);
        FaithTestData.FixCrc(bytes);
        var original = bytes.ToArray();
        var expected = bytes.ToArray();
        int[] selected = [1199, 0, 500, 1];
        foreach (var slot in selected)
            BinaryPrimitives.WriteInt32LittleEndian(expected.AsSpan(PayloadOffset + slot * RecordSize + sizeof(int)), target);
        FaithTestData.FixCrc(expected);
        var inventory = FaithInventory.Parse(bytes, FaithContainer.Parse(bytes));
        var planner = new FaithQuantityPlanning(bytes, inventory, new FaithCatalog(IReferenceContext.Empty));
        var plan = planner.Plan(FaithQuantityPlanning.ActionId, Parameters(target),
            new ActionScope(selected.Select(slot => $"inventory/{slot}").ToArray()));
        Assert.Empty(plan.Errors);
        Assert.Empty(plan.SkippedRows);
        Assert.Equal(selected.Select(slot => $"inventory/{slot}/quantity"), plan.Changes.Where(change => !change.Derived).Select(change => change.ValueId));
        Assert.All(plan.Changes.Where(change => !change.Derived), change => Assert.Equal(target, change.NewValue.AsInt32()));
        Assert.Contains(plan.Changes, change => change.ValueId == "capacity/506/main/500" && change.Derived);
        var crc = Assert.Single(plan.Changes, change => change.ValueId == "integrity/crc32");
        Assert.True(crc.Derived);
        Assert.Equal(BinaryPrimitives.ReadUInt32LittleEndian(expected.AsSpan(FaithContainer.CrcOffset)), (uint)crc.NewValue.AsInteger());
        Assert.Equal(original, bytes);
    }

    private static (ActionPlan Plan, long Allocated) Measure(int opaqueBytes, string selection)
    {
        var bytes = FaithPerformanceData.CapacitySave(mapped: true, opaqueBytes: opaqueBytes);
        var original = bytes.ToArray();
        var inventory = FaithInventory.Parse(bytes, FaithContainer.Parse(bytes));
        var planner = new FaithQuantityPlanning(bytes, inventory, new FaithCatalog(IReferenceContext.Empty));
        var scope = selection switch
        {
            "empty" => new ActionScope([]), "all" => new ActionScope(), _ => new ActionScope(["inventory/0"]),
        };
        var parameters = Parameters(selection == "unchanged" ? 2 : 1);
        _ = planner.Plan(FaithQuantityPlanning.ActionId, parameters, scope);
        var before = GC.GetAllocatedBytesForCurrentThread();
        var plan = planner.Plan(FaithQuantityPlanning.ActionId, parameters, scope);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Empty(plan.Errors);
        if (selection is "empty" or "unchanged") Assert.Empty(plan.Changes);
        Assert.Equal(original, bytes);
        return (plan, allocated);
    }

    private static Dictionary<string, FieldValue> Parameters(int target) =>
        new() { [FaithQuantityPlanning.QuantityParameter] = FieldValue.Of(target) };
}
