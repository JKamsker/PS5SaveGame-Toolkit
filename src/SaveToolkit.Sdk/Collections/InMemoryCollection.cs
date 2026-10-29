using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Sdk.Collections;

/// <summary>Queries a complete in-memory model, rendering only the selected page.</summary>
public sealed class InMemoryCollection<T>
{
    private readonly Func<IEnumerable<T>> _source;
    private readonly Func<T, string> _key;
    private readonly Dictionary<string, InMemoryColumn<T>> _columns;
    private readonly Func<T, TableRow> _renderRow;
    private readonly InMemoryCollectionOptions<T> _options;

    /// <summary>Connects complete-model projections and stable native keys to a renderer invoked only for selected rows.</summary>
    public InMemoryCollection(QueryableCollection metadata, Func<IEnumerable<T>> source,
        Func<T, string> key, IReadOnlyList<InMemoryColumn<T>> columns, Func<T, TableRow> renderRow,
        InMemoryCollectionOptions<T>? options = null)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(columns);
        ArgumentNullException.ThrowIfNull(renderRow);
        Metadata = metadata with { Columns = metadata.Columns.ToArray() };
        _source = source;
        _key = key;
        _columns = columns.ToDictionary(column => column.Key, StringComparer.Ordinal);
        _renderRow = renderRow;
        _options = options ?? new();
        var metadataKeys = Metadata.Columns.Select(column => column.Key).ToHashSet(StringComparer.Ordinal);
        if (metadataKeys.Count != Metadata.Columns.Count || !_columns.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(metadataKeys))
            throw new ArgumentException("Query adapters must match the unique metadata columns.", nameof(columns));
        if (Metadata.EmptyRows is { } empty && (!metadataKeys.Contains(empty.LabelColumnKey) || _options.IsEmpty is null))
            throw new ArgumentException("Empty-row metadata requires a label column and a model predicate.", nameof(options));
    }

    /// <summary>Collection metadata with a copied column list, used by every returned page.</summary>
    public QueryableCollection Metadata { get; }

    /// <summary>Filters, counts and sorts the complete source before rendering one bounded offset page; cursors are unsupported.</summary>
    public QueryableCollectionPage Query(QueryableCollectionQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);
        Validate(query);
        var text = _options.NormalizeSearchText(query.Text);
        if (text?.Length > QueryableCollectionLimits.MaxQueryLength)
            throw new ArgumentException("Normalized search text exceeds the supported bound.", nameof(query));

        // Capture/validate every native key before matching, including off-page and hidden rows.
        // Reuse the same value-type buffer for matching and ordering; projections remain live per query.
        var entries = Snapshot();
        var total = entries.Count;
        var filters = ResolveFilters(query.Filters);
        var includeEmpty = query.IncludeEmpty ?? Metadata.EmptyRows?.HideByDefault != true;
        var hidden = 0;
        var visible = 0;
        for (var i = 0; i < total; i++)
        {
            var entry = entries[i];
            if (!Matches(entry.Item, text, filters)) continue;
            var isEmpty = _options.IsEmpty?.Invoke(entry.Item) == true;
            if (!includeEmpty && isEmpty) { hidden++; continue; }
            entries[visible++] = entry with { IsEmpty = isEmpty };
        }
        entries.RemoveRange(visible, total - visible);

        var comparer = query.SortColumn is { } sort ? _columns[sort].Comparer : _options.DefaultComparer;
        var descending = query.Descending && (query.SortColumn is not null || _options.ReverseDefaultOrderWhenDescending);
        entries.Sort(new EntryComparer(comparer, descending));

        var offset = Math.Max(0, query.Offset);
        var limit = Math.Clamp(query.Limit, 1, QueryableCollectionLimits.MaxPageSize);
        var rowCount = Math.Min(limit, Math.Max(0, visible - offset));
        var rows = rowCount == 0 ? [] : new TableRow[rowCount];
        for (var i = 0; i < rowCount; i++)
        {
            var entry = entries[offset + i];
            var row = _renderRow(entry.Item);
            if (!string.Equals(row.Key, entry.Key, StringComparison.Ordinal))
                throw new InvalidOperationException("Rendered row keys must match their native model keys.");
            rows[i] = _options.IsEmpty is null || row.IsEmpty == entry.IsEmpty ? row : row with { IsEmpty = entry.IsEmpty };
        }
        return new(Metadata.Id, Metadata.Columns, rows, total, visible, offset, limit)
        { HiddenEmptyCount = hidden };
    }

    private List<Entry> Snapshot()
    {
        var source = _source();
        var capacity = source.TryGetNonEnumeratedCount(out var count) ? count : 0;
        var entries = new List<Entry>(capacity);
        var keys = new HashSet<string>(capacity, StringComparer.Ordinal);
        foreach (var item in source)
        {
            var key = _key(item);
            if (string.IsNullOrEmpty(key) || !keys.Add(key))
                throw new InvalidOperationException("Collection row keys must be nonempty and unique across the complete model.");
            entries.Add(new(item, key));
        }
        return entries;
    }

    private ColumnFilter[] ResolveFilters(IReadOnlyDictionary<string, string> filters)
    {
        if (filters.Count == 0) return [];
        var resolved = new ColumnFilter[filters.Count];
        var index = 0;
        foreach (var filter in filters)
            resolved[index++] = new(_columns[filter.Key].FilterText!, filter.Value);
        return resolved;
    }

    private bool Matches(T item, string? text, ColumnFilter[] filters)
    {
        if (!string.IsNullOrEmpty(text) && !MatchesSearch(item, text)) return false;
        foreach (var filter in filters)
            if (!_options.FilterMatches(filter.GetText(item), filter.Value)) return false;
        return true;
    }

    private bool MatchesSearch(T item, string text)
    {
        foreach (var column in _columns.Values)
            if (column.SearchText is { } get && _options.SearchMatches(get(item), text)) return true;
        return _options.ExtraSearchText is { } extra && _options.SearchMatches(extra(item), text);
    }

    private void Validate(QueryableCollectionQuery query)
    {
        if (query.Cursor is not null || query.Text?.Length > QueryableCollectionLimits.MaxQueryLength ||
            query.Filters.Count > QueryableCollectionLimits.MaxFilterCount ||
            !_options.ClampPaging && (query.Offset < 0 || query.Limit is < 1 or > QueryableCollectionLimits.MaxPageSize))
            throw new ArgumentException("Use bounded offset paging and search text for this collection.", nameof(query));
        if (query.SortColumn is { } sort && (!_columns.TryGetValue(sort, out var column) || column.Comparer is null))
            throw new ArgumentException("Unknown or unsupported sort column.", nameof(query));
        foreach (var filter in query.Filters)
        {
            if (filter.Key.Length > QueryableCollectionLimits.MaxFilterLength || filter.Value.Length > QueryableCollectionLimits.MaxFilterLength ||
                !_columns.TryGetValue(filter.Key, out var filterColumn) || filterColumn.FilterText is null)
                throw new ArgumentException("Unknown, unsupported or oversized column filter.", nameof(query));
        }
    }

    private readonly record struct ColumnFilter(Func<T, string?> GetText, string Value);
    private readonly record struct Entry(T Item, string Key, bool IsEmpty = false);

    private sealed class EntryComparer(IComparer<T>? comparer, bool descending) : IComparer<Entry>
    {
        public int Compare(Entry left, Entry right)
        {
            if (comparer is null)
                return descending ? StringComparer.Ordinal.Compare(right.Key, left.Key)
                    : StringComparer.Ordinal.Compare(left.Key, right.Key);
            // Swap operands rather than negating: a native comparer may return int.MinValue.
            var order = descending ? comparer.Compare(right.Item, left.Item) : comparer.Compare(left.Item, right.Item);
            return order != 0 ? order : StringComparer.Ordinal.Compare(left.Key, right.Key);
        }
    }
}
