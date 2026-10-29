using SaveToolkit.Sdk;
using SaveToolkit.Sdk.References;

namespace SaveToolkit.Plugins.FinalFantasy16.Tests;

internal sealed class FaithCountingReferences(IReferenceContext inner) : IReferenceContext
{
    public Dictionary<string, int> CatalogCalls { get; } = new(StringComparer.Ordinal);
    public int FindCalls { get; private set; }
    public IReadOnlyList<ReferencePackInfo> Packs => inner.Packs;

    public IReferenceCatalog? Catalog(string catalogId, string? gameBuild = null)
    {
        CatalogCalls[catalogId] = CatalogCalls.GetValueOrDefault(catalogId) + 1;
        return inner.Catalog(catalogId, gameBuild) is { } catalog ? new CountingCatalog(catalog, this) : null;
    }

    public AssetContent? GetAsset(string assetId) => inner.GetAsset(assetId);
    public ReferenceDatasetInfo? AssetProvenance(string assetId) => inner.AssetProvenance(assetId);

    private sealed class CountingCatalog(IReferenceCatalog inner, FaithCountingReferences owner) : IReferenceCatalog
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
            owner.FindCalls++;
            return inner.Find(key);
        }
    }
}
