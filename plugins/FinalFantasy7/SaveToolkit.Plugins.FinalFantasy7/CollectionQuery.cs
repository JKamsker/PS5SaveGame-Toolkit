using System.Globalization;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.FinalFantasy7;

internal sealed record CollectionEntry(string Key, IReadOnlyDictionary<string, string> Values, Func<TableRow>? Render = null)
{
    public TableRow Row() => Render?.Invoke() ?? new(Key,
        Values.ToDictionary(pair => pair.Key, pair => (ViewNode)new TextNode(pair.Value), StringComparer.Ordinal));
}

/// <summary>Filters the full bounded model before rendering bindings for the requested page.</summary>
internal sealed class CollectionQuery(QueryableCollection metadata, Func<IEnumerable<CollectionEntry>> entries)
{
    public QueryableCollection Metadata { get; } = metadata;
    public QueryableTableNode View() => new(Metadata.Id, Metadata.Title, Metadata.Columns, Metadata.TotalCount)
    {
        Description = Metadata.Description,
    };

    public QueryableCollectionPage Query(QueryableCollectionQuery query)
    {
        if (query.Offset < 0 || query.Limit is < 1 or > QueryableCollectionLimits.MaxPageSize || query.Cursor is not null ||
            query.Text?.Length > QueryableCollectionLimits.MaxQueryLength || query.Filters.Count > QueryableCollectionLimits.MaxFilterCount)
        {
            throw new ArgumentException("Use bounded offset paging for FFVII collections.");
        }

        var columns = Metadata.Columns.ToDictionary(column => column.Key, StringComparer.Ordinal);
        if (query.Filters.Any(filter => !columns.ContainsKey(filter.Key) || filter.Value.Length > QueryableCollectionLimits.MaxFilterLength) ||
            query.SortColumn is not null && !columns.ContainsKey(query.SortColumn))
        {
            throw new ArgumentException("Unknown FFVII collection column or oversized filter.");
        }

        var all = entries().ToArray();
        IEnumerable<CollectionEntry> matches = all;
        if (!string.IsNullOrWhiteSpace(query.Text))
        {
            matches = matches.Where(entry => entry.Values.Values.Any(value => value.Contains(query.Text, StringComparison.OrdinalIgnoreCase)));
        }

        foreach (var filter in query.Filters)
        {
            matches = matches.Where(entry => entry.Values.TryGetValue(filter.Key, out var value) &&
                string.Equals(value, filter.Value, StringComparison.OrdinalIgnoreCase));
        }

        var filtered = matches.ToArray();
        IOrderedEnumerable<CollectionEntry> sorted;
        if (query.SortColumn is { } key)
        {
            IComparer<string> comparer = columns[key].Spec is IntegerSpec ? NumericComparer.Instance : StringComparer.OrdinalIgnoreCase;
            sorted = query.Descending
                ? filtered.OrderByDescending(entry => entry.Values.GetValueOrDefault(key, ""), comparer)
                : filtered.OrderBy(entry => entry.Values.GetValueOrDefault(key, ""), comparer);
            sorted = sorted.ThenBy(entry => entry.Key, StringComparer.Ordinal);
        }
        else
        {
            sorted = filtered.OrderBy(entry => entry.Key, StringComparer.Ordinal);
        }

        return new(Metadata.Id, Metadata.Columns, [.. sorted.Skip(query.Offset).Take(query.Limit).Select(entry => entry.Row())],
            all.Length, filtered.Length, query.Offset, query.Limit);
    }

    private sealed class NumericComparer : IComparer<string>
    {
        public static NumericComparer Instance { get; } = new();

        public int Compare(string? left, string? right) =>
            long.TryParse(left, CultureInfo.InvariantCulture, out var a) && long.TryParse(right, CultureInfo.InvariantCulture, out var b)
                ? a.CompareTo(b) : StringComparer.Ordinal.Compare(left, right);
    }
}
