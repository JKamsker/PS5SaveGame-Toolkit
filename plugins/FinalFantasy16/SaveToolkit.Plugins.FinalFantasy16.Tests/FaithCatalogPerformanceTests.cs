using SaveToolkit.Core.References;
using SaveToolkit.Sdk.References;

namespace SaveToolkit.Plugins.FinalFantasy16.Tests;

public sealed class FaithCatalogPerformanceTests
{
    [Fact]
    public void InventoryAndCharacterViewsShareDocumentCatalogResolution()
    {
        var plugin = new FinalFantasy16Plugin();
        var references = new FaithCountingReferences(new ReferencePackStore([plugin]).Pin(plugin, "PPSA10665"));
        var bytes = FaithTestData.Container((FaithSchema.ItemType, FaithSchema.ItemVersion, FaithTestData.Items()),
            (FaithSchema.EquipmentType, FaithSchema.EquipmentVersion, new byte[FaithSchema.EquipmentLength]));
        using var document = plugin.Open(FaithTestData.File(bytes), references);
        _ = document.BuildView();
        _ = document.BuildView();
        Assert.All(references.CatalogCalls, pair => Assert.Equal(1, pair.Value));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RepeatedPresentAndAbsentItemLookupsResolveEachPinnedCatalogOnce(bool pinned)
    {
        var plugin = new FinalFantasy16Plugin();
        var references = new FaithCountingReferences(pinned
            ? new ReferencePackStore([plugin]).Pin(plugin, "PPSA10665") : IReferenceContext.Empty);
        var catalog = new FaithCatalog(references);
        foreach (var id in new[] { 5, 900000 })
        {
            var name = catalog.Name(id);
            var category = catalog.Category(id);
            var provenance = catalog.Provenance(id);
            var finds = references.FindCalls;
            for (var repeat = 0; repeat < 10; repeat++)
            {
                Assert.Equal(name, catalog.Name(id));
                Assert.Equal(category, catalog.Category(id));
                Assert.Equal(provenance, catalog.Provenance(id));
                _ = catalog.Item(id);
            }
            Assert.Equal(finds, references.FindCalls);
        }
        Assert.Equal(1, references.CatalogCalls[FaithCatalog.ItemsId]);
        Assert.Equal(1, references.CatalogCalls[FaithCatalog.WikiItemsId]);
        Assert.Contains("Earlier curated reference:", catalog.Provenance(5));
        Assert.Equal("Unknown item 900000", catalog.Name(900000));
        Assert.Equal("unmapped", catalog.Category(900000));
    }

    [Fact]
    public void RepeatedItemPresentationDoesNotRebuildAttributionStrings()
    {
        var catalog = new FaithCatalog(IReferenceContext.Empty);
        _ = catalog.Name(5);
        var provenance = catalog.Provenance(5);
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var repeat = 0; repeat < 100; repeat++)
        {
            _ = catalog.Name(5);
            _ = catalog.Category(5);
            _ = catalog.Provenance(5);
        }
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.True(allocated < 4096, $"Repeated immutable item presentation allocated {allocated:N0} bytes.");
        Assert.Contains("Mapping confidence: Inferred", provenance);
    }
}
