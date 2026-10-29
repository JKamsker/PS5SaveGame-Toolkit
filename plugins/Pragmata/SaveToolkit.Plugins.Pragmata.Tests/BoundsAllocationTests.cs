using SaveToolkit.Plugins.Pragmata.Crypto;
using SaveToolkit.Plugins.Pragmata.Editing;
using SaveToolkit.Plugins.Pragmata.Format;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.Pragmata.Tests;

[Collection(typeof(AllocationTestCollection))]
public sealed class BoundsAllocationTests
{
    [Fact]
    public void LargeInventoryBoundsVisitNodesWithoutPerNodeEnumerators()
    {
        const int count = 10_000;
        var container = new DsssContainer(SyntheticInventory.Create(count));
        var layout = new SaveLayout(container.Plaintext);
        _ = new ObservedEditBounds(layout, container.Plaintext);
        var allocated = AllocationProbe.Measure(() => new ObservedEditBounds(layout, container.Plaintext), out var bounds);
        foreach (var item in layout.Items)
        {
            var id = Assert.IsType<IntegerSpec>(bounds.Spec(item.Find(KnownHashes.ItemId)!));
            Assert.Equal(0, id.Min);
            Assert.Equal(count, id.Max);
            Assert.Equal(IntegerSpec.Int32, bounds.Spec(item.Find(KnownHashes.Quantity)!));
            var floating = Assert.IsType<FloatSpec>(bounds.Spec(item.Find(SyntheticInventory.FloatHash)!));
            Assert.Equal(-2.5, floating.Min);
            Assert.Equal(4.75, floating.Max);
        }
        allocated.AssertLessThan(2_000_000, $"Bounds calculation allocated {allocated.MinimumBytes:N0} bytes.");
    }

    [Fact]
    public void BoundsKeepMatchingClassesRepresentationsAndSpecialFieldsSeparate()
    {
        var container = new DsssContainer(SyntheticInventory.Create(2));
        var layout = new SaveLayout(container.Plaintext);
        var first = layout.Items[0];
        var second = layout.Items[1];
        // Same field hash and storage representation, but a different containing class.
        second.ClassHash = 0x22222222;
        var bounds = new ObservedEditBounds(layout, container.Plaintext);
        Assert.Equal(1, Assert.IsType<IntegerSpec>(bounds.Spec(first.Find(KnownHashes.ItemId)!)).Max);
        Assert.Equal(2, Assert.IsType<IntegerSpec>(bounds.Spec(second.Find(KnownHashes.ItemId)!)).Max);
        var negative = Assert.IsType<FloatSpec>(bounds.Spec(first.Find(SyntheticInventory.FloatHash)!));
        Assert.Equal(-2.5, negative.Min);
        Assert.Equal(1, negative.Max);
        var positive = Assert.IsType<FloatSpec>(bounds.Spec(second.Find(SyntheticInventory.FloatHash)!));
        Assert.Equal(0, positive.Min);
        Assert.Equal(4.75, positive.Max);
        Assert.Null(bounds.Validate(first.Find(SyntheticInventory.FloatHash)!, FieldValue.Of(-2.5)));
        Assert.NotNull(bounds.Validate(first.Find(SyntheticInventory.FloatHash)!, FieldValue.Of(4.75)));
        Assert.Equal(IntegerSpec.UInt64, bounds.Spec(layout.Header!.Find(KnownHashes.Playtime)!));
        Assert.All(layout.Timers, timer => Assert.Equal(IntegerSpec.UInt64, bounds.Spec(timer)));
        Assert.Equal(IntegerSpec.Int32, bounds.Spec(layout.Detail.Find(KnownHashes.Slot)!));
    }
}
