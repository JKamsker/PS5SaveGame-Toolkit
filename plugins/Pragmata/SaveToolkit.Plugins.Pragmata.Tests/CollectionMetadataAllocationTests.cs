using SaveToolkit.Plugins.Pragmata.Format;
using SaveToolkit.Plugins.Pragmata.Views;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Views;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.Pragmata.Tests;

[Collection(typeof(AllocationTestCollection))]
public sealed class CollectionMetadataAllocationTests
{
    [Theory]
    [InlineData(2)]
    [InlineData(251)]
    public void RepeatedMetadataReadsDoNotCopyAllDiscoveredCollections(int inventoryRows)
    {
        const int collectionCount = 4_000;
        const int readCount = 100;
        const string replacedCollectionId = "evidence/array-at-0";
        using var document = new PragmataDocument("data000.bin", SyntheticInventory.Create(inventoryRows));
        for (var index = 0; index < collectionCount; index++)
            _ = document.Evidence.Table(new SaveNode(0, FieldTags.Array) { Offset = index }, _ => new TextNode("details"));
        var opening = document.Collections;
        var expectedCount = collectionCount + (inventoryRows > LazyPages.PageSize ? 1 : 0);
        Assert.Equal(expectedCount, opening.Count);
        Assert.Throws<NotSupportedException>(() => ((IList<QueryableCollection>)opening)[0] = opening[0]);
        var allocation = AllocationProbe.Measure(() =>
        {
            for (var iteration = 0; iteration < readCount; iteration++) Assert.Equal(opening.Count, document.Collections.Count);
        });
        TestContext.Current.TestOutputHelper?.WriteLine($"{readCount} metadata reads: {allocation.MinimumBytes} B.");
        allocation.AssertLessThan(100_000, "Repeated host collection validation must not rebuild unchanged metadata arrays.");
        var replacement = new SaveNode(0, FieldTags.Array)
        {
            Offset = 0,
            Children = [new SaveNode(0, FieldTags.UInt32)],
        };
        _ = document.Evidence.Table(replacement, _ => new TextNode("replaced details"));
        Assert.Equal(1, document.Collections.Single(collection => collection.Id == replacedCollectionId).TotalCount);
        Assert.Equal(0, opening.Single(collection => collection.Id == replacedCollectionId).TotalCount);
        _ = document.Evidence.Table(new SaveNode(0, FieldTags.Array) { Offset = collectionCount }, _ => new TextNode("details"));
        Assert.Equal(opening.Count + 1, document.Collections.Count);
        Assert.Equal(expectedCount, opening.Count); // Previously returned snapshots remain stable.
    }
}
