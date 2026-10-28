using System.Buffers.Binary;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;

namespace SaveToolkit.Plugins.FinalFantasy16.Tests;

public sealed class FaithWriteBoundaryTests
{
    private readonly FinalFantasy16Plugin _plugin = new();

    [Fact]
    public void OriginalOutlierIsPreservedAndRestorableButDoesNotAuthorizeOtherOutliers()
    {
        var player = new byte[FaithSchema.LegacyPlayerLength];
        BinaryPrimitives.WriteInt32LittleEndian(player.AsSpan(FaithContextLayout.ChildXpOffset), 123);
        var bytes = FaithTestData.Container((FaithSchema.PlayerType, FaithSchema.PlayerVersion, player));
        using var document = _plugin.Open(FaithTestData.File(bytes));
        _ = document.BuildView();
        Assert.Equal(bytes, Assert.Single(document.Serialize()).Content.ToArray());
        Assert.False(document.ApplyEdits([new("player/childXp", FieldValue.Of(100))]).Succeeded);
        Assert.True(document.ApplyEdits([new("player/childXp", FieldValue.Of(1))]).Succeeded);
        Assert.True(document.ApplyEdits([new("player/childXp", FieldValue.Of(123))]).Succeeded);
        Assert.Equal(bytes, Assert.Single(document.Serialize()).Content.ToArray());
    }

    [Fact]
    public void ExistingDuplicateIdentitiesCanBeRestoredAfterReplacement()
    {
        var items = FaithTestData.Items();
        const int SecondRecordOffset = 16;
        BinaryPrimitives.WriteInt32LittleEndian(items.AsSpan(SecondRecordOffset), 1);
        var bytes = FaithTestData.Container((FaithSchema.ItemType, FaithSchema.ItemVersion, items));
        using var document = _plugin.Open(FaithTestData.File(bytes));
        _ = document.BuildView();
        Assert.True(document.ApplyEdits([new("inventory/1/item", FieldValue.Of("3"))]).Succeeded);
        Assert.True(document.ApplyEdits([new("inventory/1/item", FieldValue.Of("1"))]).Succeeded);
        Assert.Equal(bytes, Assert.Single(document.Serialize()).Content.ToArray());
    }

    [Fact]
    public void EquipmentAndShortcutsRejectWrongCategoriesAndNoncanonicalIds()
    {
        var bytes = FaithTestData.Container((FaithSchema.PlayerType, FaithSchema.PlayerVersion, new byte[FaithSchema.LegacyPlayerLength]),
            (FaithSchema.EquipmentType, FaithSchema.EquipmentVersion, new byte[FaithSchema.EquipmentLength]));
        using var document = _plugin.Open(FaithTestData.File(bytes));
        _ = document.BuildView();
        foreach (var id in new[] { "1", "000005", "-1", "2147483648" })
        {
            Assert.False(document.ApplyEdits([new("equipment/0/0/item", FieldValue.Of(id))]).Succeeded);
            Assert.False(document.ApplyEdits([new("context/shortcuts/0", FieldValue.Of(id))]).Succeeded);
        }
        Assert.Equal(bytes, Assert.Single(document.Serialize()).Content.ToArray());
    }

    [Fact]
    public void SummonReferenceAliasStaysLiveAndIsReportedWithoutAPOrFlagChanges()
    {
        var bytes = FaithTestData.Container((FaithSchema.PlayerType, FaithSchema.PlayerVersion, new byte[FaithSchema.LegacyPlayerLength]));
        using var document = _plugin.Open(FaithTestData.File(bytes));
        _ = document.BuildView();
        var result = document.ApplyEdits([new("context/summons/0", FieldValue.Of("1"))]);
        Assert.True(result.Succeeded);
        Assert.Contains(result.Changes, change => change.ValueId == "context/feats/0" && change.NewValue.AsString() == "1");
        Assert.Equal("1", document.GetValue("context/feats/0").AsString());
        Assert.Equal(0, document.GetValue("context/learnedSummons").AsInt32());
        Assert.Equal(0, document.GetValue("context/revealedSummons").AsInt32());
        FaithWriteTests.AssertOnlyRanges(bytes, Assert.Single(document.Serialize()).Content.ToArray(),
            [(FaithContainer.HeaderSize + FaithBlock.HeaderSize + FaithContextLayout.SummonModesOffset, sizeof(int))]);
    }

    [Fact]
    public void QuantityPlanIncludesReadOnlyUpgradeAliasWithoutClaimingActivation()
    {
        var items = FaithTestData.Items();
        const int SecondRecordOffset = 16;
        BinaryPrimitives.WriteInt32LittleEndian(items.AsSpan(SecondRecordOffset), 506);
        using var document = _plugin.Open(FaithTestData.File(FaithTestData.Container((FaithSchema.ItemType, FaithSchema.ItemVersion, items))));
        _ = document.BuildView();
        var planner = Assert.IsAssignableFrom<IActionPlanningDocument>(document);
        var parameters = new Dictionary<string, FieldValue> { [FaithQuantityPlanning.QuantityParameter] = FieldValue.Of(1) };
        var scope = new ActionScope(["inventory/1"]);
        var plan = planner.PlanAction(FaithQuantityPlanning.ActionId, parameters, scope);
        Assert.Contains(plan.Changes, change => change.ValueId == "capacity/506/main/1" && change.Derived);
        var result = planner.ApplyActionPlan(FaithQuantityPlanning.ActionId, parameters, scope, plan);
        Assert.True(result.Succeeded);
        Assert.Contains(result.Changes, change => change.ValueId == "capacity/506/main/1");
        Assert.Equal(1, document.GetValue("capacity/506/main/1").AsInt32());
    }

    [Fact]
    public void SystemRoleCannotWriteEvenWhenItContainsRecognizedGameplayBlocks()
    {
        var bytes = FaithTestData.Container((FaithSchema.ItemType, FaithSchema.ItemVersion, FaithTestData.Items()));
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(FaithContainer.VersionOffset), FaithContainer.SystemVersion);
        using var document = _plugin.Open(FaithTestData.File(bytes, name: "system-save-data"));
        var fields = FaithViewFields.All(document.BuildView());
        Assert.False(document.CanEdit);
        Assert.All(fields, field => Assert.True(field.Spec.ReadOnly));
        Assert.False(document.ApplyEdits([new("inventory/0/quantity", FieldValue.Of(1))]).Succeeded);
        Assert.Equal(bytes, Assert.Single(document.Serialize()).Content.ToArray());
    }
}
