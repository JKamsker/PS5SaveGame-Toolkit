namespace SaveToolkit.Sdk.References;

/// <summary>
/// Read-only reference data pinned to one document when it opens: names, descriptions, limits, icons,
/// each with attribution. The framework loads, validates and caches packs; plugins only read.
/// The context is immutable for the document's lifetime (importing a new pack affects documents
/// opened afterwards), and safe to keep in the document.
/// </summary>
/// <remarks>
/// Always handle the no-pack case: <see cref="Catalog"/> returns null and the plugin falls back to
/// raw ids or its own embedded tables. <see cref="Empty"/> is what tests and the old
/// <see cref="IGamePlugin.Open(Saves.ISaveFile)"/> path see.
/// Plugins can opt into <see cref="EmbeddedReferenceContext"/> to decode their assembly's embedded
/// packs for direct opens, while preserving the host-supplied context for hosted opens.
/// </remarks>
public interface IReferenceContext
{
    /// <summary>A context without packs.</summary>
    static IReferenceContext Empty { get; } = new EmptyReferenceContext();

    /// <summary>Packs pinned to this document (all candidates for this plugin and title).</summary>
    IReadOnlyList<ReferencePackInfo> Packs { get; }

    /// <summary>
    /// Deterministic identity of the pinned pack contents, suitable for a host compatibility
    /// boundary. It is null when no pack is pinned; plugins must not manufacture it from file I/O.
    /// </summary>
    string? ContentIdentity => Packs.Count == 0 ? null : string.Join("|", Packs
        .OrderBy(pack => pack.Id, StringComparer.Ordinal)
        .ThenBy(pack => pack.ContentFingerprint, StringComparer.Ordinal)
        .Select(pack => $"{pack.Id}@{pack.Version}:{pack.ContentFingerprint ?? "unknown"}"));

    /// <summary>
    /// Best catalog with this id for the document, or null. With <paramref name="gameBuild"/> set,
    /// packs made for other builds are skipped and an exact build match wins. Ties go to the preferred
    /// locale, then user packs over embedded ones, then the higher version.
    /// </summary>
    IReferenceCatalog? Catalog(string catalogId, string? gameBuild = null);

    /// <summary>Image bytes for a pack-qualified asset id (<see cref="ReferenceRecord.AssetId"/>), or null.</summary>
    AssetContent? GetAsset(string assetId);

    /// <summary>Dataset (with attribution) that provides an asset, or null.</summary>
    ReferenceDatasetInfo? AssetProvenance(string assetId);
}

internal sealed class EmptyReferenceContext : IReferenceContext
{
    public IReadOnlyList<ReferencePackInfo> Packs => [];
    public string? ContentIdentity => null;

    public IReferenceCatalog? Catalog(string catalogId, string? gameBuild = null) => null;

    public AssetContent? GetAsset(string assetId) => null;

    public ReferenceDatasetInfo? AssetProvenance(string assetId) => null;
}
