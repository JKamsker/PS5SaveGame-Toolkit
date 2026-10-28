using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.Witcher3;

/// <summary>Bounded browsing of all pinned reference records, including records absent from this save.</summary>
internal sealed class CatalogViews(IReferenceContext references, string build, Func<IReadOnlyList<GwentCard>?> cards)
{
    private const string CollectionPrefix = "catalog/";
    private const string ActionPrefix = "reference/inspect/";
    private readonly Dictionary<string, IReadOnlyList<ReferenceRecord>> _records = new(StringComparer.Ordinal);
    private static IReadOnlyList<string> CatalogIds => ["items", "abilities", "gwent"];
    private static IReadOnlyList<QueryableColumn> Columns => [new("name", "Name"), new("raw", "Native ID / wiki reference key"),
        new("category", "Category"), new("owned", "Saved copies / state"), new("details", "Description and source")];

    public IReadOnlyList<QueryableCollection> Collections => CatalogIds.Select(id =>
        new QueryableCollection(CollectionPrefix + id, Title(id), Columns, Catalog(id)?.Count ?? 0)).ToArray();

    public IReadOnlyList<ViewNode> Build() => [
        new TextNode("Community reference catalogs cover documented base-game and expansion records, including variants and cut content. " +
            "Wiki references with unpublished native IDs use wiki-entry keys; those keys are not save IDs. " +
            "An entry does not prove that it exists or is available in this build. Unknown native IDs stay visible in saved-data views. " +
            "Descriptions do not authorize grants, replacements or gameplay edits."),
        .. CatalogIds.Select(id => (ViewNode)new SectionNode(Title(id), Nodes(id))),
    ];

    public bool HandlesCollection(string id) => id.StartsWith(CollectionPrefix, StringComparison.Ordinal);
    public bool HandlesAction(string id) => id.StartsWith(ActionPrefix, StringComparison.Ordinal);

    public ActionOutcome Inspect(string actionId, IReadOnlyDictionary<string, FieldValue> parameters)
    {
        var id = actionId[ActionPrefix.Length..];
        if (!parameters.TryGetValue("record", out var value) || Catalog(id)?.Find(value.AsString()) is not { } record)
        {
            throw new ArgumentException("Choose a record from the pinned reference catalog.");
        }

        return new ActionOutcome { Message = $"{record.Name} [{record.Key}] · {record.Category}\n{record.Description}\n{record.AttributionText}" };
    }

    public QueryableCollectionPage Query(string collectionId, QueryableCollectionQuery query)
    {
        var id = collectionId[CollectionPrefix.Length..];
        var catalog = Catalog(id);
        if (!CatalogIds.Contains(id, StringComparer.Ordinal) || query.Cursor is not null || query.Offset < 0 ||
            query.Text?.Length > QueryableCollectionLimits.MaxQueryLength || query.Filters.Count > QueryableCollectionLimits.MaxFilterCount ||
            query.Filters.Any(f => f.Value.Length > QueryableCollectionLimits.MaxFilterLength || f.Key is not ("name" or "raw" or "category" or "owned" or "details")) ||
            query.SortColumn is not (null or "name" or "raw" or "category" or "owned" or "details"))
        {
            throw new ArgumentException("Unsupported reference catalog query.");
        }

        var matches = Records(id, catalog).Where(record => Matches(id, record, query));
        Func<ReferenceRecord, string> key = query.SortColumn switch {
            "raw" => r => r.Key, "category" => r => r.Category ?? "", "owned" => r => State(id, r),
            "details" => r => r.Description ?? "", _ => r => r.Name,
        };
        var ordered = (query.Descending ? matches.OrderByDescending(key, StringComparer.OrdinalIgnoreCase) :
            matches.OrderBy(key, StringComparer.OrdinalIgnoreCase)).ThenBy(r => r.Key, StringComparer.Ordinal).ToArray();
        var limit = Math.Clamp(query.Limit, 1, QueryableCollectionLimits.MaxPageSize);
        return new QueryableCollectionPage(collectionId, Columns, ordered.Skip(query.Offset).Take(limit).Select(r => Row(id, r)).ToArray(),
            catalog?.Count ?? 0, ordered.Length, query.Offset, limit);
    }

