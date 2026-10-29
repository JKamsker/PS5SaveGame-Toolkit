using SaveToolkit.Sdk.References;

namespace SaveToolkit.Plugins.FinalFantasy16;

// Assembly resources only. Runtime plugins never read files or download reference data.
internal static class FaithEmbeddedCatalogs
{
    private static readonly EmbeddedReferenceContext Embedded =
        EmbeddedReferenceContext.Load(typeof(FaithEmbeddedCatalogs).Assembly);

    // Keep the existing assembly-wide fallback scope; FaithCatalog owns host and source preference.
    private static readonly IReadOnlyDictionary<string, IReferenceCatalog> Catalogs = Embedded.Packs
        .SelectMany(pack => pack.Datasets).Where(dataset => dataset.Kind == ReferenceDatasetKind.Records)
        .Select(dataset => dataset.Id).Distinct(StringComparer.Ordinal)
        .ToDictionary(id => id, id => Embedded.Catalog(id)!, StringComparer.Ordinal);

    public static ReferenceRecord? Find(string catalog, string key) => Catalogs.GetValueOrDefault(catalog)?.Find(key);

    // The historical fallback sorted unsorted queries by ascending key, even with Descending set.
    public static ReferencePage Search(string catalog, ReferenceQuery query) =>
        Catalogs.GetValueOrDefault(catalog)?.Search(query.SortBy is null
            ? query with { SortBy = ReferenceSortKey.Key, Descending = false }
            : query) ?? new([], 0, Math.Max(0, query.Offset));
}
