using SaveToolkit.Core.References;
using SaveToolkit.Core.Tests.Fakes;
using SaveToolkit.Testing;

namespace SaveToolkit.Core.Tests.References;

[Collection(typeof(AllocationTestCollection))]
public sealed class ReferenceContextCacheTests
{
    private static readonly ItemsPlugin Plugin = new();

    [Fact]
    public void HostOnlyLookupsDoNotMarkCatalogOrAssetsUsed()
    {
        var context = Context();

        var hostCatalog = context.CatalogForHost("items");
        Assert.NotNull(hostCatalog);
        Assert.All(context.Datasets(), d => Assert.False(d.Used));
        Assert.Null(context.Catalog("missing", "build-1"));
        Assert.All(context.Datasets(), d => Assert.False(d.Used));

        Assert.Same(hostCatalog, context.Catalog("items"));
        Assert.All(context.Datasets().Where(d => d.Pack.Id == hostCatalog.Pack.Id), d => Assert.True(d.Used));
        Assert.All(context.Datasets().Where(d => d.Pack.Id != hostCatalog.Pack.Id), d => Assert.False(d.Used));
    }

    [Fact]
    public void HostSelectionFollowsFirstSuccessfulNonNullPluginBuild()
    {
        var context = Context();
        var any = context.Catalog("items");
        Assert.Equal("user.any", any!.Pack.Id);
        Assert.Same(any, context.CatalogForHost("items"));

        var first = context.Catalog("items", "build-1");
        var second = context.Catalog("items", "build-2");

        Assert.Equal("user.b1", first!.Pack.Id);
        Assert.Equal("user.b2", second!.Pack.Id);
        Assert.Same(first, context.CatalogForHost("items"));
        Assert.Same(any, context.Catalog("items"));
        Assert.Same(first, context.Catalog("items", "build-1"));
        Assert.Same(first, context.Catalog("items", "BUILD-1"));
        Assert.Null(context.Catalog("ITEMS", "build-1"));
        Assert.Same(first, context.CatalogForHost("items"));
    }

    [Fact]
    public void FailedBuildSelectionDoesNotPinHostBuild()
    {
        var store = new ReferencePackStore([Plugin]);
        store.Import(PackBuilder.WithItems("First", "user.b1", builds: ["build-1"]).Zip(), "b1.zip");
        store.Import(PackBuilder.WithItems("Second", "user.b2", builds: ["build-2"]).Zip(), "b2.zip");
        var context = new ReferenceContext([store.Find("user.b1")!, store.Find("user.b2")!], ["en"]);

        Assert.Null(context.Catalog("items", "missing-build"));
        Assert.All(context.Datasets(), d => Assert.False(d.Used));
        var second = context.Catalog("items", "build-2");

        Assert.Equal("user.b2", second!.Pack.Id);
        Assert.Same(second, context.CatalogForHost("items"));
        Assert.Null(context.Catalog("items", "missing-build"));
        Assert.Same(second, context.CatalogForHost("items"));
        Assert.All(context.Datasets().Where(d => d.Pack.Id == "user.b1"), d => Assert.False(d.Used));
    }

    [Fact]
    public void ConcurrentHostAndPluginLookupsPreservePinnedBuildAndUsage()
    {
        var context = Context();
        var first = context.Catalog("items", "build-1");
        var second = context.CatalogForHost("items");
        Assert.Same(first, second);

        Parallel.For(0, 256, i =>
        {
            Assert.Same(first, context.CatalogForHost("items"));
            Assert.Equal("user.b2", context.Catalog("items", "build-2")!.Pack.Id);
            Assert.Null(context.Catalog("missing"));
        });

        Assert.All(context.Datasets().Where(d => d.Pack.Id is "user.b1" or "user.b2"), d => Assert.True(d.Used));
        Assert.All(context.Datasets().Where(d => d.Pack.Id == "user.any"), d => Assert.False(d.Used));
    }

    [Fact]
    public void CacheSaturationPreservesSelectionUsageAndExistingCacheEntries()
    {
        var context = Context();
        var first = context.Catalog("items", "build-1");
        for (var i = 0; i < 512; i++) Assert.Null(context.CatalogForHost($"missing/{i}"));

        Assert.Equal("user.b2", context.Catalog("items", "build-2")!.Pack.Id);
        Assert.Same(first, context.CatalogForHost("items"));
        var allocated = AllocationProbe.Measure(() =>
        {
            for (var i = 0; i < 1_000; i++) _ = context.Catalog("items", "build-1");
        });

        allocated.AssertAtMost(4_096, $"Cached lookups after saturation allocated {allocated.MinimumBytes:N0} bytes.");
        Assert.All(context.Datasets().Where(d => d.Pack.Id == "user.b2"), d => Assert.True(d.Used));
    }

    private static ReferenceContext Context()
    {
        var store = new ReferencePackStore([Plugin]);
        store.Import(PackBuilder.WithItems("Any", "user.any").Zip(), "any.zip");
        store.Import(PackBuilder.WithItems("First", "user.b1", builds: ["build-1"]).Zip(), "b1.zip");
        store.Import(PackBuilder.WithItems("Second", "user.b2", builds: ["build-2"]).Zip(), "b2.zip");
        return store.Pin(Plugin, ItemsPlugin.TitleId);
    }
}
