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
    private readonly IReadOnlyList<LoadedReferencePack> _packs;
    private readonly IReadOnlyList<string> _preferredLocales;
    private readonly ConcurrentDictionary<(string Pack, string Dataset), byte> _used = new();
    private readonly ConcurrentDictionary<string, string?> _catalogBuilds = new(StringComparer.Ordinal);

    public ReferenceContext(IReadOnlyList<LoadedReferencePack> packs, IReadOnlyList<string> preferredLocales)
    {
        _packs = packs;
        _preferredLocales = preferredLocales;
        Packs = [.. packs.Select(p => p.Info)];
    }

    public static ReferenceContext None { get; } = new([], []);

    public IReadOnlyList<ReferencePackInfo> Packs { get; }

    public IReferenceCatalog? Catalog(string catalogId, string? gameBuild = null)
    {
        var catalog = Select(catalogId, gameBuild);
        if (catalog is not null)
        {
            _used.TryAdd((catalog.Pack.Id, catalog.Id), 0);
            _catalogBuilds[catalogId] = gameBuild;
        }

        return catalog;
    }

    public AssetContent? GetAsset(string assetId)
    {
        var asset = FindAsset(assetId);
        if (asset is not null)
        {
            _used.TryAdd((asset.Dataset.PackId, asset.Dataset.Id), 0);
        }

        return asset?.Content;
    }

    public ReferenceDatasetInfo? AssetProvenance(string assetId) => FindAsset(assetId)?.Dataset;

    /// <summary>
    /// Catalog lookup on behalf of the host (API search, F4 picker). Uses the build the plugin asked for,
    /// so the browser sees the same catalog as the plugin. Does not count as plugin usage.
    /// </summary>
    public IReferenceCatalog? CatalogForHost(string catalogId) =>
        Select(catalogId, _catalogBuilds.GetValueOrDefault(catalogId));

    /// <summary>Asset lookup on behalf of the host; restricted to the pinned packs.</summary>
    public AssetContent? AssetForHost(string assetId) => FindAsset(assetId)?.Content;

    /// <summary>Every pinned dataset, used ones first.</summary>
    public IReadOnlyList<PinnedDataset> Datasets() =>
    [
        .. _packs
            .SelectMany(p => p.Info.Datasets.Select(d => new PinnedDataset(p.Info, d, _used.ContainsKey((p.Id, d.Id)))))
            .OrderBy(d => d.Used ? 0 : 1)
            .ThenBy(d => d.Pack.Id, StringComparer.Ordinal)
            .ThenBy(d => d.Dataset.Id, StringComparer.Ordinal),
    ];

    private IReferenceCatalog? Select(string catalogId, string? gameBuild) =>
        ReferenceSelector.Select(_packs, catalogId, gameBuild, _preferredLocales)?.Catalog(catalogId);

    private LoadedAsset? FindAsset(string assetId) =>
        PackPaths.TrySplitAsset(assetId, out var packId, out var path)
            ? _packs.FirstOrDefault(p => p.Id == packId)?.Asset(path)
            : null;
}
