using SaveToolkit.Sdk;
using SaveToolkit.Testing;
using static SaveToolkit.Plugins.SuckerPunch.Tests.QuantityPlanningTestData;

namespace SaveToolkit.Plugins.SuckerPunch.Tests;

[Collection(typeof(AllocationTestCollection))]
public sealed class PlanningPerformanceTests
{
    private const long MaximumPreviewAllocation = 32 * 1024;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PreviewAllocationsAreBoundedForChangingAndEmptySelections(bool yotei)
    {
        var save = new SyntheticSave(yotei);
        using var document = save.Open();
        var planner = (IActionPlanningDocument)document;
        var parameters = Target(1);
        ActionScope[] scopes = [new(), new([])];
        foreach (var scope in scopes)
        {
            for (var i = 0; i < 5; i++) _ = planner.PlanAction(save.ActionId, parameters, scope);
            var allocated = AllocationProbe.Measure(() => planner.PlanAction(save.ActionId, parameters, scope), out var plan);
            Assert.Empty(plan.Errors);
            if (scope.RowKeys is null) Assert.NotEmpty(plan.Changes);
            else Assert.Empty(plan.Changes);
            allocated.AssertLessThan(MaximumPreviewAllocation, $"Preview allocated {allocated.MinimumBytes:N0} bytes.");
        }
        Assert.Equal(save.Bytes, SuckerPunchTestData.PrimaryOutput(document).Content.ToArray());
    }

    [Theory]
    [InlineData(false, 0)]
    [InlineData(false, 1)]
    [InlineData(false, 257)]
    [InlineData(true, 0)]
    [InlineData(true, 1)]
    [InlineData(true, 257)]
    public void PreviewChecksumMatchesIndependentByteSumAcrossCarriesAndMirroredCounts(bool yotei, uint target)
    {
        var save = new SyntheticSave(yotei);
        var resource = save.Inventory[save.ResourceName] + save.QuantityRelativeOffset;
        var ammo = save.Inventory["ArrowNormal"] + save.QuantityRelativeOffset;
        save.Write(resource, 511);
        save.Write(ammo, 256);
        save.Write(ammo + sizeof(uint), 256);
        save.Repair();
        using var document = save.Open();
        var planner = (IActionPlanningDocument)document;
        var parameters = Target(target);
        // Yōtei's current quantity extends the observed bound to 256; 257 skips its ammo row.
        var scope = new ActionScope([save.ResourceName, "ArrowNormal"]);
        var expected = save.Bytes.ToArray();
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(expected.AsSpan(resource), target);
        var changesAmmo = !yotei || target <= 256;
        if (changesAmmo)
        {
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(expected.AsSpan(ammo), target);
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(expected.AsSpan(ammo + sizeof(uint)), target);
        }
        var checksum = yotei ? YoteiTestData.Checksum(expected) : SuckerPunchTestData.Checksum(expected);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(expected.AsSpan(save.ChecksumOffset), checksum);
        var plan = planner.PlanAction(save.ActionId, parameters, scope);
        Assert.Empty(plan.Errors);
        Assert.Equal(checksum, Assert.Single(plan.Changes, change => change.ValueId ==
            $"container/word{save.ChecksumOffset / sizeof(uint)}").NewValue.AsUInt32());
        Assert.Equal(save.Bytes, SuckerPunchTestData.PrimaryOutput(document).Content.ToArray());
        Assert.True(planner.ApplyActionPlan(save.ActionId, parameters, scope, plan).Succeeded);
        Assert.Equal(expected, SuckerPunchTestData.PrimaryOutput(document).Content.ToArray());
        // A later plan must start from the edited checksum, not the open-time header.
        var next = planner.PlanAction(save.ActionId, Target(2), scope);
        Assert.True(planner.ApplyActionPlan(save.ActionId, Target(2), scope, next).Succeeded);
        var final = SuckerPunchTestData.PrimaryOutput(document).Content.ToArray();
        Assert.Equal(yotei ? YoteiTestData.Checksum(final) : SuckerPunchTestData.Checksum(final),
            SuckerPunchTestData.Word(final, save.ChecksumOffset));
    }
}
