using SaveToolkit.Plugins.Pragmata.Editing;
using SaveToolkit.Plugins.Pragmata.Format;
using SaveToolkit.Plugins.Pragmata.Views;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;

namespace SaveToolkit.Plugins.Pragmata.Tests;

public sealed class IdentityAllocationTests
{
    [Fact]
    public void RepeatedPhysicalPathsReuseTheirImmutableIdentity()
    {
        var root = new SaveNode(0x12345678, FieldTags.Class);
        var array = new SaveNode(0xabcdef01, FieldTags.Array, root);
        var item = new SaveNode(0, FieldTags.Class, array, 123);
        var leaf = new SaveNode(0x11223344, FieldTags.Int32, item);
        Assert.Equal("12345678/abcdef01[123]/11223344", leaf.Path);
        var expected = leaf.Path;
        var start = GC.GetAllocatedBytesForCurrentThread();
        string? actual = null;
        for (var index = 0; index < 10_000; index++) actual = leaf.Path;
        var allocated = GC.GetAllocatedBytesForCurrentThread() - start;
        Assert.Equal(expected, actual);
        Assert.True(allocated == 0, $"Repeated immutable paths allocated {allocated:N0} bytes.");
        // Stored contents and presentation do not change physical identity.
        root.ClassHash = 42;
        array.Offset = 100;
        item.Children = [leaf];
        Assert.Same(expected, leaf.Path);
    }

    [Fact]
    public void LargeQuantityPreviewDoesNotRebuildEveryAncestorPath()
    {
        const int count = 10_000;
        using var document = new PragmataDocument("data.bin", SyntheticInventory.Create(count));
        _ = document.BuildView();
        for (var offset = 0; offset < count; offset += 200)
            _ = document.Query(InventoryCollections.CollectionId, new() { Offset = offset, Limit = 200 });
        var parameters = new Dictionary<string, FieldValue> { ["quantity"] = FieldValue.Of(17L) };
        var scope = new ActionScope();
        _ = document.PlanAction(PragmataViews.QuantityAction, parameters, scope);
        var start = GC.GetAllocatedBytesForCurrentThread();
        var plan = document.PlanAction(PragmataViews.QuantityAction, parameters, scope);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - start;
        Assert.Empty(plan.Errors);
        Assert.Equal(count, plan.Changes.Count);
        Assert.All(plan.Changes, change => Assert.Equal(17, change.NewValue.AsInt32()));
        Assert.Equal(document.Layout.Items.Select(item => item.Find(KnownHashes.Quantity)!.Path), plan.Changes.Select(change => change.ValueId));
        Assert.True(allocated < 11_000_000, $"Quantity preview allocated {allocated:N0} bytes.");
        Assert.Equal(QuantityActions.RowKey(document.Layout.Items[0]), $"inventory/record-at-{document.Layout.Items[0].Offset:x}");
    }
}
