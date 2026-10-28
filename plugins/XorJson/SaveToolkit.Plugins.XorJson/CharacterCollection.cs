using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.XorJson;

/// <summary>Immutable character projections; only requested rows become view nodes.</summary>
internal sealed class CharacterCollection(
    string id, string title, string tabTitle, IReadOnlyList<QueryableColumn> columns, IReadOnlyList<CharacterRow> rows)
{
    private const string Description = "Experimental values from this save. PC catalog labels are inferred and may differ on PS5. " +
        "Search covers all rows; filters match complete column text. Sorting is lexical, including numbers. " +
        "Raw details include unknown fields. Gameplay constraints and reward dependencies remain unverified.";

    public QueryableCollection Metadata { get; } = new(id, title, columns, rows.Count) { Description = Description };
    public string TabTitle { get; } = tabTitle;

    public QueryableTableNode View() => new(id, title, columns, rows.Count) { Description = Description };

    public QueryableCollectionPage Query(QueryableCollectionQuery query, Func<JsonValueToken, ViewNode> view)
    {
        Validate(query);
        IEnumerable<CharacterRow> matches = rows;
        if (!string.IsNullOrEmpty(query.Text))
            matches = matches.Where(row => row.Cells.Values.Any(cell => cell.Text.Contains(query.Text, StringComparison.OrdinalIgnoreCase)));
        matches = matches.Where(row => query.Filters.All(filter =>
            string.Equals(row.Text(filter.Key), filter.Value, StringComparison.OrdinalIgnoreCase)));
        var filtered = matches.ToArray();
        IOrderedEnumerable<CharacterRow> ordered;
        if (query.SortColumn is { } sort)
        {
            ordered = query.Descending ? filtered.OrderByDescending(row => row.Text(sort), StringComparer.Ordinal)
                : filtered.OrderBy(row => row.Text(sort), StringComparer.Ordinal);
            ordered = ordered.ThenBy(row => row.Source.Index);
        }
        else ordered = query.Descending ? filtered.OrderByDescending(row => row.Source.Index) : filtered.OrderBy(row => row.Source.Index);
        var offset = Math.Max(0, query.Offset);
        var limit = Math.Clamp(query.Limit, 1, QueryableCollectionLimits.MaxPageSize);
        var page = ordered.Skip(offset).Take(limit).Select(row => new TableRow(row.Source.ValueId,
            columns.ToDictionary(column => column.Key, column => Cell(row, column.Key, view), StringComparer.Ordinal))).ToArray();
        return new(id, columns, page, rows.Count, filtered.Length, offset, limit);
    }

    private static ViewNode Cell(CharacterRow row, string key, Func<JsonValueToken, ViewNode> view)
    {
        if (key == "details") return view(row.Source);
        if (!row.Cells.TryGetValue(key, out var cell)) return new TextNode("—");
        if (cell.Token is { } token) return view(token);
        return cell.Reference is { } record
            ? new SectionNode(cell.Text, [new TextNode($"Inferred PC label; PS5 applicability unverified. {record.AttributionText}")])
                { Description = record.AttributionText, Collapsed = true }
            : new TextNode(cell.Text);
    }

    private void Validate(QueryableCollectionQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (query.Cursor is not null) throw new ArgumentException("Character collections use offset paging.", nameof(query));
        var keys = columns.Select(column => column.Key).ToHashSet(StringComparer.Ordinal);
        if (query.Text?.Length > QueryableCollectionLimits.MaxQueryLength ||
            query.Filters.Count > QueryableCollectionLimits.MaxFilterCount ||
            query.Filters.Any(filter => !keys.Contains(filter.Key) || filter.Value.Length > QueryableCollectionLimits.MaxFilterLength) ||
            query.SortColumn is { } sort && !keys.Contains(sort))
            throw new ArgumentException("Unknown column or collection query limit exceeded.", nameof(query));
    }
}

internal sealed record CharacterCell(string Text, JsonValueToken? Token = null, ReferenceRecord? Reference = null);

internal sealed record CharacterRow(JsonValueToken Source, IReadOnlyDictionary<string, CharacterCell> Cells)
{
    public string Text(string key) => key == "details" ? Source.TypedPath : Cells.TryGetValue(key, out var cell) ? cell.Text : "";
}
