using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.SuckerPunch;

/// <summary>Indexes immutable observed identifiers against the document's pinned references on first query.</summary>
internal sealed class TsushimaEquipmentQuery(IReferenceContext references, IReadOnlyList<TsushimaIdentifier> identifiers)
{
    private readonly CollectionQueryCache<Entry> _queries = new();
    private Entry[]? _entries;
    private IReferenceCatalog? _catalog;
    public int TotalCount { get; } = identifiers.Count(identifier => identifier.Group == IdentifierGroup.Equipment);

    public QueryableCollectionPage Query(QueryableCollectionQuery query, IReadOnlyList<QueryableColumn> columns,
        Func<TsushimaIdentifier, ReferenceRecord?, IReferenceCatalog?, TableRow> render)
    {
        if (_entries is null)
        {
            _catalog = references.Catalog(TsushimaIds.Equipment);
            _entries = identifiers.Where(identifier => identifier.Group == IdentifierGroup.Equipment)
                .Select(identifier => new Entry(identifier, _catalog?.Find(identifier.Name))).ToArray();
        }
        var matches = _queries.Get(query, () => Select(query));
        return new(TsushimaReferenceViews.EquipmentCollectionId, columns,
            matches.Skip(query.Offset).Take(query.Limit).Select(entry => render(entry.Identifier, entry.Record, _catalog)).ToArray(),
            TotalCount, matches.Length, query.Offset, query.Limit);
    }

    private Entry[] Select(QueryableCollectionQuery query)
    {
        var filtered = _entries!.Where(entry => Matches(entry, query));
        // Offsets are numeric; avoid allocating one formatted string per model row and query.
        IEnumerable<Entry> ordered = query.SortColumn == "location"
            ? filtered.OrderBy(entry => entry.Identifier.Offset)
            : filtered.OrderBy(entry => SortValue(entry, query.SortColumn), StringComparer.Ordinal)
                .ThenBy(entry => entry.Identifier.Offset);
        if (query.Descending) ordered = ordered.Reverse();
        return ordered.ToArray();
    }

    private static bool Matches(Entry entry, QueryableCollectionQuery query)
    {
        if (query.Text is { Length: > 0 } text &&
            !entry.Identifier.Name.Contains(text, StringComparison.OrdinalIgnoreCase) &&
            !(entry.Record?.Name.Contains(text, StringComparison.OrdinalIgnoreCase) ?? false)) return false;
        foreach (var (key, value) in query.Filters)
        {
            var actual = key switch
            {
                "raw" => entry.Identifier.Name,
                "reference" or TsushimaAssets.IconColumn => entry.Record?.Name ?? entry.Identifier.Name,
                "category" => entry.Record?.Category ?? "Unknown",
                "mapping" => MappingConfidence(entry.Record),
                "state" => "Unknown",
                _ => throw new ArgumentException($"Unknown equipment filter '{key}'.", nameof(query)),
            };
            if (!actual.Contains(value, StringComparison.OrdinalIgnoreCase)) return false;
        }
        return true;
    }

    private static string SortValue(Entry entry, string? column) => column switch
    {
        null or "raw" => entry.Identifier.Name,
        "reference" or TsushimaAssets.IconColumn => entry.Record?.Name ?? entry.Identifier.Name,
        "category" => entry.Record?.Category ?? "Unknown",
        "mapping" => MappingConfidence(entry.Record),
        "state" => "Unknown",
        _ => throw new ArgumentException($"Unknown equipment sort column '{column}'.", nameof(column)),
    };

    private static string MappingConfidence(ReferenceRecord? record) =>
        record?.Attributes.GetValueOrDefault("mappingConfidence") switch
        { "Verified" => "Verified", "Inferred" => "Inferred", _ => "Unknown" };

    private sealed record Entry(TsushimaIdentifier Identifier, ReferenceRecord? Record);
}
