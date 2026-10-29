using System.Collections.Concurrent;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.References;

namespace SaveToolkit.Core.References;

/// <summary>A dataset pinned to a document, and whether the plugin has resolved it.</summary>
public sealed record PinnedDataset(ReferencePackInfo Pack, ReferenceDatasetInfo Dataset, bool Used);

/// <summary>
/// The packs pinned to one document at open time. Immutable apart from usage tracking, which records
/// which datasets the plugin actually resolved so the Sources panel can list what is in use.
/// </summary>
public sealed class ReferenceContext : IReferenceContext
{
    // Host lookups can name arbitrary missing catalogs; keep per-document retained state bounded.
    private const int MaxCachedSelections = 256;
    private readonly IReadOnlyList<LoadedReferencePack> _packs;
    private readonly IReadOnlyList<string> _preferredLocales;
    private readonly ConcurrentDictionary<(string Pack, string Dataset), byte> _used = new();
    private readonly Lock _catalogGate = new();
    private readonly Dictionary<(string Id, string? Build), CatalogResolution> _catalogs = [];
    private readonly Dictionary<string, string?> _catalogBuilds = new(StringComparer.Ordinal);

    public ReferenceContext(IReadOnlyList<LoadedReferencePack> packs, IReadOnlyList<string> preferredLocales)
    {
        _packs = packs.ToArray();
        _preferredLocales = preferredLocales.ToArray();
        Packs = [.. packs.Select(p => p.Info)];
    }

    public static ReferenceContext None { get; } = new([], []);

    public IReadOnlyList<ReferencePackInfo> Packs { get; }

    public IReferenceCatalog? Catalog(string catalogId, string? gameBuild = null)
    {
        lock (_catalogGate)
        {
            var resolution = Resolve(catalogId, gameBuild);
            var catalog = resolution.Catalog;
            if (catalog is not null && !resolution.Used)
            {
                MarkUsed(catalog.Pack.Id, catalog.Id);

                // Records point at icons of their pack; attribute them on the first plugin lookup.
                foreach (var assets in catalog.Pack.Datasets)
                {
                    if (assets.Kind == ReferenceDatasetKind.Assets) MarkUsed(assets.PackId, assets.Id);
                }

                // A host lookup may have cached selection, but must not pin the plugin's build.
                if (_catalogBuilds.GetValueOrDefault(catalogId) is null) _catalogBuilds[catalogId] = gameBuild;
                resolution.Used = true;
            }

            return catalog;
        }
    }

    public AssetContent? GetAsset(string assetId)
    {
        var asset = FindAsset(assetId);
        if (asset is not null)
        {
            MarkUsed(asset.Dataset.PackId, asset.Dataset.Id);
        }

        return asset?.Content;
    }

    public ReferenceDatasetInfo? AssetProvenance(string assetId) => FindAsset(assetId)?.Dataset;

    /// <summary>
    /// Catalog lookup on behalf of the host (API search, F4 picker). Uses the build the plugin asked for,
    /// so the browser sees the same catalog as the plugin. Does not count as plugin usage.
    /// </summary>
    public IReferenceCatalog? CatalogForHost(string catalogId)
    {
        lock (_catalogGate)
        {
            return Resolve(catalogId, _catalogBuilds.GetValueOrDefault(catalogId)).Catalog;
        }
    }

    /// <summary>Asset lookup on behalf of the host; restricted to the pinned packs. Shown icons count as used.</summary>
    public AssetContent? AssetForHost(string assetId) => GetAsset(assetId);

    /// <summary>Every pinned dataset, used ones first.</summary>
    public IReadOnlyList<PinnedDataset> Datasets() =>
    [
        .. _packs
            .SelectMany(p => p.Info.Datasets.Select(d => new PinnedDataset(p.Info, d, _used.ContainsKey((p.Id, d.Id)))))
            .OrderBy(d => d.Used ? 0 : 1)
            .ThenBy(d => d.Pack.Id, StringComparer.Ordinal)
            .ThenBy(d => d.Dataset.Id, StringComparer.Ordinal),
    ];

    private void MarkUsed(string packId, string datasetId) => _used.TryAdd((packId, datasetId), 0);

    // Called under _catalogGate to serialize cached selection and its first-use bookkeeping.
    private CatalogResolution Resolve(string catalogId, string? gameBuild)
    {
        var key = (catalogId, gameBuild);
        if (!_catalogs.TryGetValue(key, out var resolution))
        {
            var catalog = ReferenceSelector.Select(_packs, catalogId, gameBuild, _preferredLocales)?.Catalog(catalogId);
            resolution = new CatalogResolution(catalog);
            if (_catalogs.Count < MaxCachedSelections) _catalogs.Add(key, resolution);
        }

        return resolution;
    }

    private DecodedAsset? FindAsset(string assetId) =>
        PackPaths.TrySplitAsset(assetId, out var packId, out var path)
            ? _packs.FirstOrDefault(p => p.Id == packId)?.Asset(path)
            : null;

    private sealed class CatalogResolution(IReferenceCatalog? catalog)
    {
        public IReferenceCatalog? Catalog { get; } = catalog;
        public bool Used { get; set; }
    }
}
