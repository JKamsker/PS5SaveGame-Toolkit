using SaveToolkit.Sdk.Collections;

namespace SaveToolkit.Plugins.EldenRing;

internal static class BrowseQuery
{
    public static (IReadOnlyList<T> Page, int Filtered, int Offset, int Limit) Apply<T>(
        IEnumerable<T> source, QueryableCollectionQuery query, IReadOnlyList<QueryableColumn> columns,
        Func<T, string> key, Func<T, string, string> text, Func<T, string, long?> number)
    {
        if (query.Cursor is not null)
            throw new ArgumentException("This collection uses offset paging, not cursors.", nameof(query));
        var keys = columns.Select(column => column.Key).ToHashSet(StringComparer.Ordinal);
        if (query.SortColumn is { } sort && !keys.Contains(sort)
            || query.Filters.Keys.Any(filter => !keys.Contains(filter)))
            throw new ArgumentException("Unknown collection column.", nameof(query));
        if (query.Text?.Length > QueryableCollectionLimits.MaxQueryLength
            || query.Filters.Count > QueryableCollectionLimits.MaxFilterCount
            || query.Filters.Values.Any(value => value.Length > QueryableCollectionLimits.MaxFilterLength))
            throw new ArgumentException("Collection query exceeds protocol limits.", nameof(query));

        var filtered = source.Where(row => (string.IsNullOrWhiteSpace(query.Text)
                || columns.Any(column => text(row, column.Key).Contains(query.Text.Trim(), StringComparison.OrdinalIgnoreCase)))
            && query.Filters.All(filter => string.Equals(text(row, filter.Key), filter.Value, StringComparison.OrdinalIgnoreCase))).ToArray();
        var columnKey = query.SortColumn ?? columns[0].Key;
        IOrderedEnumerable<T> ordered;
        if (filtered.Any(row => number(row, columnKey).HasValue))
            ordered = query.Descending ? filtered.OrderByDescending(row => number(row, columnKey)) : filtered.OrderBy(row => number(row, columnKey));
        else
            ordered = query.Descending
                ? filtered.OrderByDescending(row => text(row, columnKey), StringComparer.OrdinalIgnoreCase)
                : filtered.OrderBy(row => text(row, columnKey), StringComparer.OrdinalIgnoreCase);
        var offset = Math.Max(0, query.Offset);
        var limit = Math.Clamp(query.Limit, 1, QueryableCollectionLimits.MaxPageSize);
        return (ordered.ThenBy(key, StringComparer.Ordinal).Skip(offset).Take(limit).ToArray(), filtered.Length, offset, limit);
    }
}
