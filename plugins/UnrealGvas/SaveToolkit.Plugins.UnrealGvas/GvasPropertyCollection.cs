using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.UnrealGvas;

/// <summary>Read-only projections share raw property identities; only the original tree owns edit bindings.</summary>
internal sealed class GvasPropertyCollection
{
    // Even maximally escaped strings must fit the host's 2 MiB / 200-row response budget.
    internal const int CellLimit = 192;
    private static readonly IReadOnlyList<QueryableColumn> Columns = [
        new("name", "Property"), new("path", "Serialized path"), new("type", "Storage type"),
        new("value", "Saved value / payload"), new("id", "Canonical raw identity"),
        new("evidence", "Evidence"), new("details", "Description"), new("source", "Attribution and external source"),
    ];
    private readonly IReadOnlyList<GvasPropertyEntry> _entries;
    private readonly IReadOnlyList<IReferenceCatalog> _catalogs;
    private readonly Func<GvasPropertyEntry, ViewNode>? _valueCell;
    private readonly Dictionary<string, ReferenceRecord?> _records = new(StringComparer.Ordinal);
    private readonly Dictionary<(string Name, string Type), Caption> _captions = [];

    public GvasPropertyCollection(string id, string title, IReadOnlyList<GvasPropertyEntry> entries,
        IReadOnlyList<IReferenceCatalog> catalogs, string description,
        Func<GvasPropertyEntry, ViewNode>? valueCell = null)
    {
        // Identities do not change during scalar edits. Keep the existing ordinal page order
        // once, while reading values live for every search and explicit sort.
        _entries = entries.OrderBy(entry => entry.Id, StringComparer.Ordinal).ToArray();
        _catalogs = catalogs;
        _valueCell = valueCell;
        Metadata = new(id, title, Columns, entries.Count) { Description = description };
    }

    public QueryableCollection Metadata { get; }
    public QueryableTableNode View => new(Metadata.Id, Metadata.Title, Columns, Metadata.TotalCount)
    { Description = Metadata.Description };

    public QueryableCollectionPage Query(QueryableCollectionQuery query)
    {
        Validate(query);
        // Values are read live: a scalar edit in the canonical tree must appear in every projection.
        IReadOnlyList<GvasPropertyEntry> filtered = string.IsNullOrEmpty(query.Text) && query.Filters.Count == 0
            ? _entries : _entries.Where(entry => Matches(entry, query)).ToArray();
        IEnumerable<GvasPropertyEntry> ordered = query.SortColumn is { } column
            ? query.Descending
                ? filtered.OrderByDescending(entry => Cell(entry, column), StringComparer.Ordinal).ThenBy(entry => entry.Id, StringComparer.Ordinal)
                : filtered.OrderBy(entry => Cell(entry, column), StringComparer.Ordinal).ThenBy(entry => entry.Id, StringComparer.Ordinal)
            : filtered;
        var rows = ordered.Skip(query.Offset).Take(query.Limit).Select(entry => new TableRow(entry.Id,
            Columns.ToDictionary(column => column.Key, column => CellNode(entry, column.Key), StringComparer.Ordinal))).ToArray();
        return new(Metadata.Id, Columns, rows, _entries.Count, filtered.Count, query.Offset, query.Limit);
    }

    private bool Matches(GvasPropertyEntry entry, QueryableCollectionQuery query)
    {
        foreach (var filter in query.Filters)
            if (!Cell(entry, filter.Key).Equals(filter.Value, StringComparison.OrdinalIgnoreCase)) return false;
        if (string.IsNullOrEmpty(query.Text)) return true;
        foreach (var column in Columns)
            if (Cell(entry, column.Key).Contains(query.Text, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    private string Cell(GvasPropertyEntry entry, string key) => key switch
        {
            "name" => GetCaption(entry).Name,
            "path" => entry.Path,
            "type" => entry.Node.Type,
            "value" => entry.Value,
            "id" => entry.Id,
            "evidence" => entry.Evidence,
            "details" => GetCaption(entry).Description,
            "source" => GetCaption(entry).Source,
            _ => throw new ArgumentException($"Unknown property column '{key}'."),
        };

    private ViewNode CellNode(GvasPropertyEntry entry, string key)
    {
        if (key == "value" && _valueCell is not null) return _valueCell(entry);
        var text = Cell(entry, key);
        return new TextNode(key is "id" or "source" ? text : GvasPropertyEntry.Clip(text, CellLimit));
    }

    private Caption GetCaption(GvasPropertyEntry entry)
    {
        var key = (entry.Node.Name, entry.Node.Type);
        if (_captions.TryGetValue(key, out var caption)) return caption;
        var named = Find(entry.Node.Name);
        var record = named ?? Find(entry.Node.Type);
        caption = new(named?.Name ?? entry.Node.Name,
            record is null ? "Raw serialized name. Unknown IDs remain unchanged." : record.Description ?? "No description supplied.",
            record?.AttributionText ?? "Raw storage observation; PS5 Save Game Toolkit contributors, GPL-3.0-or-later.");
        _captions.Add(key, caption);
        return caption;
    }

    private ReferenceRecord? Find(string key)
    {
        if (_records.TryGetValue(key, out var record)) return record;
        foreach (var catalog in _catalogs)
        {
            record = catalog.Find(key);
            if (record is not null) break;
        }
        _records.Add(key, record);
        return record;
    }

    private sealed record Caption(string Name, string Description, string Source);

    private static void Validate(QueryableCollectionQuery query)
    {
        if (query.Offset < 0 || query.Limit is < 1 or > QueryableCollectionLimits.MaxPageSize || query.Cursor is not null)
            throw new ArgumentException("Use a nonnegative offset and a page limit from 1 to 200; cursors are unsupported.");
        if (query.Text?.Length > QueryableCollectionLimits.MaxQueryLength || query.Filters.Count > QueryableCollectionLimits.MaxFilterCount ||
            query.Filters.Any(filter => filter.Value.Length > QueryableCollectionLimits.MaxFilterLength))
            throw new ArgumentException("Property query exceeds the text/filter budget.");
        if (query.SortColumn is { } sort && !Columns.Any(column => column.Key == sort) ||
            query.Filters.Keys.Any(key => !Columns.Any(column => column.Key == key)))
            throw new ArgumentException("Unknown property query column.");
    }
}
