using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.AstroPlayroom;

internal sealed class AstroCollectionQuery(AstroSchema schema, AstroLocations locations,
    Func<AstroField, FieldValue> current, Func<AstroCollectionRecord, TableRow> row)
{
    public const string CollectionId = "astro.collectibles";
    public const int EagerLimit = 100;
    private static readonly string[] Groups = ["Artifact", "Puzzle", "Prop"];
    private static readonly IReadOnlyList<QueryableColumn> Columns = [
        new("name", "Name / saved alias", new TextSpec()) { Width = ColumnWidth.Wide },
        new("key", "Exact saved key", new TextSpec()),
        new("image", "Thumbnail") { Width = ColumnWidth.Narrow },
        new("status", "Saved ownership", new TextSpec()),
        new("category", "Category / candidate area", new TextSpec()),
        new("world", "Candidate world", new TextSpec()),
        new("details", "Evidence and provenance")
    ];

    public QueryableCollection Metadata => new(CollectionId, "All saved collectibles", Columns, schema.Records.Count)
    { Description = "Search, sort and filter the entire save, then preview selected or all matching ownership changes. World aliases are Inferred; physical locations remain unknown." };

    public QueryableTableNode View() => new(CollectionId, Metadata.Title, Columns, Metadata.TotalCount)
    { Description = Metadata.Description };

    public IReadOnlyList<QueryableCollection> Collections => [Metadata, .. Groups.Select(group =>
        new QueryableCollection(Id(group), group, Columns, schema.Records.Count(record => record.Group == group)))];

    public QueryableTableNode GroupView(string group) => new(Id(group), group, Columns,
        schema.Records.Count(record => record.Group == group));

    public static bool KnownCollection(string id) => id == CollectionId || Groups.Any(group => id == Id(group));

    private static string Id(string group) => CollectionId + "." + group;

    public QueryableCollectionPage Query(string collectionId, QueryableCollectionQuery query)
    {
        if (!KnownCollection(collectionId)) throw new KeyNotFoundException("Unknown ASTRO collection.");
        if (query.Cursor is not null || query.Offset < 0 || query.Limit is < 1 or > QueryableCollectionLimits.MaxPageSize ||
            query.Text?.Length > QueryableCollectionLimits.MaxQueryLength || query.Filters.Count > QueryableCollectionLimits.MaxFilterCount)
            throw new ArgumentException("Unsupported ASTRO query bounds or cursor.");
        var keys = Columns.Select(column => column.Key).ToHashSet(StringComparer.Ordinal);
        if (query.SortColumn is { } sort && !keys.Contains(sort) ||
            query.Filters.Any(filter => !keys.Contains(filter.Key) || filter.Value.Length > QueryableCollectionLimits.MaxFilterLength))
            throw new ArgumentException("Unknown ASTRO query column or oversized filter.");

        // Build cheap searchable values for all records, but rich cells only for this page.
        var source = schema.Records.Where(record => collectionId == CollectionId || collectionId == Id(record.Group)).ToArray();
        var records = source.Select(record => (Record: record, Values: Values(record)))
            .Where(item => string.IsNullOrWhiteSpace(query.Text) || item.Values.Values.Any(value =>
                value.Contains(query.Text.Trim(), StringComparison.OrdinalIgnoreCase)))
            .Where(item => query.Filters.All(filter => string.Equals(item.Values[filter.Key], filter.Value, StringComparison.OrdinalIgnoreCase)))
            .ToArray();
        var ordered = query.Descending
            ? records.OrderByDescending(item => item.Values[query.SortColumn ?? "key"], StringComparer.OrdinalIgnoreCase)
            : records.OrderBy(item => item.Values[query.SortColumn ?? "key"], StringComparer.OrdinalIgnoreCase);
        var rows = ordered.ThenBy(item => item.Record.Element.Id, StringComparer.Ordinal)
            .Skip(query.Offset).Take(query.Limit).Select(item => row(item.Record)).ToArray();
        return new QueryableCollectionPage(collectionId, Columns, rows, source.Length, records.Length, query.Offset, query.Limit);
    }

    private Dictionary<string, string> Values(AstroCollectionRecord record)
    {
        var reference = locations.Reference(record);
        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["key"] = AstroLocations.Key(record),
            ["name"] = reference?.Name ?? AstroLocations.Key(record),
            ["status"] = record.Ownership is not { } flag ? "Unknown" : current(flag).AsBool() ? "Owned" : "Missing",
            ["category"] = reference?.Category ?? "Unmapped",
            ["world"] = locations.World(record)?.Name ?? "Unmapped",
            ["image"] = reference?.AssetId is null ? "No identified image" : "Candidate illustration",
            ["details"] = reference is null ? "No matching schema-qualified reference; exact raw key retained."
                : $"{reference.Description} {reference.AttributionText}"
        };
    }
}
