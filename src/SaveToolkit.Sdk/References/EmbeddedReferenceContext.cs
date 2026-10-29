using System.Reflection;

namespace SaveToolkit.Sdk.References;

/// <summary>
/// Validated embedded reference packs, read from manifest resources without file or network I/O.
/// Load once per plugin assembly, then <see cref="Pin"/> for each document's plugin and title.
/// </summary>
/// <remarks>
/// Uses the host's decoder, hash normalization, catalog queries and build/locale/version selection.
/// Only embedded packs are considered; supplied host contexts and game-specific joins remain plugin policy.
/// No global cache is retained. Catalogs and individual asset buffers are materialized once on first use and shared between pinned contexts.
/// Concurrent reads are supported when callers treat returned metadata, nested collections and asset
/// buffers as read-only. Pin copies locale preferences. Limits apply per pack, not to the whole assembly.
/// </remarks>
public sealed class EmbeddedReferenceContext : IReferenceContext
{
    private readonly IReadOnlyList<DecodedReferencePack> _packs;
    private readonly IReadOnlyList<string> _preferredLocales;

    private EmbeddedReferenceContext(IReadOnlyList<DecodedReferencePack> packs, IReadOnlyList<string> preferredLocales)
    {
        _packs = packs;
        _preferredLocales = preferredLocales;
        Packs = Array.AsReadOnly(packs.Select(p => p.Info).ToArray());
    }

    /// <summary>All candidates in this context, ordered by ordinal pack id.</summary>
    public IReadOnlyList<ReferencePackInfo> Packs { get; }

    /// <summary>
    /// Validates every <c>refpacks/&lt;folder&gt;/</c> pack in an assembly; catalogs and images load on first use. Backslashes in resource names
    /// are normalized as in the host. The returned unfiltered collection can be cached and pinned per open.
    /// </summary>
    /// <exception cref="EmbeddedReferencePackException">Any discovered pack is invalid or has a duplicate id.</exception>
    /// <exception cref="ArgumentNullException">The assembly is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A supplied limit is negative.</exception>
    public static EmbeddedReferenceContext Load(Assembly assembly, EmbeddedReferencePackLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        limits ??= new();
        limits.Validate();
        var packs = new Dictionary<string, DecodedReferencePack>(StringComparer.Ordinal);
        var strings = new ReferenceStringPool();
        foreach (var source in EmbeddedPackSource.Discover(assembly))
        {
            var pack = PackDecoder.Load(source, ReferencePackOrigin.Embedded, limits, strings);
            if (!packs.TryAdd(pack.Id, pack))
                throw new EmbeddedReferencePackException($"Pack id '{pack.Id}' is already loaded; duplicate embedded pack.");
        }

        return new([.. packs.Values.OrderBy(p => p.Id, StringComparer.Ordinal)], ["en"]);
    }

    /// <summary>Decodes an assembly's resources and pins packs matching the plugin and title.</summary>
    /// <exception cref="EmbeddedReferencePackException">Any discovered pack is invalid or has a duplicate id.</exception>
    /// <exception cref="ArgumentNullException">The assembly or plugin ID is null.</exception>
    /// <exception cref="ArgumentException">The plugin ID is blank or locale preferences contain null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A supplied limit is negative.</exception>
    public static EmbeddedReferenceContext Load(Assembly assembly, string pluginId, string? titleId,
        EmbeddedReferencePackLimits? limits = null, IReadOnlyList<string>? preferredLocales = null) =>
        Load(assembly, limits).Pin(pluginId, titleId, preferredLocales);

    /// <summary>
    /// Returns a context filtered by plugin and title (case-insensitive). Null titles exclude title-specific
    /// packs. Locale preference defaults to English; an empty list prefers language-neutral packs.
    /// Pin the unfiltered loaded collection for each open; pinning an already filtered context only narrows it.
    /// </summary>
    /// <exception cref="ArgumentNullException">The plugin ID is null.</exception>
    /// <exception cref="ArgumentException">The plugin ID is blank or locale preferences contain null.</exception>
    public EmbeddedReferenceContext Pin(string pluginId, string? titleId, IReadOnlyList<string>? preferredLocales = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginId);
        if (preferredLocales is not null && preferredLocales.Any(locale => locale is null))
            throw new ArgumentException("Preferred locales must be non-null.", nameof(preferredLocales));
        return new([.. _packs.Where(p => p.AppliesTo(pluginId, titleId))],
            [.. preferredLocales ?? _preferredLocales]);
    }

    /// <inheritdoc />
    public IReferenceCatalog? Catalog(string catalogId, string? gameBuild = null) =>
        PackSelector.Select(_packs, catalogId, gameBuild, _preferredLocales)?.Catalog(catalogId);

    /// <inheritdoc />
    public AssetContent? GetAsset(string assetId) => FindAsset(assetId)?.Content;

    /// <inheritdoc />
    public ReferenceDatasetInfo? AssetProvenance(string assetId) => FindAsset(assetId)?.Dataset;

    private DecodedAsset? FindAsset(string assetId) =>
        PackPaths.TrySplitAsset(assetId, out var packId, out var path)
            ? _packs.FirstOrDefault(p => p.Id == packId)?.Asset(path)
            : null;
}
