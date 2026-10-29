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

        // Snapshot cheap projections once; rich cells are still rendered only for this page.
        var source = schema.Records.Where(record => collectionId == CollectionId || collectionId == Id(record.Group))
            .Select(record => new QueryRecord(record, Values(record))).ToArray();
        var columns = Columns.Select(column => new InMemoryColumn<QueryRecord>(column.Key)
        {
            SearchText = item => item.Values[column.Key],
            FilterText = item => item.Values[column.Key],
            Comparer = Comparer<QueryRecord>.Create((left, right) =>
                StringComparer.OrdinalIgnoreCase.Compare(left.Values[column.Key], right.Values[column.Key]))
        }).ToArray();
        return new InMemoryCollection<QueryRecord>(Metadata with { Id = collectionId, TotalCount = source.Length },
            () => source, item => item.Record.Element.Id, columns, item => row(item.Record), new()
            {
                ClampPaging = false,
                DefaultComparer = columns.Single(column => column.Key == "key").Comparer,
                NormalizeSearchText = text => string.IsNullOrWhiteSpace(text) ? null : text.Trim()
            }).Query(query);
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

    private sealed record QueryRecord(AstroCollectionRecord Record, Dictionary<string, string> Values);
}
