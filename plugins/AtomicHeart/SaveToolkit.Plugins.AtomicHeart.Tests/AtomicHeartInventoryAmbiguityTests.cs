using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;

namespace SaveToolkit.Plugins.AtomicHeart.Tests;

public sealed class AtomicHeartInventoryAmbiguityTests
{
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void AmbiguousContainersRemainInTheRawInspectorWithoutBecomingWriteTargets(
        bool duplicateContainer, bool duplicateCollection)
    {
        var bytes = InventoryFixture.Create(duplicateContainer, duplicateCollection);
        var index = SlotDataDecoder.Decode(bytes, SaveExtensionFile.Parse(bytes));
        var record = Assert.Single(index.Objects, item => item.Kind == RecordKinds.Component);
        Assert.Equal(duplicateContainer ? 2 : 1, record.Properties.Count);
        Assert.All(record.Properties, property => Assert.Equal(duplicateCollection ? 2 : 1, property.Children.Count));

        using var document = WriteTestData.Open(bytes);
        Assert.Empty(WriteTestData.Table(document).Rows);
        Assert.False(document.CanEdit);
        var plan = Assert.IsAssignableFrom<IActionPlanningDocument>(document).PlanAction(
            InventoryPlanning.ActionId, new Dictionary<string, FieldValue> { ["amount"] = FieldValue.Of(1) }, new());
        Assert.Empty(plan.Changes);
        Assert.Equal(index.Plain, document.ReadBlob("plain", 0, int.MaxValue).ToArray());
        Assert.Equal(bytes, WriteTestData.Output(document));
    }

    [Fact]
    public void UnambiguousAuthoredInventoryRetainsItsExistingWriteTargets()
    {
        using var document = WriteTestData.Open(InventoryFixture.Create());
        var rows = WriteTestData.Table(document).Rows;
        Assert.Equal(2, rows.Count);
        Assert.True(document.CanEdit);
        Assert.All(rows, row => Assert.False(WriteTestData.Field(row, "amount").Spec.ReadOnly));
        var field = WriteTestData.Field(rows[0], "amount");
        Assert.True(document.ApplyEdits([new(field.ValueId, FieldValue.Of(1))]).Succeeded);
        using var reopened = WriteTestData.Open(WriteTestData.Output(document));
        Assert.Equal(FieldValue.Of(1), reopened.GetValue(field.ValueId));
    }
}
