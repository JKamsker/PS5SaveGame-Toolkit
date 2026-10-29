using System.Globalization;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.FinalFantasy7;

internal sealed record CollectionEntry(string Key, IReadOnlyDictionary<string, string> Values, Func<TableRow>? Render = null)
{
    public bool IsEmpty { get; init; }

    public TableRow Row() => (Render?.Invoke() ?? new(Key,
        Values.ToDictionary(pair => pair.Key, pair => (ViewNode)new TextNode(pair.Value), StringComparer.Ordinal)))
        with { IsEmpty = IsEmpty };
}

/// <summary>FFVII query policies over the shared full-model, page-only rendering engine.</summary>
internal sealed class CollectionQuery(QueryableCollection metadata, Func<IEnumerable<CollectionEntry>> entries)
{
    private readonly InMemoryCollection<CollectionEntry> _collection = new(metadata, entries, entry => entry.Key,
        [.. metadata.Columns.Select(Column)], entry => entry.Row(), new()
        {
            ClampPaging = false,
            ReverseDefaultOrderWhenDescending = false,
            NormalizeSearchText = text => string.IsNullOrWhiteSpace(text) ? null : text,
            IsEmpty = entry => entry.IsEmpty,
        });

    public QueryableCollection Metadata { get; } = metadata;
    public QueryableTableNode View() => Metadata.ToTableNode();

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

        return _collection.Query(query);
    }

    private static InMemoryColumn<CollectionEntry> Column(QueryableColumn column)
    {
        IComparer<string> comparer = column.Spec is IntegerSpec ? NumericComparer.Instance : StringComparer.OrdinalIgnoreCase;
        return new(column.Key)
        {
            SearchText = entry => entry.Values.GetValueOrDefault(column.Key),
            FilterText = entry => entry.Values.GetValueOrDefault(column.Key),
            Comparer = Comparer<CollectionEntry>.Create((left, right) => comparer.Compare(
                left.Values.GetValueOrDefault(column.Key, ""), right.Values.GetValueOrDefault(column.Key, ""))),
        };
    }

    private sealed class NumericComparer : IComparer<string>
    {
        public static NumericComparer Instance { get; } = new();

        public int Compare(string? left, string? right) =>
            long.TryParse(left, CultureInfo.InvariantCulture, out var a) && long.TryParse(right, CultureInfo.InvariantCulture, out var b)
                ? a.CompareTo(b) : StringComparer.Ordinal.Compare(left, right);
    }
}
