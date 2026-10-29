using System.Globalization;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.Cyberpunk2077;

/// <summary>Plugin projections and compatibility policy around the shared full-model query engine.</summary>
internal sealed class InspectionCollection<T>
{
    private readonly InMemoryCollection<Entry> _collection;
    private readonly IReadOnlyDictionary<string, Func<T, string>> _values;

    public InspectionCollection(QueryableCollection metadata, IReadOnlyList<T> items, Func<T, string> key,
        IReadOnlyDictionary<string, Func<T, string>> values, Func<T, TableRow> row,
        Func<T, string>? extraSearch = null, Func<T, bool>? isEmpty = null)
    {
        _values = values;
        // Only native identities are cached. Text, numeric values and empty predicates stay live.
        var entries = new Lazy<Entry[]>(() => items.Select(item => new Entry(item, key(item))).ToArray());
        var columns = metadata.Columns.Select(column => new InMemoryColumn<Entry>(column.Key)
        {
            SearchText = entry => values[column.Key](entry.Item),
            FilterText = entry => values[column.Key](entry.Item),
            Comparer = Comparer<Entry>.Create((left, right) => Compare(values[column.Key](left.Item),
                values[column.Key](right.Item), column.Spec is IntegerSpec or NullableSpec { Inner: IntegerSpec })),
        }).ToArray();
        _collection = new(metadata, () => entries.Value, entry => entry.Key, columns, entry => row(entry.Item), new()
        {
            ExtraSearchText = extraSearch is null ? null : entry => extraSearch(entry.Item),
            // Empty markers without presentation metadata never hide rows in this plugin.
            IsEmpty = metadata.EmptyRows is not null && isEmpty is not null ? entry => isEmpty(entry.Item) : null,
        });
    }

    public QueryableCollection Metadata => _collection.Metadata;
    public QueryableTableNode View() => Metadata.ToTableNode();

    public QueryableCollectionPage Query(QueryableCollectionQuery query)
    {
        // Preserve plugin diagnostics and validation precedence; the SDK validates again before querying.
        ArgumentNullException.ThrowIfNull(query);
        if (query.Cursor is not null)
            throw new ArgumentException("This collection uses offset paging.", nameof(query));
        if (query.Text?.Length > QueryableCollectionLimits.MaxQueryLength ||
            query.Filters.Count > QueryableCollectionLimits.MaxFilterCount ||
            query.Filters.Any(pair => pair.Key.Length > QueryableCollectionLimits.MaxFilterLength || pair.Value.Length > QueryableCollectionLimits.MaxFilterLength))
            throw new ArgumentException("Collection query exceeds the supported bounds.", nameof(query));
        foreach (var filter in query.Filters.Keys)
        {
            if (!_values.ContainsKey(filter))
                throw new ArgumentException($"Unknown filter column '{filter}'.", nameof(query));
        }
        if (query.SortColumn is { } sort && !_values.ContainsKey(sort))
            throw new ArgumentException($"Unknown sort column '{sort}'.", nameof(query));
        return _collection.Query(query);
    }

    // Integer columns compare exact Int128 values, retaining ordinal fallback for absent/non-numeric text.
    // Text columns remain ordinal case-insensitive. The SDK supplies ascending native-key tie breaks.
    private static int Compare(string left, string right, bool numeric) => numeric
        ? Int128.TryParse(left, NumberStyles.Integer, CultureInfo.InvariantCulture, out var x) &&
          Int128.TryParse(right, NumberStyles.Integer, CultureInfo.InvariantCulture, out var y)
            ? x.CompareTo(y) : StringComparer.Ordinal.Compare(left, right)
        : StringComparer.OrdinalIgnoreCase.Compare(left, right);

    private sealed record Entry(T Item, string Key);
}
