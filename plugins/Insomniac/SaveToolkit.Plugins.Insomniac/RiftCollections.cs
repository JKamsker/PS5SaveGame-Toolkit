using System.Globalization;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.Insomniac;

internal sealed record RiftCell(string Text, Func<ViewNode> Build, decimal? Number = null)
{
    internal static RiftCell TextOnly(string text) => new(text, () => new TextNode(text));
}

internal sealed record RiftRow(string Key, IReadOnlyDictionary<string, RiftCell> Cells);

/// <summary>F8 paging over immutable parsed state. View cells and bindings are
/// materialized only for the requested page, including filtered selection scopes.</summary>
internal sealed class RiftCollections
{
    private sealed record Entry(QueryableCollection Metadata, IReadOnlyList<RiftRow> Rows);
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);

    internal IReadOnlyList<QueryableCollection> Metadata => _entries.Values.Select(e => e.Metadata).ToArray();

    internal QueryableTableNode Add(string id, string title, IReadOnlyList<QueryableColumn> columns,
        IEnumerable<RiftRow> source, string description)
    {
        var rows = source.ToArray();
        if (rows.Select(r => r.Key).Distinct(StringComparer.Ordinal).Count() != rows.Length)
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
        var keys = entry.Metadata.Columns.Select(c => c.Key).ToHashSet(StringComparer.Ordinal);
        if (query.Filters.Keys.Any(k => !keys.Contains(k)) || query.SortColumn is { } sort && !keys.Contains(sort))
            throw new ArgumentException("Unknown filter or sort column.", nameof(query));
        var matching = entry.Rows.Where(row =>
            (string.IsNullOrWhiteSpace(query.Text) || row.Cells.Values.Any(c =>
                c.Text.Contains(query.Text.Trim(), StringComparison.OrdinalIgnoreCase))) &&
            query.Filters.All(f => row.Cells.TryGetValue(f.Key, out var cell) &&
                string.Equals(cell.Text, f.Value, StringComparison.OrdinalIgnoreCase))).ToArray();
        IEnumerable<RiftRow> ordered = matching.OrderBy(row => row.Key, StringComparer.Ordinal);
        if (query.SortColumn is { } column)
        {
            var comparer = Comparer<RiftCell>.Create((left, right) =>
            {
                if (left.Number is { } a && right.Number is { } b) return a.CompareTo(b);
                if (left.Number.HasValue != right.Number.HasValue) return left.Number.HasValue ? -1 : 1;
                return StringComparer.OrdinalIgnoreCase.Compare(left.Text, right.Text);
            });
            ordered = query.Descending
                ? matching.OrderByDescending(row => row.Cells[column], comparer).ThenBy(row => row.Key, StringComparer.Ordinal)
                : matching.OrderBy(row => row.Cells[column], comparer).ThenBy(row => row.Key, StringComparer.Ordinal);
        }
        var offset = Math.Max(0, query.Offset);
        var limit = Math.Clamp(query.Limit, 1, QueryableCollectionLimits.MaxPageSize);
        var page = ordered.Skip(offset).Take(limit).Select(row => new TableRow(row.Key,
            row.Cells.ToDictionary(c => c.Key, c => c.Value.Build(), StringComparer.Ordinal))).ToArray();
        return new(id, entry.Metadata.Columns, page, entry.Rows.Count, matching.Length, offset, limit);
    }

    internal static RiftCell Number(long value) => new(value.ToString(CultureInfo.InvariantCulture),
        () => new TextNode(value.ToString(CultureInfo.InvariantCulture)), value);

    internal static RiftCell Field(string text, Func<FieldNode> build, decimal? number = null) => new(text, build, number);
}
