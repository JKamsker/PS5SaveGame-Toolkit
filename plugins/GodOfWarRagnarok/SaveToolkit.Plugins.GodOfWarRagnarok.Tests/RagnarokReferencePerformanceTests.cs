using SaveToolkit.Core.References;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.References;

namespace SaveToolkit.Plugins.GodOfWarRagnarok.Tests;

public sealed class RagnarokReferencePerformanceTests
{
    [Theory]
    [InlineData(0x5AE80674D240529Eul)]
    [InlineData(0xF0E1D2C3B4A59687ul)]
    public void RepeatedIdentityResolutionReusesPinnedLookupsAndAttribution(ulong id)
    {
        var context = Context();
        var references = new RagnarokReferences(context);
        var record = references.Find(id);
        var name = references.Name(id);
        var attribution = references.Attribution(id);
        var category = references.Category(id);
        var catalog = references.Catalog(id);
        context.FindCalls = 0;
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var repeat = 0; repeat < 100; repeat++)
        {
            _ = references.Find(id);
            _ = references.Name(id);
            _ = references.Attribution(id);
            _ = references.Category(id);
            _ = references.Catalog(id);
        }
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(0, context.FindCalls);
        Assert.InRange(allocated, 0, 1024);
        Assert.Same(record, references.Find(id));
        Assert.Equal(name, references.Name(id));
        Assert.Equal(attribution, references.Attribution(id));
        Assert.Equal(category, references.Category(id));
        Assert.Equal(catalog, references.Catalog(id));
        Assert.Equal(id == 0x5AE80674D240529Eul ? "Hacksilver" : $"Unknown ID 0x{id:X16}", name);
    }

    [Fact]
    public void ResolutionCachesBelongToTheirPinnedDocument()
    {
        var firstContext = Context(" first");
        var secondContext = Context(" second");
        var first = new RagnarokReferences(firstContext);
        var second = new RagnarokReferences(secondContext);
        const ulong hacksilver = 0x5AE80674D240529E;
        Assert.Equal("Hacksilver first", first.Name(hacksilver));
        Assert.Equal("Hacksilver second", second.Name(hacksilver));
        Assert.Equal("Hacksilver first", first.Name(hacksilver));
        Assert.True(firstContext.FindCalls > 0);
        Assert.True(secondContext.FindCalls > 0);
    }

    private static CountingContext Context(string suffix = "")
    {
        var plugin = new GodOfWarRagnarokPlugin();
        return new(RagnarokTestSupport.PinnedReferences(plugin), suffix);
    }

    private sealed class CountingContext(IReferenceContext inner, string suffix) : IReferenceContext
    {
        public int FindCalls { get; set; }
        public IReadOnlyList<ReferencePackInfo> Packs => inner.Packs;
        public IReferenceCatalog? Catalog(string catalogId, string? gameBuild = null) =>
            inner.Catalog(catalogId, gameBuild) is { } catalog ? new CountingCatalog(catalog, this, suffix) : null;
        public AssetContent? GetAsset(string assetId) => inner.GetAsset(assetId);
        public ReferenceDatasetInfo? AssetProvenance(string assetId) => inner.AssetProvenance(assetId);
    }

    private sealed class CountingCatalog(IReferenceCatalog inner, CountingContext context, string suffix) : IReferenceCatalog
    {
        public string Id => inner.Id;
        public ReferencePackInfo Pack => inner.Pack;
        public ReferenceDatasetInfo Dataset => inner.Dataset;
        public int Count => inner.Count;
        public IReadOnlyList<string> Categories => inner.Categories;
        public ReferenceRecord? Find(string key)
        {
            context.FindCalls++;
            var record = inner.Find(key);
            return record is not null && suffix.Length > 0 ? record with { Name = record.Name + suffix } : record;
        }
        public ReferencePage Search(ReferenceQuery query) => inner.Search(query);
    }
}
