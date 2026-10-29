using SaveToolkit.Core.References;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.References;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.SuckerPunch.Tests;

[Collection(typeof(AllocationTestCollection))]
public sealed class CollectionPerformanceTests
{
    private const long MaximumCachedPageAllocation = 12 * 1024;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SubsequentPagesAllocateOnlyBoundedViewRows(bool yotei)
    {
        using var data = new CollectionPerformanceData(yotei);
        string[] columns = yotei ? ["id", "category", "a", "b", "state"] :
            ["raw", "reference", "category", "location", "mapping", "state", "icon"];
        foreach (var column in columns)
        {
            var query = new QueryableCollectionQuery { Limit = 1, SortColumn = column, Descending = true };
            _ = data.Query(query);
            var next = query with { Offset = 10 };
            _ = data.Query(next);
            var allocated = AllocationProbe.Measure(() => data.Query(next), out var page);
            Assert.Single(page.Rows);
            Assert.Equal(data.Rows.Count, page.FilteredCount);
            allocated.AssertLessThan(MaximumCachedPageAllocation, $"{column} page allocated {allocated.MinimumBytes:N0} bytes.");
        }
        Assert.Equal(data.Original, Assert.Single(data.Document.Serialize()).Content.ToArray());
    }

    [Fact]
    public void EquipmentReferenceLookupsAfterFirstQueryAreBoundedByPageSize()
    {
        var plugin = new SuckerPunchPlugin();
        var context = new CountingContext(new ReferencePackStore([plugin]).Pin(plugin, "PPSA02225"));
        using var data = new CollectionPerformanceData(false, context);
        var query = new QueryableCollectionQuery { Limit = 10, SortColumn = "reference" };
        _ = data.Query(query);
        var before = context.FindCalls;
        var page = data.Query(query with { Offset = 10 });
        Assert.Equal(10, page.Rows.Count);
        Assert.InRange(context.FindCalls - before, 0, query.Limit);
    }

    private sealed class CountingContext(IReferenceContext inner) : IReferenceContext
    {
        private readonly Dictionary<string, CountingCatalog> _catalogs = new(StringComparer.Ordinal);
        public int FindCalls => _catalogs.Values.Sum(catalog => catalog.FindCalls);
        public IReadOnlyList<ReferencePackInfo> Packs => inner.Packs;
        public AssetContent? GetAsset(string assetId) => inner.GetAsset(assetId);
        public ReferenceDatasetInfo? AssetProvenance(string assetId) => inner.AssetProvenance(assetId);
        public IReferenceCatalog? Catalog(string catalogId, string? gameBuild = null)
        {
            if (inner.Catalog(catalogId, gameBuild) is not { } catalog) return null;
            if (!_catalogs.TryGetValue(catalogId, out var counted))
            {
                counted = new CountingCatalog(catalog);
                _catalogs.Add(catalogId, counted);
            }
            return counted;
        }
    }

    private sealed class CountingCatalog(IReferenceCatalog inner) : IReferenceCatalog
    {
        public int FindCalls { get; private set; }
        public string Id => inner.Id;
        public ReferencePackInfo Pack => inner.Pack;
        public ReferenceDatasetInfo Dataset => inner.Dataset;
        public int Count => inner.Count;
        public IReadOnlyList<string> Categories => inner.Categories;
        public ReferenceRecord? Find(string key) { FindCalls++; return inner.Find(key); }
        public ReferencePage Search(ReferenceQuery query) => inner.Search(query);
    }
}
