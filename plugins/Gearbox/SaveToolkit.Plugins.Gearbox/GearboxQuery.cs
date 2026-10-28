using System.Globalization;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.Gearbox;

internal sealed record GearboxQueryRow(string Key, IReadOnlyDictionary<string, string> Values, Func<TableRow> Build);

/// <summary>Shared full-collection query semantics; only the requested page creates bindings.</summary>
internal static class GearboxQuery
{
    public static QueryableCollectionPage Page(QueryableCollection collection, QueryableCollectionQuery query, IEnumerable<GearboxQueryRow> rows)
    {
        if (query.Cursor is not null) throw new ArgumentException("Gearbox collections use offset paging.");
        if (query.SortColumn is { } sort && collection.Columns.All(c => c.Key != sort) ||
            query.Filters.Keys.Any(key => collection.Columns.All(c => c.Key != key)))
            throw new ArgumentException("Unknown Gearbox sort/filter column.");
        var filtered = rows.Where(row => (string.IsNullOrEmpty(query.Text) || row.Values.Values.Any(v =>
                v.Contains(query.Text, StringComparison.OrdinalIgnoreCase))) && query.Filters.All(f =>
                string.Equals(row.Values.GetValueOrDefault(f.Key), f.Value, StringComparison.OrdinalIgnoreCase))).ToArray();
        var column = collection.Columns.First(c => c.Key == (query.SortColumn ?? collection.Columns[0].Key));
        IOrderedEnumerable<GearboxQueryRow> ordered;
        if (column.Spec is IntegerSpec)
            ordered = query.Descending ? filtered.OrderByDescending(row => Number(row.Values.GetValueOrDefault(column.Key)))
                : filtered.OrderBy(row => Number(row.Values.GetValueOrDefault(column.Key)));
        else
            ordered = query.Descending ? filtered.OrderByDescending(row => row.Values.GetValueOrDefault(column.Key), StringComparer.OrdinalIgnoreCase)
                : filtered.OrderBy(row => row.Values.GetValueOrDefault(column.Key), StringComparer.OrdinalIgnoreCase);
        var offset = Math.Max(0, query.Offset);
        var limit = Math.Clamp(query.Limit, 1, QueryableCollectionLimits.MaxPageSize);
        return new(collection.Id, collection.Columns, ordered.ThenBy(row => row.Key, StringComparer.Ordinal).Skip(offset).Take(limit)
            .Select(row => row.Build()).ToArray(), collection.TotalCount, filtered.Length, offset, limit);
    }

    private static long Number(string? value) => long.TryParse(value, CultureInfo.InvariantCulture, out var result) ? result : long.MinValue;
}