    private IReferenceCatalog? Catalog(string id) => CatalogIds.Contains(id, StringComparer.Ordinal) ? references.Catalog(id, build) : null;
    private static string Title(string id) => id switch { "items" => "Item catalog", "abilities" => "Ability and mutation catalog", _ => "Gwent catalog" };

    private IReadOnlyList<ViewNode> Nodes(string id)
    {
        if (Catalog(id) is not { } catalog) { return [new TextNode("No compatible reference pack is pinned. Saved native IDs remain available.")]; }
        var first = catalog.Search(new ReferenceQuery { Limit = 1 }).Items.FirstOrDefault()?.Key ?? "";
        return [new TextNode(catalog.Dataset.Attribution.ToShortText()),
            new QueryableTableNode(CollectionPrefix + id, Title(id), Columns, catalog.Count),
            new ActionNode(ActionPrefix + id, "Look up reference") {
                Description = "Inspect the selected reference and attribution without changing the save.",
                Parameters = [new FieldNode("record", "Reference", new ReferenceSpec(id), FieldValue.Of(first))],
            }];
    }

    private IReadOnlyList<ReferenceRecord> Records(string id, IReferenceCatalog? catalog)
    {
        if (catalog is null) { return []; }
        if (_records.TryGetValue(id, out var cached)) { return cached; }
        var records = new List<ReferenceRecord>(catalog.Count);
        for (var offset = 0; offset < catalog.Count; offset += ReferenceQuery.MaxLimit)
        {
            records.AddRange(catalog.Search(new ReferenceQuery { Offset = offset, Limit = ReferenceQuery.MaxLimit }).Items);
        }

        _records.Add(id, records);
        return records;
    }

    private bool Matches(string id, ReferenceRecord record, QueryableCollectionQuery query)
    {
        string[] text = [record.Name, record.Key, record.Category ?? "", record.Description ?? "", State(id, record), .. record.Attributes.Values];
        return (string.IsNullOrWhiteSpace(query.Text) || text.Any(t => t.Contains(query.Text, StringComparison.OrdinalIgnoreCase))) &&
            query.Filters.All(f => string.IsNullOrEmpty(f.Value) || string.Equals(f.Key switch {
                "name" => record.Name, "raw" => record.Key, "category" => record.Category, "owned" => State(id, record), "details" => record.Description, _ => null,
            }, f.Value, StringComparison.OrdinalIgnoreCase));
    }

    private string State(string id, ReferenceRecord record)
    {
        if (id != "gwent") { return "Reference only"; }
        if (cards() is not { } collection) { return "Collection unavailable"; }
        var card = collection.FirstOrDefault(c => c.Index.ToString(System.Globalization.CultureInfo.InvariantCulture) == record.Key);
        return card is null ? "Not recorded; availability unknown" : $"{card.Copies} saved copies";
    }

    private TableRow Row(string id, ReferenceRecord record) => new(record.Key, new Dictionary<string, ViewNode> {
        ["name"] = new SectionNode(record.Name, [new TextNode(record.AttributionText)]) {
            Badge = record.Attributes.ContainsKey("nativeIdStatus") ? "Native ID unknown" : "Inferred mapping" },
        ["raw"] = new TextNode(record.Key) { Monospace = true }, ["category"] = new TextNode(record.Category ?? "Unclassified"),
        ["owned"] = new TextNode(State(id, record)),
        ["details"] = new SectionNode("Reference details", [new TextNode(record.Description ?? "No description."),
            new TextNode(record.AttributionText), .. record.Attributes.Select(a => (ViewNode)new TextNode($"{a.Key}: {a.Value}"))]) { Collapsed = true },
    });
}
