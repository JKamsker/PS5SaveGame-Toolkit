using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;

namespace SaveToolkit.Plugins.Witcher3.Tests;

public sealed class WitcherRefillBoundaryTests
{
    [Theory]
    [InlineData("Bread", 1, 0)]
    [InlineData("Bread", 5, 1)]
    [InlineData("Bread", 5, 4)]
    [InlineData("Bread", 5, 5)]
    [InlineData("Bread", 5, 6)]
    [InlineData("Bread", 65535, 65534)]
    [InlineData("Bread", 65535, 65535)]
    [InlineData("Bottled water", 1, 0)]
    [InlineData("Bottled water", 5, 1)]
    [InlineData("Bottled water", 5, 4)]
    [InlineData("Bottled water", 5, 5)]
    [InlineData("Bottled water", 5, 6)]
    [InlineData("Bottled water", 65535, 65534)]
    [InlineData("Bottled water", 65535, 65535)]
    public void PlanUsesExactImportedCeilingAndNeverDecreasesOrMutatesStacks(string name, int imported, int current)
    {
        // Zero is an in-memory planner boundary, not a claim that a zero-quantity save is supported.
        var item = new InventoryItem(name, 7, 0, checked((ushort)imported), false) { Quantity = checked((ushort)current) };
        var planner = new RefillPlanning([item], true);
        var plan = planner.Plan(new Dictionary<string, FieldValue>(), new());
        Assert.Empty(plan.Errors);
        Assert.Equal(current, item.Quantity);
        Assert.Equal(imported, item.OriginalQuantity);
        Assert.Equal([RefillPlanning.Warning], plan.Warnings);
        if (current < imported)
        {
            var change = Assert.Single(plan.Changes);
            Assert.Equal(item.ValueId, change.ValueId);
            Assert.Equal(FieldValue.Of(current), change.OldValue);
            Assert.Equal(FieldValue.Of(imported), change.NewValue);
            Assert.True(change.OldExists);
            Assert.True(change.NewExists);
            Assert.False(change.Derived);
            Assert.Empty(plan.SkippedRows);
        }
        else
        {
            Assert.Empty(plan.Changes);
            Assert.Equal("7", Assert.Single(plan.SkippedRows).RowKey);
            Assert.Contains("never decreases", plan.SkippedRows[0].Reason, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData("bread")]
    [InlineData("Bread ")]
    [InlineData("Bottled Water")]
    [InlineData("q401_bread")]
    [InlineData("Crowns")]
    public void SimilarLabelsAndUnsupportedNativeIdsCannotBecomeRefillTargets(string name)
    {
        var item = new InventoryItem(name, 9, 0, 5, false) { Quantity = 1 };
        var plan = new RefillPlanning([item], true).Plan(new Dictionary<string, FieldValue>(), new());
        Assert.Empty(plan.Errors);
        Assert.Empty(plan.Changes);
        var skipped = Assert.Single(plan.SkippedRows);
        Assert.Equal("9", skipped.RowKey);
        Assert.NotEmpty(skipped.Reason);
        Assert.Equal(1, item.Quantity);
    }

    [Theory]
    [InlineData("3", 1, 5)]
    [InlineData("9", 2, 8)]
    public void EqualNamesWithDifferentImportedCeilingsAreSelectedByInstance(string key, int current, int imported)
    {
        InventoryItem[] items = [new("Bread", 9, 0, 8, false) { Quantity = 2 }, new("Bread", 3, 0, 5, false) { Quantity = 1 }];
        var plan = new RefillPlanning(items, true).Plan(new Dictionary<string, FieldValue>(), new([key]));
        Assert.Empty(plan.Errors);
        Assert.Empty(plan.SkippedRows);
        var change = Assert.Single(plan.Changes);
        Assert.Equal($"inventory/{key}/quantity", change.ValueId);
        Assert.Equal(FieldValue.Of(current), change.OldValue);
        Assert.Equal(FieldValue.Of(imported), change.NewValue);
        Assert.Equal(new ushort[] { 2, 1 }, items.Select(i => i.Quantity));
    }
}
