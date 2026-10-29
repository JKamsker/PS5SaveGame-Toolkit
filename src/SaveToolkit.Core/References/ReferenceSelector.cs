using SaveToolkit.Sdk.References;

namespace SaveToolkit.Core.References;

/// <summary>Host facade over the same deterministic selection policy used by embedded SDK contexts.</summary>
internal static class ReferenceSelector
{
    public static LoadedReferencePack? Select(
        IEnumerable<LoadedReferencePack> packs, string catalogId, string? gameBuild, IReadOnlyList<string> preferredLocales)
    {
        var candidates = packs.ToArray();
        var selected = PackSelector.Select(candidates.Select(p => p.Decoded), catalogId, gameBuild, preferredLocales);
        return candidates.FirstOrDefault(p => ReferenceEquals(p.Decoded, selected));
    }

    internal static int LocaleRank(string? locale, IReadOnlyList<string> preferred) => PackSelector.LocaleRank(locale, preferred);
}
