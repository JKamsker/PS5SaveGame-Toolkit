using SaveToolkit.Sdk;
using SaveToolkit.Sdk.References;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.AstroPlayroom.Tests;

internal static class AstroPerformanceData
{
    public static AllocationProbeResult Allocated(Action operation) =>
        AllocationProbe.Measure(operation, warmupRuns: 5);
}

internal sealed class CountingAstroReferences(IReferenceContext inner) : IReferenceContext
{
    private readonly Dictionary<string, IReferenceCatalog> _catalogs = new(StringComparer.Ordinal);
    public int Finds { get; private set; }
    public IReadOnlyList<ReferencePackInfo> Packs => inner.Packs;
    public AssetContent? GetAsset(string assetId) => inner.GetAsset(assetId);
    public ReferenceDatasetInfo? AssetProvenance(string assetId) => inner.AssetProvenance(assetId);

    public IReferenceCatalog? Catalog(string catalogId, string? gameBuild = null)
    {
        if (_catalogs.TryGetValue(catalogId, out var cached)) return cached;
        if (inner.Catalog(catalogId, gameBuild) is not { } catalog) return null;
        var result = new CountingCatalog(catalog, () => Finds++);
        _catalogs.Add(catalogId, result);
        return result;
    }

    private sealed class CountingCatalog(IReferenceCatalog inner, Action found) : IReferenceCatalog
    {
        public string Id => inner.Id;
        public ReferencePackInfo Pack => inner.Pack;
        public ReferenceDatasetInfo Dataset => inner.Dataset;
        public int Count => inner.Count;
        public IReadOnlyList<string> Categories => inner.Categories;
        public IReadOnlyList<ReferenceSortKey> SortKeys => inner.SortKeys;
        public ReferencePage Search(ReferenceQuery query) => inner.Search(query);
        public ReferenceRecord? Find(string key)
        {
            found();
            return inner.Find(key);
        }
    }
}
