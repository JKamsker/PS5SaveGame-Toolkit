using System.Globalization;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.Cyberpunk2077;

/// <summary>Query the complete model, materializing view cells only for the requested page.</summary>
internal sealed class InspectionCollection<T>(
    QueryableCollection metadata,
    IReadOnlyList<T> items,
    Func<T, string> key,
    IReadOnlyDictionary<string, Func<T, string>> values,
    Func<T, TableRow> row,
    Func<T, string>? extraSearch = null)
{
    public QueryableCollection Metadata { get; } = metadata;

    public QueryableTableNode View() => new(Metadata.Id, Metadata.Title, Metadata.Columns, Metadata.TotalCount)
        { Description = Metadata.Description };

    public QueryableCollectionPage Query(QueryableCollectionQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (query.Cursor is not null)
        {
            throw new ArgumentException("This collection uses offset paging.", nameof(query));
        }

        if (query.Text?.Length > QueryableCollectionLimits.MaxQueryLength ||
            query.Filters.Count > QueryableCollectionLimits.MaxFilterCount ||
            query.Filters.Any(pair => pair.Key.Length > QueryableCollectionLimits.MaxFilterLength || pair.Value.Length > QueryableCollectionLimits.MaxFilterLength))
        {
            throw new ArgumentException("Collection query exceeds the supported bounds.", nameof(query));
        }

        foreach (var filter in query.Filters.Keys)
        {
            if (!values.ContainsKey(filter))
            {
                throw new ArgumentException($"Unknown filter column '{filter}'.", nameof(query));
            }
        }

        var sort = query.SortColumn;
        if (sort is not null && !values.ContainsKey(sort))
        {
            throw new ArgumentException($"Unknown sort column '{sort}'.", nameof(query));
        }

        IEnumerable<T> matches = items;
        if (!string.IsNullOrEmpty(query.Text))
        {
            matches = matches.Where(item => values.Values.Any(get => get(item).Contains(query.Text, StringComparison.OrdinalIgnoreCase)) ||
                extraSearch?.Invoke(item).Contains(query.Text, StringComparison.OrdinalIgnoreCase) == true);
        }

        matches = matches.Where(item => query.Filters.All(filter =>
            string.Equals(values[filter.Key](item), filter.Value, StringComparison.OrdinalIgnoreCase)));
        var filtered = matches.ToArray();
        IOrderedEnumerable<T> ordered;
        if (sort is not null)
        {
            var numeric = Metadata.Columns.Single(column => column.Key == sort).Spec is IntegerSpec or NullableSpec { Inner: IntegerSpec };
            IComparer<string> comparer = numeric ? NumericComparer.Instance : StringComparer.OrdinalIgnoreCase;
            ordered = query.Descending ? filtered.OrderByDescending(values[sort], comparer) : filtered.OrderBy(values[sort], comparer);
            ordered = ordered.ThenBy(key, StringComparer.Ordinal);
        }
        else
        {
            ordered = query.Descending ? filtered.OrderByDescending(key, StringComparer.Ordinal) : filtered.OrderBy(key, StringComparer.Ordinal);
        }

        var offset = Math.Max(0, query.Offset);
        var limit = Math.Clamp(query.Limit, 1, QueryableCollectionLimits.MaxPageSize);
        return new QueryableCollectionPage(Metadata.Id, Metadata.Columns, ordered.Skip(offset).Take(limit).Select(row).ToArray(),
            items.Count, filtered.Length, offset, limit);
    }

    private sealed class NumericComparer : IComparer<string>
    {
        public static NumericComparer Instance { get; } = new();

        public int Compare(string? x, string? y) =>
            Int128.TryParse(x, NumberStyles.Integer, CultureInfo.InvariantCulture, out var left) &&
            Int128.TryParse(y, NumberStyles.Integer, CultureInfo.InvariantCulture, out var right)
                ? left.CompareTo(right) : StringComparer.Ordinal.Compare(x, y);
    }
}
