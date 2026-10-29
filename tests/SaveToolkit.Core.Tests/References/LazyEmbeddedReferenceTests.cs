using System.Text.Json;
using SaveToolkit.Core.References;
using SaveToolkit.Core.Tests.Fakes;
using SaveToolkit.Sdk.References;

namespace SaveToolkit.Core.Tests.References;

public sealed class LazyEmbeddedReferenceTests
{
    private const string EnglishId = "lazy.en";
    private const string GermanId = "lazy.de";
    private const string EnglishRecords = "refpacks/lazy.en/items.json";
    private const string GermanRecords = "refpacks/lazy.de/items.json";
    private const string EnglishIcon = "refpacks/lazy.en/icons/a.png";
    private const string QualifiedIcon = "lazy.en:icons/a.png";

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ListingPinningAndSelectionMaterializeOnlyTheSelectedCatalog(bool host)
    {
        var source = Fixture();
        var sdk = EmbeddedReferenceContext.Load(source);
        var core = new ReferencePackStore(null, [source]);
        Assert.Empty(core.LoadErrors);
        var before = source.Reads(EnglishRecords);
        Assert.Equal(2, before); // SDK and Core each validate once at startup.
        Assert.Equal(2, source.Reads(GermanRecords));
        Assert.All(sdk.Packs, p => Assert.Equal(2, p.Datasets.Single(d => d.Id == "items").ItemCount));
        var pinned = sdk.Pin("test.items", null);
        var corePinned = core.Pin(new ItemsPlugin(), null);
        Assert.Equal(4, corePinned.Datasets().Count);
        Assert.NotNull(pinned.AssetProvenance(QualifiedIcon));
        Assert.NotNull(corePinned.AssetProvenance(QualifiedIcon));
        Assert.Null(pinned.Catalog("missing"));
        Assert.Null(corePinned.CatalogForHost("missing"));
        Assert.Equal(2, source.Reads(EnglishIcon));
        Assert.Equal(before, source.Reads(EnglishRecords));

        var catalog = host ? corePinned.CatalogForHost("items") : pinned.Catalog("items");
        Assert.Equal(EnglishId, catalog!.Pack.Id);
        Assert.Equal("User Potion", catalog.Find("1001")!.Name);
        Assert.Equal(before + 1, source.Reads(EnglishRecords));
        Assert.Equal(2, source.Reads(GermanRecords));
        Assert.Equal(2, source.Reads(EnglishIcon));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ParallelFirstAccessPublishesOneCatalogAndOneAssetAcrossPinnedContexts(bool host)
    {
        var source = Fixture();
        var pins = Pins(source, host);
        var catalogs = await Task.WhenAll(pins.Select(pin => Task.Run(() => pin.Catalog("items"))));
        Assert.All(catalogs, catalog => Assert.Same(catalogs[0], catalog));
        Assert.Equal(2, source.Reads(EnglishRecords));
        Assert.Equal(1, source.Reads(GermanRecords));
        Assert.Equal(1, source.Reads(EnglishIcon));

        var assets = await Task.WhenAll(pins.Select(pin => Task.Run(() => pin.GetAsset(QualifiedIcon))));
        Assert.NotNull(assets[0]);
        Assert.All(assets, asset => Assert.Same(assets[0], asset));
        Assert.Equal(2, source.Reads(EnglishIcon));
        Assert.Equal(1, source.Reads("refpacks/lazy.de/icons/a.png"));
    }

    [Fact]
    public void AccessingOneDatasetDoesNotMaterializeAnotherDatasetOfTheSamePack()
    {
        var builder = new PackBuilder
        {
            Id = EnglishId,
            Mutate = manifest =>
            {
                var items = manifest["datasets"]![0]!;
                var equipment = items.DeepClone();
                equipment["id"] = "equipment";
                items["files"]!.AsArray().RemoveAt(1);
                equipment["files"]!.AsArray().RemoveAt(0);
                manifest["datasets"]!.AsArray().Add(equipment);
            },
        }.Records("items.json", """{"records":[{"id":"1","name":"Potion"}]}""")
            .Records("equipment.json", """{"records":[{"id":"2","name":"Shield"}]}""");
        var source = CountingPackResourceAssembly.Create(builder);
        var context = EmbeddedReferenceContext.Load(source).Pin("test.items", null);
        Assert.NotNull(context.Catalog("items")!.Find("1"));
        Assert.Equal(2, source.Reads(EnglishRecords));
        Assert.Equal(1, source.Reads("refpacks/lazy.en/equipment.json"));
        Assert.NotNull(context.Catalog("equipment")!.Find("2"));
        Assert.Equal(2, source.Reads("refpacks/lazy.en/equipment.json"));
    }

    [Fact]
    public async Task CancelledWaiterDoesNotCancelOrPoisonSharedFirstMaterialization()
    {
        var source = Fixture();
        var context = EmbeddedReferenceContext.Load(source).Pin("test.items", null);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim();
        source.BeforeRead = path =>
        {
            if (path != EnglishRecords) return;
            entered.TrySetResult();
            if (!release.Wait(TimeSpan.FromSeconds(20))) throw new TimeoutException("Materialization was not released.");
        };
        var load = Task.Run(() => context.Catalog("items"));
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            using var cancelled = new CancellationTokenSource();
            cancelled.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => load.WaitAsync(cancelled.Token));
        }
        finally { release.Set(); }
        var catalog = await load.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.NotNull(catalog);
        Assert.Same(catalog, context.Catalog("items"));
        Assert.Equal(2, source.Reads(EnglishRecords));
    }

    [Fact]
    public void IdenticalRecordStringsAreSharedAcrossMaterializedLocales()
    {
        const string rows = """
            {"records":[
              {"id":"1","name":"Shared name","category":"Shared category","description":"Shared description",
               "source":"https://example.org/shared","attributes":{"shared-key":"shared-value"}},
              {"id":"2","name":"Shared name","category":"Shared category","description":"Shared description",
               "source":"https://example.org/shared","attributes":{"shared-key":"shared-value"}}
            ]}
            """;
        var source = CountingPackResourceAssembly.Create(
            new PackBuilder { Id = EnglishId, Locale = "en" }.Records("items.json", rows),
            new PackBuilder { Id = GermanId, Locale = "de" }.Records("items.json", rows));
        var context = EmbeddedReferenceContext.Load(source);
        var english = context.Pin("test.items", null, ["en"]).Catalog("items")!;
        var german = context.Pin("test.items", null, ["de"]).Catalog("items")!;
        var first = english.Find("1")!;
        foreach (var record in new[] { english.Find("2")!, german.Find("1")!, german.Find("2")! })
        {
            Assert.Same(first.Name, record.Name);
            Assert.Same(first.Category, record.Category);
            Assert.Same(first.Description, record.Description);
            Assert.Same(first.Source, record.Source);
            Assert.Same(first.Attributes.Keys.Single(), record.Attributes.Keys.Single());
            Assert.Same(first.Attributes.Values.Single(), record.Attributes.Values.Single());
        }
    }

    [Theory]
    [InlineData(255)]
    [InlineData(256)]
    [InlineData(257)]
    [InlineData(8192)]
    public void PooledStringsPreserveBufferBoundariesEscapesAndOrdinalAttributeKeys(int descriptionLength)
    {
        var description = new string('x', descriptionLength);
        var descriptionJson = JsonSerializer.Serialize(description);
        var rows = $$$"""
            {"records":[
              {"id":"1","name":"  Caf\u00e9 \uD83D\uDE80  ","description":{{{descriptionJson}}},
               "attributes":{"Key":"Upper","key":"lower","empty":"","\uD83D\uDE80":"Caf\u00e9 \uD83D\uDE80"}},
              {"id":"2","name":"Caf\u00e9 \uD83D\uDE80","description":{{{descriptionJson}}},
               "attributes":{"Key":"Upper","key":"lower","empty":"","\uD83D\uDE80":"Caf\u00e9 \uD83D\uDE80"}}
            ]}
            """;
        var source = CountingPackResourceAssembly.Create(new PackBuilder { Id = EnglishId }.Records("items.json", rows));
        var catalog = EmbeddedReferenceContext.Load(source).Pin("test.items", null).Catalog("items")!;
        var first = catalog.Find("1")!;
        var second = catalog.Find("2")!;

        Assert.Equal(description, first.Description);
        Assert.Same(first.Description, second.Description);
        Assert.Equal("Café 🚀", first.Name);
        Assert.Same(first.Name, second.Name);
        Assert.Equal(4, first.Attributes.Count);
        Assert.Equal("Upper", first.Attributes["Key"]);
        Assert.Equal("lower", first.Attributes["key"]);
        Assert.Equal("", first.Attributes["empty"]);
        Assert.Equal("Café 🚀", first.Attributes["🚀"]);
        Assert.Same(first.Name, first.Attributes["🚀"]);
        foreach (var (key, value) in first.Attributes)
        {
            var secondKey = second.Attributes.Keys.Single(candidate => candidate == key);
            Assert.Same(key, secondKey);
            Assert.Same(value, second.Attributes[secondKey]);
        }
    }

    private static CountingPackResourceAssembly Fixture() => CountingPackResourceAssembly.Create(
        PackBuilder.WithItems(id: EnglishId), PackBuilder.WithItems(id: GermanId, locale: "de"));

    private static IReferenceContext[] Pins(CountingPackResourceAssembly source, bool host)
    {
        if (host)
        {
            var store = new ReferencePackStore(null, [source]);
            Assert.Empty(store.LoadErrors);
            return [.. Enumerable.Range(0, 32).Select(_ => store.Pin(new ItemsPlugin(), null))];
        }

        var context = EmbeddedReferenceContext.Load(source);
        return [.. Enumerable.Range(0, 32).Select(_ => context.Pin("test.items", null))];
    }
}
