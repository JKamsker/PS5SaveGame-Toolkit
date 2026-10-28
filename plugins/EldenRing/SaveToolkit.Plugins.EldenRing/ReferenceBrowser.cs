using System.Globalization;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.EldenRing;

internal sealed class ReferenceBrowser(IReferenceCatalog? catalog, IReferenceCatalog? community = null)
{
    public const string CatalogId = "eldenring-items";
    public const string CollectionId = "catalog/items";
    private const int MaximumRecords = 250_000;
    private readonly Lazy<IReadOnlyList<ReferenceRecord>> _index = new(() => Index(catalog));

    public QueryableCollection Collection => new(CollectionId, "Item reference catalog", Columns, catalog?.Count ?? 0)
    {
        Description = "Complete pinned upstream name tables, including unused/test/unnamed rows; PS5 identities remain Inferred. "
            + "No quantity limits, build compatibility, DLC completeness or ownership is implied. "
            + (catalog?.Dataset.Attribution.ToShortText() ?? "No compatible pack: raw identities remain visible in inventory."),
    };

    private static IReadOnlyList<QueryableColumn> Columns =>
    [new("id", "Canonical ID"), new("name", "Name"), new("category", "Category"), new("details", "Details / provenance")];

    public ReferenceRecord? Find(string key)
    {
        var primary = catalog?.Find(key);
        return primary is null || primary.Attributes.GetValueOrDefault("nameStatus") == "unnamed"
            ? community?.Find(key) ?? primary : primary;
    }

    public string FieldCatalogId(string key) => Find(key)?.Dataset.Id ?? CatalogId;

    public string CommunityGroups(string key) => community?.Find(key) is { } row
        ? string.Join(" · ", new[] { row.Attributes.GetValueOrDefault("groups"), row.Attributes.GetValueOrDefault("editorGroups") }
            .Where(value => value is not null)) : "Unspecified";

    public string ItemDetails(string key)
    {
        var record = Find(key);
        var details = record is null ? "Unknown name; raw identity retained." : Details(record);
        var supplemental = community?.Find(key);
        return supplemental is null ? details : details + " · Community groups: "
            + CommunityGroups(key) + " · Source content groups: "
            + supplemental.Attributes.GetValueOrDefault("contentGroups", "Unspecified") + " · " + Details(supplemental);
    }

    public QueryableCollectionPage Query(QueryableCollectionQuery query)
    {
        var result = BrowseQuery.Apply(_index.Value, query, Columns, row => row.Key, Text, (_, _) => null);
        return new QueryableCollectionPage(CollectionId, Columns, result.Page.Select(Row).ToArray(),
            catalog?.Count ?? 0, result.Filtered, result.Offset, result.Limit);
    }

    public static string Details(ReferenceRecord record) => string.Join(" · ",
        new[] { record.Description, record.AttributionText, $"Pack {record.Dataset.PackId}; dataset {record.Dataset.Id}",
            record.Dataset.PackId == CommunityReferences.PackId ? record.Dataset.Attribution.Modifications : null }
            .Where(value => !string.IsNullOrWhiteSpace(value)));

    private static string Text(ReferenceRecord row, string column) => column switch
    {
        "id" => row.Key,
        "name" => row.Name,
        "category" => row.Category ?? "unknown",
        "details" => Details(row),
        _ => throw new ArgumentException("Unknown catalog column."),
    };

    private static TableRow Row(ReferenceRecord record) => new($"catalog/{record.Key}",
        Columns.ToDictionary(column => column.Key, column => column.Key == "details"
            ? (ViewNode)new SectionNode("Reference evidence", [new TextNode(Details(record))])
            : new TextNode(Text(record, column.Key))));

    private static IReadOnlyList<ReferenceRecord> Index(IReferenceCatalog? source)
    {
        if (source is null) return [];
        if (source.Count > MaximumRecords)
            throw new InvalidOperationException($"Catalog exceeds {MaximumRecords.ToString(CultureInfo.InvariantCulture)} records.");
        // Index metadata, not view nodes, to filter/sort the complete catalog before taking a page.
        var rows = new List<ReferenceRecord>(source.Count);
        for (var offset = 0; offset < source.Count; offset += ReferenceQuery.MaxLimit)
        {
            var page = source.Search(new ReferenceQuery { Offset = offset, Limit = ReferenceQuery.MaxLimit });
            rows.AddRange(page.Items);
        }
        return rows;
    }
}
