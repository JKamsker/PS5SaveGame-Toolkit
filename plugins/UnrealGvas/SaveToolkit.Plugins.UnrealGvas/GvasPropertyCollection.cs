using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.UnrealGvas;

/// <summary>Read-only projections share raw property identities; only the original tree owns edit bindings.</summary>
internal sealed class GvasPropertyCollection
{
    // Even maximally escaped strings must fit the host's 2 MiB / 200-row response budget.
    private const int CellLimit = 192;
    private static readonly IReadOnlyList<QueryableColumn> Columns = [
        new("name", "Property"), new("path", "Serialized path"), new("type", "Storage type"),
        new("value", "Saved value / payload"), new("id", "Canonical raw identity"),
        new("evidence", "Evidence"), new("details", "Description"), new("source", "Attribution and external source"),
    ];
    private readonly IReadOnlyList<GvasPropertyEntry> _entries;
    private readonly IReadOnlyList<IReferenceCatalog> _catalogs;

    public GvasPropertyCollection(string id, string title, IReadOnlyList<GvasPropertyEntry> entries,
        IReadOnlyList<IReferenceCatalog> catalogs, string description)
    {
        _entries = entries;
        _catalogs = catalogs;
        Metadata = new(id, title, Columns, entries.Count) { Description = description };
    }

    public QueryableCollection Metadata { get; }
    public QueryableTableNode View => new(Metadata.Id, Metadata.Title, Columns, Metadata.TotalCount)
    { Description = Metadata.Description };

    public QueryableCollectionPage Query(QueryableCollectionQuery query)
    {
        Validate(query);
        // Values are read live: a scalar edit in the canonical tree must appear in every projection.
        var filtered = _entries.Where(entry => Matches(entry, query)).ToArray();
        var ordered = query.SortColumn is { } column
            ? query.Descending
                ? filtered.OrderByDescending(entry => Cell(entry, column), StringComparer.Ordinal).ThenBy(entry => entry.Id, StringComparer.Ordinal)
                : filtered.OrderBy(entry => Cell(entry, column), StringComparer.Ordinal).ThenBy(entry => entry.Id, StringComparer.Ordinal)
            : filtered.OrderBy(entry => entry.Id, StringComparer.Ordinal);
        var rows = ordered.Skip(query.Offset).Take(query.Limit).Select(entry => new TableRow(entry.Id,
            Columns.ToDictionary(column => column.Key, column => (ViewNode)new TextNode(
                column.Key is "id" or "source" ? Cell(entry, column.Key)
                    : GvasPropertyEntry.Clip(Cell(entry, column.Key), CellLimit)), StringComparer.Ordinal))).ToArray();
        return new(Metadata.Id, Columns, rows, _entries.Count, filtered.Length, query.Offset, query.Limit);
    }

    private bool Matches(GvasPropertyEntry entry, QueryableCollectionQuery query) =>
        (string.IsNullOrEmpty(query.Text) || Columns.Any(column => Cell(entry, column.Key).Contains(query.Text, StringComparison.OrdinalIgnoreCase))) &&
        query.Filters.All(filter => Cell(entry, filter.Key).Equals(filter.Value, StringComparison.OrdinalIgnoreCase));

    private string Cell(GvasPropertyEntry entry, string key)
    {
        var named = _catalogs.Select(catalog => catalog.Find(entry.Node.Name)).FirstOrDefault(record => record is not null);
        var record = named ?? _catalogs.Select(catalog => catalog.Find(entry.Node.Type)).FirstOrDefault(record => record is not null);
        return key switch
        {
            "name" => named?.Name ?? entry.Node.Name,
            "path" => entry.Path,
            "type" => entry.Node.Type,
            "value" => entry.Value,
            "id" => entry.Id,
            "evidence" => entry.Evidence,
            "details" => record is null ? "Raw serialized name. Unknown IDs remain unchanged."
                : record.Description ?? "No description supplied.",
            "source" => record?.AttributionText ?? "Raw storage observation; PS5 Save Game Toolkit contributors, GPL-3.0-or-later.",
            _ => throw new ArgumentException($"Unknown property column '{key}'."),
        };
    }

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
