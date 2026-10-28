using SaveToolkit.Sdk.Collections;

namespace SaveToolkit.Plugins.Pragmata.Views;

internal static class CollectionQueries
{
    public static void Validate(QueryableCollectionQuery query, IReadOnlyList<QueryableColumn> columns)
    {
        if (query.Limit is < 1 or > QueryableCollectionLimits.MaxPageSize || query.Offset < 0)
            throw new ArgumentOutOfRangeException(nameof(query), "Invalid page size or offset.");
        if (query.Cursor is not null) throw new ArgumentException("Use offset paging for Pragmata collections.", nameof(query));
        if (query.Text?.Length > QueryableCollectionLimits.MaxQueryLength || query.Filters.Count > QueryableCollectionLimits.MaxFilterCount ||
            query.Filters.Any(f => f.Value.Length > QueryableCollectionLimits.MaxFilterLength))
            throw new ArgumentException("Query exceeds the collection protocol bounds.", nameof(query));
        if ((query.SortColumn is { } sort && !columns.Any(c => c.Key == sort)) || query.Filters.Keys.Any(key => !columns.Any(c => c.Key == key)))
            throw new ArgumentException("Unknown query column.", nameof(query));
    }
}
