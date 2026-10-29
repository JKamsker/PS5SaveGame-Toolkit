using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.SuckerPunch.Tests;

public sealed class SyntheticEditingTests
{
    [Theory]
    [InlineData(false, 0U)]
    [InlineData(false, 32767U)]
    [InlineData(true, 0U)]
    [InlineData(true, 9U)]
    public void BoundaryEditsReopenWithExactCountsCopiesChecksumAndOpaqueBytes(bool yotei, uint target)
    {
        var save = new SyntheticSave(yotei);
        using var document = save.Open();
        var result = document.ApplyEdits([new(save.ResourceId, FieldValue.Of((ulong)target)),
            new(save.ArrowId, FieldValue.Of((ulong)target))]);
        Assert.True(result.Succeeded);
        Assert.Equal(4, result.Changes.Count);
        Assert.All(result.Changes, change => Assert.NotEqual(change.OldValue, change.NewValue));
        Assert.Contains(result.Warnings, warning => warning.Contains("not validated on console", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Changes, change => change.ValueId == save.ArrowId + "-copy");
        Assert.Contains(result.Changes, change => change.ValueId == $"container/word{save.ChecksumOffset / sizeof(uint)}");
        WriteAssertions.EditedQuantitiesMatch(save, document, target);
        Assert.True(document.ApplyEdits([new(save.ResourceId, FieldValue.Of((ulong)SyntheticSave.InitialQuantity)),
            new(save.ArrowId, FieldValue.Of((ulong)SyntheticSave.InitialQuantity))]).Succeeded);
        Assert.Equal(save.Bytes, Assert.Single(document.Serialize()).Content.ToArray());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InvalidSecondEditRejectsTheWholeBatchAndNoOpsPreserveEveryByte(bool yotei)
    {
        var save = new SyntheticSave(yotei);
        using var document = save.Open();
        FieldValue[] invalid = [FieldValue.Null, FieldValue.Of("3"), FieldValue.Of(3.0), FieldValue.Of(true),
            FieldValue.Of(-1L), FieldValue.Of(Int128.MaxValue), FieldValue.Of(yotei ? 10L : 32768L)];
        foreach (var value in invalid)
        {
            var result = document.ApplyEdits([new(save.ResourceId, FieldValue.Of(5L)), new(save.ArrowId, value)]);
            Assert.False(result.Succeeded);
            Assert.Empty(result.Changes);
            Assert.Equal(SyntheticSave.InitialQuantity, document.GetValue(save.ResourceId).AsUInt32());
            Assert.Equal(save.Bytes, Assert.Single(document.Serialize()).Content.ToArray());
        }
        foreach (var id in new[] { save.Prefix + "TestItem/quantity", "container/word0", "unknown" })
        {
            var result = document.ApplyEdits([new(save.ResourceId, FieldValue.Of(5L)), new(id, FieldValue.Of(1L))]);
            Assert.False(result.Succeeded);
            Assert.Empty(result.Changes);
            Assert.Equal(save.Bytes, Assert.Single(document.Serialize()).Content.ToArray());
        }
        var noOp = document.ApplyEdits([new(save.ResourceId, document.GetValue(save.ResourceId)),
            new(save.ArrowId, document.GetValue(save.ArrowId))]);
        Assert.True(noOp.Succeeded);
        Assert.All(noOp.Changes, change => Assert.Equal(change.OldValue, change.NewValue));
        Assert.Equal(save.Bytes, Assert.Single(document.Serialize()).Content.ToArray());
    }

    [Theory]
    [InlineData(false, 0U, false)]
    [InlineData(false, 2U, false)]
    [InlineData(false, 3U, true)]
    [InlineData(true, 0U, false)]
    [InlineData(true, 2U, false)]
    [InlineData(true, 3U, false)]
    public void EligibilityWordsAreTitleSpecific(bool yotei, uint flags, bool editable)
    {
        var save = new SyntheticSave(yotei);
        save.Write(save.Inventory["ArrowNormal"], flags);
        save.Repair();
        using var document = save.Open();
        var table = yotei ? YoteiTestData.Inventory(document) : SuckerPunchTestData.Inventory(document);
        var field = Assert.IsType<FieldNode>(table.Rows.Single(row => row.Key == "ArrowNormal").Cells["quantity"]);
        Assert.Equal(!editable, field.Spec.ReadOnly);
        Assert.Equal(editable, document.ApplyEdits([new(save.ArrowId, FieldValue.Of(3L))]).Succeeded);
        if (!editable) Assert.Equal(save.Bytes, Assert.Single(document.Serialize()).Content.ToArray());
        else Assert.Equal(3U, document.GetValue(save.ArrowId + "-copy").AsUInt32());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void YoteiEligibilityByteCannotBeReplacedByTheTsushimaWordRule(byte flag)
    {
        var save = new SyntheticSave(yotei: true);
        save.Bytes[save.Inventory["Mon"] + sizeof(uint)] = flag;
        save.Repair();
        using var document = save.Open();
        Assert.False(document.ApplyEdits([new(save.ResourceId, FieldValue.Of(3L))]).Succeeded);
        Assert.Equal(save.Bytes, Assert.Single(document.Serialize()).Content.ToArray());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InconsistentAmmoIsReadOnlyAndMixedEditsRemainAtomic(bool yotei)
    {
        var save = new SyntheticSave(yotei);
        save.Write(save.Inventory["ArrowNormal"] + save.QuantityRelativeOffset + sizeof(uint), 3);
        save.Repair();
        using var document = save.Open();
        Assert.False(document.ApplyEdits([new(save.ResourceId, FieldValue.Of(5L)),
            new(save.ArrowId, FieldValue.Of(3L))]).Succeeded);
        Assert.Equal(save.Bytes, Assert.Single(document.Serialize()).Content.ToArray());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ChecksumMismatchRejectsTsushimaAndLeavesYoteiInspectionOnly(bool yotei)
    {
        var save = new SyntheticSave(yotei);
        save.Bytes[save.ChecksumOffset] ^= 1;
        if (!yotei)
        {
            Assert.Equal("Ghost of Tsushima save checksum is invalid.",
                Assert.Throws<SaveFormatException>(() => save.Open()).Message);
            return;
        }
        using var document = save.Open();
        Assert.False(document.CanEdit);
        Assert.All(YoteiTestData.Inventory(document).Rows,
            row => Assert.True(Assert.IsType<FieldNode>(row.Cells["quantity"]).Spec.ReadOnly));
        Assert.False(document.ApplyEdits([new(save.ResourceId, FieldValue.Of(5L))]).Succeeded);
        var planner = (IActionPlanningDocument)document;
        var plan = planner.PlanAction(save.ActionId, new Dictionary<string, FieldValue> { ["quantity"] = FieldValue.Of(5L) }, new());
        Assert.Empty(plan.Changes);
        Assert.NotEmpty(plan.SkippedRows);
        Assert.Equal(save.Bytes, Assert.Single(document.Serialize()).Content.ToArray());
    }

    [Fact]
    public void YoteiOriginalAboveObservedMaximumCanBeRestoredExactly()
    {
        const uint original = 112_100;
        var save = new SyntheticSave(yotei: true);
        save.Write(save.Inventory["Mon"] + save.QuantityRelativeOffset, original);
        save.Repair();
        using var document = save.Open();
        var field = Assert.IsType<FieldNode>(YoteiTestData.Inventory(document).Rows.Single(row => row.Key == "Mon").Cells["quantity"]);
        Assert.Equal((Int128)original, Assert.IsType<IntegerSpec>(field.Spec).Max);
        Assert.True(document.ApplyEdits([new(save.ResourceId, FieldValue.Of(0L))]).Succeeded);
        Assert.True(document.ApplyEdits([new(save.ResourceId, FieldValue.Of((ulong)original))]).Succeeded);
        Assert.Equal(save.Bytes, Assert.Single(document.Serialize()).Content.ToArray());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReturnedBuffersAreIndependentAndRemainSnapshotsAfterEdits(bool yotei)
    {
        var save = new SyntheticSave(yotei);
        using var document = save.Open();
        var offset = save.Inventory[save.ResourceName] + save.QuantityRelativeOffset;
        WriteAssertions.FlipReturnedByte(Assert.Single(document.Serialize()).Content);
        Assert.Equal(save.Bytes, Assert.Single(document.Serialize()).Content.ToArray());
        WriteAssertions.FlipReturnedByte(document.ReadBlob("content", offset, sizeof(uint)));
        Assert.Equal(save.Bytes, Assert.Single(document.Serialize()).Content.ToArray());
        var export = Assert.Single(document.Serialize()).Content;
        var blob = document.ReadBlob("content", offset, sizeof(uint));
        Assert.True(document.ApplyEdits([new(save.ResourceId, FieldValue.Of(3L))]).Succeeded);
        Assert.Equal(save.Bytes, export.ToArray());
        Assert.Equal(save.Bytes.AsSpan(offset, sizeof(uint)).ToArray(), blob.ToArray());
        Assert.Equal(3U, document.GetValue(save.ResourceId).AsUInt32());
    }
}
