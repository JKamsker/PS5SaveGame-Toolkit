using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.AstroPlayroom;

internal sealed class AstroCollectionQuery
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
    private static readonly IReadOnlySet<string> ColumnKeys = Columns.Select(column => column.Key).ToHashSet(StringComparer.Ordinal);
    private static readonly IReadOnlySet<string> CollectionIds = Groups.Select(Id).Append(CollectionId).ToHashSet(StringComparer.Ordinal);
    private static readonly InMemoryColumn<QueryRecord>[] Adapters = Columns.Select(column =>
        new InMemoryColumn<QueryRecord>(column.Key)
        {
            SearchText = item => item.Value(column.Key),
            FilterText = item => item.Value(column.Key),
            Comparer = Comparer<QueryRecord>.Create((left, right) =>
                StringComparer.OrdinalIgnoreCase.Compare(left.Value(column.Key), right.Value(column.Key)))
        }).ToArray();
    private static readonly InMemoryCollectionOptions<QueryRecord> Options = new()
    {
        ClampPaging = false,
        DefaultComparer = Adapters.Single(column => column.Key == "key").Comparer,
        NormalizeSearchText = text => string.IsNullOrWhiteSpace(text) ? null : text.Trim()
    };
    private readonly IReadOnlyList<QueryableCollection> _metadata;
    private readonly Lazy<IReadOnlyDictionary<string, InMemoryCollection<QueryRecord>>> _queries;

    public AstroCollectionQuery(AstroSchema schema, AstroLocations locations,
        Func<AstroField, FieldValue> current, Func<AstroCollectionRecord, TableRow> row)
    {
        _metadata = [
            new(CollectionId, "All saved collectibles", Columns, schema.Records.Count)
            {
                Description = "Search, sort and filter the entire save, then preview selected or all matching ownership changes. World aliases are Inferred; physical locations remain unknown."
            },
            .. Groups.Select(group => new QueryableCollection(Id(group), group, Columns,
                schema.Records.Count(record => record.Group == group)))
        ];
        // No catalogue query data is needed to open the first view. Resolve immutable projections
        // once on demand; status and rich row cells continue to read the document's live values.
        _queries = new(() =>
        {
            var records = schema.Records.Select(record => Project(record, locations, current)).ToArray();
            return _metadata.ToDictionary(metadata => metadata.Id, metadata =>
            {
                var source = metadata.Id == CollectionId ? records :
                    records.Where(item => Id(item.Record.Group) == metadata.Id).ToArray();
                return new InMemoryCollection<QueryRecord>(metadata, () => source,
                    item => item.Record.Element.Id, Adapters, item => row(item.Record), Options);
            }, StringComparer.Ordinal);
        });
    }

    public QueryableCollection Metadata => _metadata[0];
    public IReadOnlyList<QueryableCollection> Collections => _metadata;

    public QueryableTableNode View() => new(CollectionId, Metadata.Title, Columns, Metadata.TotalCount)
    { Description = Metadata.Description };

    public QueryableTableNode GroupView(string group)
    {
        var metadata = _metadata.Single(item => item.Id == Id(group));
        return new(metadata.Id, group, Columns, metadata.TotalCount);
    }

    public static bool KnownCollection(string id) => CollectionIds.Contains(id);

    private static string Id(string group) => CollectionId + "." + group;

    public QueryableCollectionPage Query(string collectionId, QueryableCollectionQuery query)
    {
        if (!KnownCollection(collectionId)) throw new KeyNotFoundException("Unknown ASTRO collection.");
        if (query.Cursor is not null || query.Offset < 0 || query.Limit is < 1 or > QueryableCollectionLimits.MaxPageSize ||
            query.Text?.Length > QueryableCollectionLimits.MaxQueryLength || query.Filters.Count > QueryableCollectionLimits.MaxFilterCount)
            throw new ArgumentException("Unsupported ASTRO query bounds or cursor.");
        if (query.SortColumn is { } sort && !ColumnKeys.Contains(sort) ||
            query.Filters.Any(filter => !ColumnKeys.Contains(filter.Key) || filter.Value.Length > QueryableCollectionLimits.MaxFilterLength))
            throw new ArgumentException("Unknown ASTRO query column or oversized filter.");

        return _queries.Value[collectionId].Query(query);
    }

    private static QueryRecord Project(AstroCollectionRecord record, AstroLocations locations, Func<AstroField, FieldValue> current)
    {
        var reference = locations.Reference(record);
        return new(record, current, reference?.Name ?? record.Key, reference?.Category ?? "Unmapped",
            locations.World(record)?.Name ?? "Unmapped",
            reference?.AssetId is null ? "No identified image" : "Candidate illustration",
            reference is null ? "No matching schema-qualified reference; exact raw key retained."
                : $"{reference.Description} {reference.AttributionText}");
    }

    private sealed record QueryRecord(AstroCollectionRecord Record, Func<AstroField, FieldValue> Current,
        string Name, string Category, string World, string Image, string Details)
    {
        public string Value(string column) => column switch
        {
            "key" => Record.Key,
            "name" => Name,
            "status" => Record.Ownership is not { } flag ? "Unknown" : Current(flag).AsBool() ? "Owned" : "Missing",
            "category" => Category,
            "world" => World,
            "image" => Image,
            "details" => Details,
            _ => throw new ArgumentException("Unknown ASTRO projection column.", nameof(column))
        };
    }
}
