using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Sdk.Collections;

/// <summary>Queries a complete in-memory model, rendering only the selected page.</summary>
public sealed class InMemoryCollection<T>
{
    private readonly Func<IEnumerable<T>> _source;
    private readonly Func<T, string> _key;
    private readonly IReadOnlyDictionary<string, InMemoryColumn<T>> _columns;
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

        // Snapshot keys once, so tie-breaking and row identities cannot depend on page offsets.
        var all = _source().Select(item => new Entry(item, _key(item))).ToArray();
        var keys = new HashSet<string>(StringComparer.Ordinal);
        if (all.Any(entry => string.IsNullOrEmpty(entry.Key) || !keys.Add(entry.Key)))
            throw new InvalidOperationException("Collection row keys must be nonempty and unique across the complete model.");

        var matching = all.Where(entry => (string.IsNullOrEmpty(text) ||
                _columns.Values.Any(column => column.SearchText is { } get && _options.SearchMatches(get(entry.Item), text)) ||
                _options.ExtraSearchText is { } extra && _options.SearchMatches(extra(entry.Item), text)) &&
            query.Filters.All(filter => _options.FilterMatches(_columns[filter.Key].FilterText!(entry.Item), filter.Value)))
            .Select(entry => entry with { IsEmpty = _options.IsEmpty?.Invoke(entry.Item) == true }).ToArray();
        var includeEmpty = query.IncludeEmpty ?? Metadata.EmptyRows?.HideByDefault != true;
        var hidden = includeEmpty ? 0 : matching.Count(entry => entry.IsEmpty);
        var visible = includeEmpty ? matching : matching.Where(entry => !entry.IsEmpty);

        var comparer = query.SortColumn is { } sort ? _columns[sort].Comparer : _options.DefaultComparer;
        var descending = query.Descending && (query.SortColumn is not null || _options.ReverseDefaultOrderWhenDescending);
        IOrderedEnumerable<Entry> ordered;
        if (comparer is null)
            ordered = descending ? visible.OrderByDescending(entry => entry.Key, StringComparer.Ordinal)
                : visible.OrderBy(entry => entry.Key, StringComparer.Ordinal);
        else
            ordered = (descending ? visible.OrderByDescending(entry => entry.Item, comparer)
                : visible.OrderBy(entry => entry.Item, comparer)).ThenBy(entry => entry.Key, StringComparer.Ordinal);

        var offset = Math.Max(0, query.Offset);
        var limit = Math.Clamp(query.Limit, 1, QueryableCollectionLimits.MaxPageSize);
        var rows = ordered.Skip(offset).Take(limit).Select(entry =>
        {
            var row = _renderRow(entry.Item);
            if (!string.Equals(row.Key, entry.Key, StringComparison.Ordinal))
                throw new InvalidOperationException("Rendered row keys must match their native model keys.");
            return _options.IsEmpty is null ? row : row with { IsEmpty = entry.IsEmpty };
        }).ToArray();
        return new(Metadata.Id, Metadata.Columns, rows, all.Length, matching.Length - hidden, offset, limit)
        { HiddenEmptyCount = hidden };
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

    private sealed record Entry(T Item, string Key)
    {
        public bool IsEmpty { get; init; }
    }
}
