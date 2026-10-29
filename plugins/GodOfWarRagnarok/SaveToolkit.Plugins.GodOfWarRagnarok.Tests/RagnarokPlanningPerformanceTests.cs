using System.Buffers.Binary;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.GodOfWarRagnarok.Tests;

[Collection(typeof(AllocationTestCollection))]
public sealed class RagnarokPlanningPerformanceTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SmallSelectionsDoNotMaterializeAllCounterIdentities(bool empty)
    {
        using var document = RagnarokPerformanceFixture.Open();
        var scope = new ActionScope(empty ? [] : [RowKey(4)]);
        var parameters = new Dictionary<string, FieldValue>();
        _ = document.PlanAction(RagnarokWrites.RestoreActionId, parameters, scope);
        var allocated = AllocationProbe.Measure(() => document.PlanAction(RagnarokWrites.RestoreActionId, parameters, scope), out var plan);
        Assert.Empty(plan.Errors);
        Assert.Empty(plan.Changes);
        Assert.Equal(empty ? 0 : 1, plan.SkippedRows.Count);
        allocated.AssertInRange(0, 4096);
    }

    [Fact]
    public void ApplyingOneUnloadedTargetBindsOnlyThatTargetAndPreservesEveryOtherByte()
    {
        var bytes = RagnarokPerformanceFixture.Bytes();
        var index = RagnarokRecordIndex.Parse(bytes, false);
        using var document = new RagnarokDocument("GOWRSAVE0", "GOWRSAVE0", bytes, index, new(IReferenceContext.Empty));
        const int row = 4;
        const int offset = RagnarokPerformanceFixture.RowsOffset + row * 16 + 8;
        // Simulate an earlier reduction without publishing any field binding. This is an in-memory
        // test seam; plugins still own their buffers and callers never mutate them in production.
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), 0);
        var scope = new ActionScope([RowKey(row)]);
        var parameters = new Dictionary<string, FieldValue>();
        var plan = document.PlanAction(RagnarokWrites.RestoreActionId, parameters, scope);
        var change = Assert.Single(plan.Changes);
        Assert.Equal(FieldValue.Of(0L), change.OldValue);
        Assert.Equal(FieldValue.Of((long)uint.MaxValue), change.NewValue);
        Assert.Throws<KeyNotFoundException>(() => document.GetValue(change.ValueId));

        RagnarokDocument measuredDocument = null!;
        var allocated = AllocationProbe.Measure(() => measuredDocument.ApplyActionPlan(
            RagnarokWrites.RestoreActionId, parameters, scope, plan), out var outcome, beforeRun: () =>
        {
            measuredDocument?.Dispose();
            var freshBytes = RagnarokPerformanceFixture.Bytes();
            measuredDocument = new RagnarokDocument("GOWRSAVE0", "GOWRSAVE0", freshBytes,
                RagnarokRecordIndex.Parse(freshBytes, false), new(IReferenceContext.Empty));
            BinaryPrimitives.WriteUInt32LittleEndian(freshBytes.AsSpan(offset), 0);
            plan = measuredDocument.PlanAction(RagnarokWrites.RestoreActionId, parameters, scope);
        });
        using var applied = measuredDocument;
        Assert.True(outcome.Succeeded);
        Assert.Equal(change.NewValue, applied.GetValue(change.ValueId));
        Assert.Single(outcome.Changes);
        Assert.Contains(RagnarokWrites.IntegrityWarning, outcome.Warnings);
        allocated.AssertInRange(0, 12_000);
        Assert.Throws<KeyNotFoundException>(() => applied.GetValue(RowKey(1) + "/quantity"));
        var serialized = Assert.Single(applied.Serialize()).Content.ToArray();
        Assert.Equal(RagnarokPerformanceFixture.Bytes(), serialized);
        using var reopened = (RagnarokDocument)new GodOfWarRagnarokPlugin().Open(MemorySaveSet.SingleFile("GOWRSAVE0", serialized, "PPSA08330"));
        Assert.Equal(uint.MaxValue, reopened.CounterField(offset).Value.AsUInt32());
    }

    [Fact]
    public void SelectionRetainsNativeOrderAndDuplicateUnknownAndStaleGuards()
    {
        using var document = RagnarokPerformanceFixture.Open();
        var parameters = new Dictionary<string, FieldValue>();
        var first = document.CounterField(RagnarokPerformanceFixture.RowsOffset + 4 * 16 + 8);
        var second = document.CounterField(RagnarokPerformanceFixture.RowsOffset + 9 * 16 + 8);
        Assert.True(document.ApplyEdits([new(first.ValueId, FieldValue.Of(1L)), new(second.ValueId, FieldValue.Of(2L))]).Succeeded);
        var scope = new ActionScope([RowKey(9), RowKey(4), RowKey(0)]);
        var plan = document.PlanAction(RagnarokWrites.RestoreActionId, parameters, scope);
        Assert.Equal(new[] { first.ValueId, second.ValueId }, plan.Changes.Select(change => change.ValueId));
        Assert.Equal(RowKey(0), Assert.Single(plan.SkippedRows).RowKey);
        Assert.Empty(plan.Errors);
        Assert.NotEmpty(document.PlanAction(RagnarokWrites.RestoreActionId, parameters, new([RowKey(4), RowKey(4)])).Errors);
        Assert.NotEmpty(document.PlanAction(RagnarokWrites.RestoreActionId, parameters, new(["missing"])).Errors);
        Assert.True(document.ApplyEdits([new(first.ValueId, FieldValue.Of(3L))]).Succeeded);
        Assert.False(document.ApplyActionPlan(RagnarokWrites.RestoreActionId, parameters, scope, plan).Succeeded);
        Assert.Equal(FieldValue.Of(3L), document.GetValue(first.ValueId));
        Assert.Equal(FieldValue.Of(2L), document.GetValue(second.ValueId));
    }

    private static string RowKey(int row)
    {
        var id = RagnarokPerformanceFixture.Id(row);
        var occurrence = Enumerable.Range(0, row).Count(previous => RagnarokPerformanceFixture.Id(previous) == id);
        return $"wallet/00004895C643225F/resource/{id:X16}/{occurrence}";
    }
}
