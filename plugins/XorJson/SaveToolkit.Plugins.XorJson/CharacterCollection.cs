using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.XorJson;

/// <summary>Token-backed projections with lazy cells and one cached sort order per collection revision.</summary>
internal sealed class CharacterCollection
{
    private const string Description = "Experimental values from this save. PC catalog labels are inferred and may differ on PS5. " +
        "Search covers all rows; filters match complete column text. Sorting is lexical, including numbers. " +
        "Raw details include unknown fields. Gameplay constraints and reward dependencies remain unverified.";
    private readonly IReadOnlyList<QueryableColumn> _columns;
    private readonly HashSet<string> _columnKeys;
    private readonly IReadOnlyList<JsonValueToken> _sources;
    private readonly Func<JsonValueToken, CharacterRow> _project;
    private CharacterRow?[]? _rows;
    private string? _sortColumn;
    private bool _descending;
    private int[]? _sorted;

    public CharacterCollection(string id, string title, string tabTitle, IReadOnlyList<QueryableColumn> columns,
        IReadOnlyList<JsonValueToken> sources, Func<JsonValueToken, CharacterRow> project)
    {
        _columns = columns;
        _columnKeys = columns.Select(column => column.Key).ToHashSet(StringComparer.Ordinal);
        // Structural insertion in an earlier nested group can break allocation-ID order.
        _sources = InIdentityOrder(sources) ? sources : sources.OrderBy(node => node.Index).ToArray();
        _project = project;
        TabTitle = tabTitle;
        Metadata = new(id, title, columns, sources.Count) { Description = Description };
    }

    public QueryableCollection Metadata { get; }
    public string TabTitle { get; }
    public QueryableTableNode View() => new(Metadata.Id, Metadata.Title, _columns, _sources.Count) { Description = Description };

    public QueryableCollectionPage Query(QueryableCollectionQuery query, Func<JsonValueToken, ViewNode> view)
    {
        Validate(query);
        var order = query.SortColumn is { } sort ? Sort(sort, query.Descending) : null;
        var offset = Math.Max(0, query.Offset);
        var limit = Math.Clamp(query.Limit, 1, QueryableCollectionLimits.MaxPageSize);
        var page = new List<TableRow>();
        var filtered = 0;
        var filters = query.Filters.ToArray();
        var needsFilter = !string.IsNullOrEmpty(query.Text) || query.Filters.Count != 0;
        // Without filters neither counting nor skipping needs to touch projected cells.
        var start = needsFilter ? 0 : Math.Min(offset, _sources.Count);
        var end = needsFilter ? _sources.Count : (int)Math.Min(_sources.Count, (long)start + limit);
        for (var position = start; position < end; position++)
        {
            var index = order is not null ? order[position] : query.Descending ? _sources.Count - 1 - position : position;
            var row = Row(index);
            if (needsFilter && !Matches(row, query.Text, filters)) continue;
            if (!needsFilter || filtered >= offset && page.Count < limit)
                page.Add(ViewRow(row, view));
            filtered++;
        }
        return new(Metadata.Id, _columns, page, _sources.Count, needsFilter ? filtered : _sources.Count, offset, limit);
    }

    private CharacterRow Row(int index) =>
        (_rows ??= new CharacterRow?[_sources.Count])[index] ??= _project(_sources[index]);

    private TableRow ViewRow(CharacterRow row, Func<JsonValueToken, ViewNode> view) =>
        new(row.Source.ValueId, _columns.ToDictionary(column => column.Key,
            column => Cell(row, column.Key, view), StringComparer.Ordinal));

    private int[] Sort(string column, bool descending)
    {
        if (_sorted is not null && _sortColumn == column && _descending == descending) return _sorted;
        var keys = new string[_sources.Count];
        var order = new int[_sources.Count];
        for (var i = 0; i < _sources.Count; i++)
        {
            order[i] = i;
            keys[i] = column == "details" ? _sources[i].TypedPath : Row(i).Text(column);
        }
        Array.Sort(order, (left, right) =>
        {
            var comparison = descending ? string.CompareOrdinal(keys[right], keys[left]) : string.CompareOrdinal(keys[left], keys[right]);
            return comparison != 0 ? comparison : _sources[left].Index.CompareTo(_sources[right].Index);
        });
        _sortColumn = column;
        _descending = descending;
        return _sorted = order;
    }

    private static bool Matches(CharacterRow row, string? text, KeyValuePair<string, string>[] filters)
    {
        if (!string.IsNullOrEmpty(text))
        {
            var found = false;
            foreach (var cell in row.Cells.Values)
                if (cell.Text.Contains(text, StringComparison.OrdinalIgnoreCase)) { found = true; break; }
            if (!found) return false;
        }
        foreach (var filter in filters)
            if (!string.Equals(row.Text(filter.Key), filter.Value, StringComparison.OrdinalIgnoreCase)) return false;
        return true;
    }

    private static bool InIdentityOrder(IReadOnlyList<JsonValueToken> sources)
    {
        for (var i = 1; i < sources.Count; i++)
            if (sources[i - 1].Index > sources[i].Index) return false;
        return true;
    }

    private static ViewNode Cell(CharacterRow row, string key, Func<JsonValueToken, ViewNode> view)
    {
        if (key == "details") return view(row.Source);
        if (!row.Cells.TryGetValue(key, out var cell)) return new TextNode("—");
        if (key == "icon") return cell.Reference?.AssetId is { } asset
            ? new ImageNode(asset, cell.Reference.Name) : new TextNode("—");
        if (cell.Token is { } token) return view(token);
        if (cell.Reference is not { } record) return new TextNode(cell.Text);
        var details = new List<ViewNode>
        {
            new TextNode($"Inferred PC label; PS5 applicability unverified. {record.AttributionText}"),
        };
        if (!string.IsNullOrWhiteSpace(record.Description)) details.Add(new TextNode(record.Description));
        return new SectionNode(cell.Text, details) { Description = record.AttributionText, Collapsed = true };
    }

    private void Validate(QueryableCollectionQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (query.Cursor is not null) throw new ArgumentException("Character collections use offset paging.", nameof(query));
        if (query.Text?.Length > QueryableCollectionLimits.MaxQueryLength ||
            query.Filters.Count > QueryableCollectionLimits.MaxFilterCount ||
            query.Filters.Any(filter => !_columnKeys.Contains(filter.Key) || filter.Value.Length > QueryableCollectionLimits.MaxFilterLength) ||
            query.SortColumn is { } sort && !_columnKeys.Contains(sort))
            throw new ArgumentException("Unknown column or collection query limit exceeded.", nameof(query));
    }
}

internal sealed record CharacterCell(string Text, JsonValueToken? Token = null, ReferenceRecord? Reference = null);

internal sealed record CharacterRow(JsonValueToken Source, Dictionary<string, CharacterCell> Cells)
{
    public string Text(string key) => key == "details" ? Source.TypedPath : Cells.TryGetValue(key, out var cell) ? cell.Text : "";
}
