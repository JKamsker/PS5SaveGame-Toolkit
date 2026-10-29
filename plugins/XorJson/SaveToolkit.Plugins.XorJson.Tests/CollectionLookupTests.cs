using System.Text.Json;
using SaveToolkit.Core.References;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Views;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.XorJson.Tests;

[Collection(AllocationMeasurements.Name)]
public sealed class CollectionLookupTests
{
    [Fact]
    public void LargestSampleOpeningAndFirstViewStayWithinAllocationBudget()
    {
        var plugin = new XorJsonPlugin();
        var file = SampleSaves.Load("PPSA16064").AllDiskFiles.Where(file => !file.IsSystemFile).MaxBy(file => file.Length)!;
        // Isolate plugin work from the host's pinned catalog loader; catalog calls
        // are checked separately below, and the standalone harness measures both.
        using (var warmup = plugin.Open(file)) _ = warmup.BuildView();
        var opening = AllocationProbe.MeasureOpen(() => plugin.Open(file), out var opened);
        using var document = opened;
        ISaveDocument? firstViewDocument = null;
        var firstView = AllocationProbe.Measure(() => firstViewDocument!.BuildView(), out _,
            beforeRun: () => firstViewDocument = plugin.Open(file),
            afterRun: () => firstViewDocument?.Dispose());
        opening.AssertInRange(1, 2 * 1024 * 1024);
        firstView.AssertInRange(1, 128 * 1024);
        Assert.Equal(file.ReadAllBytes(), document.Serialize().Single().Content.ToArray());
    }

    [Fact]
    public void MetadataAndSingleRowPagesOnlyResolveVisibleInventoryReferences()
    {
        var plugin = new XorJsonPlugin();
        var references = new CountingReferences(new ReferencePackStore([plugin]).Pin(plugin, "PPSA16064"));
        var entries = Enumerable.Range(0, 1000).Select(_ => new { ItemData = new { TypeTag = "SW.Item.Sword" }, StackCount = 1 });
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new
        {
            SerializeMeta = new { HardFormat = "FCharacterSaveV1" },
            CharacterSaveV1 = new { Inventory = new { Entries = entries } },
        });
        using var document = plugin.Open(MemorySaveSet.SingleFile("hero.sav", bytes), references);
        var collection = Assert.IsAssignableFrom<IQueryableDocument>(document);
        Assert.Equal(1000, collection.Collections.Single(c => c.Id == "character/inventory").TotalCount);
        Assert.Equal(0, references.FindCalls);
        var page = collection.Query("character/inventory", new() { Offset = 999, Limit = 1 });
        Assert.Single(page.Rows);
        Assert.Equal(2, references.FindCalls); // One projection lookup and one scalar field lookup.
        _ = collection.Query("character/inventory", new() { Offset = 999, Limit = 1 });
        Assert.Equal(3, references.FindCalls); // Reuse cells, rebuild the live field binding.
        Assert.Equal(bytes, document.Serialize().Single().Content.ToArray());
    }

    private sealed class CountingReferences(IReferenceContext inner) : IReferenceContext
    {
        private readonly Dictionary<string, IReferenceCatalog> _catalogs = new(StringComparer.Ordinal);
        public int FindCalls { get; private set; }
        public IReadOnlyList<ReferencePackInfo> Packs => inner.Packs;
        public AssetContent? GetAsset(string assetId) => inner.GetAsset(assetId);
        public ReferenceDatasetInfo? AssetProvenance(string assetId) => inner.AssetProvenance(assetId);
        public IReferenceCatalog? Catalog(string catalogId, string? gameBuild = null)
        {
            if (_catalogs.TryGetValue(catalogId, out var cached)) return cached;
            if (inner.Catalog(catalogId, gameBuild) is not { } catalog) return null;
            return _catalogs[catalogId] = new CountingCatalog(catalog, () => FindCalls++);
        }
    }

    private sealed class CountingCatalog(IReferenceCatalog inner, Action found) : IReferenceCatalog
    {
        public string Id => inner.Id;
        public ReferencePackInfo Pack => inner.Pack;
        public ReferenceDatasetInfo Dataset => inner.Dataset;
        public int Count => inner.Count;
        public IReadOnlyList<string> Categories => inner.Categories;
        public ReferencePage Search(ReferenceQuery query) => inner.Search(query);
        public ReferenceRecord? Find(string key) { found(); return inner.Find(key); }
    }
}
