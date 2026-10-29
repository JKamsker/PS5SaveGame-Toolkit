
namespace SaveToolkit.Sdk.References;

/// <summary>
/// Deterministic choice of the pack that provides a catalog for a document:
/// <list type="number">
/// <item>packs made for another build are skipped when the plugin names its build;</item>
/// <item>exact build match, then build-agnostic packs, then build-specific packs when the build is unknown;</item>
/// <item>preferred locale (exact tag, then same language), then language-neutral, then any other locale;</item>
/// <item>user packs over embedded packs;</item>
/// <item>higher version, then pack id (ordinal) as the final tie-breaker.</item>
/// </list>
/// </summary>
internal static class PackSelector
{
    public static DecodedReferencePack? Select(
        IEnumerable<DecodedReferencePack> packs, string catalogId, string? gameBuild, IReadOnlyList<string> preferredLocales) =>
        packs
            .Where(p => p.HasCatalog(catalogId) && BuildAllowed(p.Info, gameBuild))
            .OrderBy(p => BuildRank(p.Info, gameBuild))
            .ThenBy(p => LocaleRank(p.Info.Locale, preferredLocales))
            .ThenBy(p => p.Info.Origin == ReferencePackOrigin.User ? 0 : 1)
            .ThenByDescending(p => ParseVersion(p.Info.Version))
            .ThenBy(p => p.Id, StringComparer.Ordinal)
            .FirstOrDefault();

    internal static int LocaleRank(string? locale, IReadOnlyList<string> preferred)
    {
        if (locale is null)
        {
            return 2 * preferred.Count;
        }

        var best = (2 * preferred.Count) + 1;
        for (var i = 0; i < preferred.Count; i++)
        {
            if (string.Equals(locale, preferred[i], StringComparison.OrdinalIgnoreCase))
            {
                return 2 * i;
            }

            if (string.Equals(Language(locale), Language(preferred[i]), StringComparison.OrdinalIgnoreCase))
            {
                best = Math.Min(best, (2 * i) + 1);
            }
        }

        return best;
    }

    private static bool BuildAllowed(ReferencePackInfo pack, string? build) =>
        build is null || pack.GameBuilds.Count == 0 || pack.GameBuilds.Contains(build, StringComparer.OrdinalIgnoreCase);

    private static int BuildRank(ReferencePackInfo pack, string? build) => (build, pack.GameBuilds.Count) switch
    {
        (not null, > 0) => 0,
        (_, 0) => 1,
        _ => 2,
    };

    private static string Language(string tag) => tag.Split('-')[0];

    private static Version ParseVersion(string version) =>
        Version.TryParse(version.Split('-', '+')[0], out var v) ? v : new Version(0, 0);
}
