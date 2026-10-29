using SaveToolkit.Sdk;

namespace SaveToolkit.Sdk.References;

/// <summary>A validated, fully loaded pack. Immutable and shared by every document that pins it.</summary>
internal sealed class DecodedReferencePack
{
    private readonly IReadOnlyDictionary<string, DecodedReferenceCatalog> _catalogs;
    private readonly IReadOnlyDictionary<string, DecodedAsset> _assets;

    internal DecodedReferencePack(
        ReferencePackInfo info,
        IReadOnlyDictionary<string, DecodedReferenceCatalog> catalogs,
        IReadOnlyDictionary<string, DecodedAsset> assets)
    {
        Info = info;
        _catalogs = catalogs;
        _assets = assets;
    }

    public ReferencePackInfo Info { get; }

    public string Id => Info.Id;

    public IReferenceCatalog? Catalog(string catalogId) => _catalogs.GetValueOrDefault(catalogId);

    /// <summary>Asset by pack-relative path.</summary>
    internal DecodedAsset? Asset(string path) => _assets.GetValueOrDefault(path);

    /// <summary>True when the pack applies to documents opened by <paramref name="pluginId"/> for <paramref name="titleId"/>.</summary>
    public bool AppliesTo(string pluginId, string? titleId) =>
        Info.PluginIds.Contains(pluginId, StringComparer.OrdinalIgnoreCase)
        && (Info.TitleIds.Count == 0 || (titleId is not null && Info.TitleIds.Contains(titleId, StringComparer.OrdinalIgnoreCase)));
}
