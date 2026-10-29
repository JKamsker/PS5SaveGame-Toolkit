using System.Globalization;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.Insomniac;

internal sealed record RiftCell(string Text, Func<ViewNode> Build, decimal? Number = null)
{
    internal static RiftCell TextOnly(string text) => new(text, () => new TextNode(text));
}

internal sealed record RiftRow(string Key, Dictionary<string, RiftCell> Cells);

/// <summary>F8 paging over immutable parsed state. View cells and bindings are
/// materialized only for the requested page, including filtered selection scopes.</summary>
internal sealed class RiftCollections
{
    private sealed class Entry(QueryableCollection metadata, RiftRow[] rows)
    {
        private sealed record SortOrder(string Column, bool Descending, RiftRow[] Rows);
        private readonly object _sortLock = new();
        private SortOrder? _lastSort;

        internal QueryableCollection Metadata { get; } = metadata;
        internal RiftRow[] Rows { get; } = rows;
        internal HashSet<string> ColumnKeys { get; } = metadata.Columns.Select(c => c.Key).ToHashSet(StringComparer.Ordinal);

        internal RiftRow[] OrderedRows(string? column, bool descending)
        {
            if (column is null) return Rows;
            lock (_sortLock)
            {
                if (_lastSort is { } cached && cached.Column == column && cached.Descending == descending)
                    return cached.Rows;
                var ordered = descending
                    ? Rows.OrderByDescending(row => row.Cells[column], CellComparer)
                    : Rows.OrderBy(row => row.Cells[column], CellComparer);
                var sorted = ordered.ThenBy(row => row.Key, StringComparer.Ordinal).ToArray();
                // Keep at most one extra ordering per immutable collection, never one per query.
                _lastSort = new(column, descending, sorted);
                return sorted;
            }
        }
    }

    private static readonly Comparer<RiftCell> CellComparer = Comparer<RiftCell>.Create((left, right) =>
    {
        if (left.Number is { } a && right.Number is { } b) return a.CompareTo(b);
        if (left.Number.HasValue != right.Number.HasValue) return left.Number.HasValue ? -1 : 1;
        return StringComparer.OrdinalIgnoreCase.Compare(left.Text, right.Text);
    });
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);

    internal IReadOnlyList<QueryableCollection> Metadata => _entries.Values.Select(e => e.Metadata).ToArray();

    internal QueryableTableNode Add(string id, string title, IReadOnlyList<QueryableColumn> columns,
        IEnumerable<RiftRow> source, string description)
    {
        var rows = source.ToArray();
        Array.Sort(rows, (left, right) => StringComparer.Ordinal.Compare(left.Key, right.Key));
        for (var index = 1; index < rows.Length; index++)
            if (rows[index - 1].Key == rows[index].Key)
                throw new InvalidOperationException($"Duplicate row identity in {id}.");
        _entries[id] = new(new(id, title, columns, rows.Length) { Description = description }, rows);
        return new(id, title, columns, rows.Length) { Description = description };
    }

    internal QueryableCollectionPage Query(string id, QueryableCollectionQuery query)
    {
        if (!_entries.TryGetValue(id, out var entry)) throw new KeyNotFoundException($"Unknown collection '{id}'.");
        if (query.Cursor is not null) throw new ArgumentException("This collection uses offset paging.", nameof(query));
        if (query.Text?.Length > QueryableCollectionLimits.MaxQueryLength ||
            query.Filters.Count > QueryableCollectionLimits.MaxFilterCount ||
            query.Filters.Any(f => f.Value.Length > QueryableCollectionLimits.MaxFilterLength))
            throw new ArgumentException("Collection query exceeds the supported limits.", nameof(query));
        if (query.Filters.Keys.Any(k => !entry.ColumnKeys.Contains(k)) ||
            query.SortColumn is { } sort && !entry.ColumnKeys.Contains(sort))
            throw new ArgumentException("Unknown filter or sort column.", nameof(query));
        var text = query.Text?.Trim();
        var filters = query.Filters.ToArray();
        var ordered = entry.OrderedRows(query.SortColumn, query.Descending);
        var offset = Math.Max(0, query.Offset);
        var limit = Math.Clamp(query.Limit, 1, QueryableCollectionLimits.MaxPageSize);
        var page = new List<TableRow>(limit);
        var matchingCount = 0;
        if (string.IsNullOrEmpty(text) && filters.Length == 0)
        {
            matchingCount = ordered.Length;
            for (var index = offset; index < ordered.Length && page.Count < limit; index++)
                page.Add(BuildRow(ordered[index]));
        }
        else
        {
            foreach (var row in ordered)
            {
                if (!Matches(row, text, filters)) continue;
                if (matchingCount >= offset && page.Count < limit) page.Add(BuildRow(row));
                matchingCount++;
            }
        }
        return new(id, entry.Metadata.Columns, page, entry.Rows.Length, matchingCount, offset, limit);
    }

    private static bool Matches(RiftRow row, string? text, KeyValuePair<string, string>[] filters)
    {
        if (!string.IsNullOrEmpty(text))
        {
            var found = false;
            foreach (var cell in row.Cells)
                if (cell.Value.Text.Contains(text, StringComparison.OrdinalIgnoreCase)) { found = true; break; }
            if (!found) return false;
        }
        foreach (var filter in filters)
            if (!row.Cells.TryGetValue(filter.Key, out var cell) ||
                !string.Equals(cell.Text, filter.Value, StringComparison.OrdinalIgnoreCase)) return false;
        return true;
    }

    private static TableRow BuildRow(RiftRow row) => new(row.Key,
        row.Cells.ToDictionary(c => c.Key, c => c.Value.Build(), StringComparer.Ordinal));

    internal static RiftCell Number(long value) => new(value.ToString(CultureInfo.InvariantCulture),
        () => new TextNode(value.ToString(CultureInfo.InvariantCulture)), value);

    internal static RiftCell Field(string text, Func<FieldNode> build, decimal? number = null) => new(text, build, number);
}
