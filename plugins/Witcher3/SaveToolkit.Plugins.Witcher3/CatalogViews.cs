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
    private static IReadOnlyList<string> CatalogIds => ["items", "abilities", "gwent"];
    private static IReadOnlyList<QueryableColumn> Columns => [new("image", "Icon"), new("name", "Name"), new("raw", "Native ID / wiki reference key"),
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
            query.Filters.Any(f => f.Value.Length > QueryableCollectionLimits.MaxFilterLength || f.Key is not ("image" or "name" or "raw" or "category" or "owned" or "details")) ||
            query.SortColumn is not (null or "image" or "name" or "raw" or "category" or "owned" or "details"))
        {
            throw new ArgumentException("Unsupported reference catalog query.");
        }

        var limit = Math.Clamp(query.Limit, 1, QueryableCollectionLimits.MaxPageSize);
        var category = query.Filters.GetValueOrDefault("category");
        var sort = query.SortColumn switch {
            "raw" => ReferenceSortKey.Key, "category" => ReferenceSortKey.Category, _ => ReferenceSortKey.Name,
        };
        // Custom catalogs may not declare sorting. Their own stable file order remains the fallback.
        var search = new ReferenceQuery {
            Category = string.IsNullOrEmpty(category) ? null : category,
            SortBy = catalog?.SortKeys.Contains(sort) == true ? sort : null,
            Descending = query.Descending, Offset = query.Offset, Limit = limit,
        };
        var state = State(id);
        if (string.IsNullOrWhiteSpace(query.Text) && query.Filters.All(f => f.Key == "category" || string.IsNullOrEmpty(f.Value)) &&
            query.SortColumn is not ("owned" or "details"))
        {
            var page = catalog?.Search(search);
            return new(collectionId, Columns, page?.Items.Select(r => Row(r, state)).ToArray() ?? [],
                catalog?.Count ?? 0, page?.Total ?? 0, query.Offset, limit);
        }

        // Saved state/description are plugin presentation columns, not SDK reference sort keys.
        // Full metadata text search and these legacy columns stream bounded reference pages without a second index.
        var matches = Records(catalog, search).Where(record => Matches(record, query, state));
        if (query.SortColumn == "owned")
        {
            // Preserve the unknown-state bucket: after recorded counts ascending, before them descending.
            matches = (query.Descending
                ? matches.OrderByDescending(r => state(r).Copies is null).ThenByDescending(r => state(r).Copies)
                : matches.OrderBy(r => state(r).Copies is null).ThenBy(r => state(r).Copies))
                .ThenBy(r => r.Key, StringComparer.Ordinal);
        }
        else if (query.SortColumn == "details")
        {
            matches = (query.Descending ? matches.OrderByDescending(r => r.Description ?? "", StringComparer.OrdinalIgnoreCase) :
                matches.OrderBy(r => r.Description ?? "", StringComparer.OrdinalIgnoreCase)).ThenBy(r => r.Key, StringComparer.Ordinal);
        }
        var rows = new List<TableRow>(limit);
        var count = 0;
        foreach (var record in matches)
        {
            if (count >= query.Offset && rows.Count < limit) { rows.Add(Row(record, state)); }
            count++;
        }
        return new(collectionId, Columns, rows, catalog?.Count ?? 0, count, query.Offset, limit);
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
                Mutates = false,
                Description = "Inspect the selected reference and attribution without changing the save.",
                Parameters = [new FieldNode("record", "Reference", new ReferenceSpec(id), FieldValue.Of(first))],
            }];
    }

    private static IEnumerable<ReferenceRecord> Records(IReferenceCatalog? catalog, ReferenceQuery search)
    {
        if (catalog is null) { yield break; }
        for (var offset = 0; offset < catalog.Count; offset += ReferenceQuery.MaxLimit)
        {
            var page = catalog.Search(search with { Offset = offset, Limit = ReferenceQuery.MaxLimit });
            foreach (var record in page.Items) { yield return record; }
            if (offset + page.Items.Count >= page.Total || page.Items.Count == 0) { yield break; }
        }
    }

    private static bool Matches(ReferenceRecord record, QueryableCollectionQuery query, Func<ReferenceRecord, SavedState> state)
    {
        string[] text = [record.Name, record.Key, record.Category ?? "", record.Description ?? "", state(record).Text, .. record.Attributes.Values];
        return (string.IsNullOrWhiteSpace(query.Text) || text.Any(value => value.Contains(query.Text, StringComparison.OrdinalIgnoreCase))) &&
            query.Filters.All(f => string.IsNullOrEmpty(f.Value) || string.Equals(f.Key switch {
                "image" => record.AssetId ?? "", "name" => record.Name, "raw" => record.Key, "category" => record.Category,
                "owned" => state(record).Text, "details" => record.Description, _ => null,
            }, f.Value, StringComparison.OrdinalIgnoreCase));
    }

    private Func<ReferenceRecord, SavedState> State(string id)
    {
        if (id != "gwent")
        {
            var reference = new SavedState(Description: "Reference only");
            return _ => reference;
        }
        if (cards() is not { } collection)
        {
            var unavailable = new SavedState(Description: "Collection unavailable");
            return _ => unavailable;
        }
        // Cards() reflects mutable copy edits. Share one snapshot throughout this query, never across queries.
        var saved = new Dictionary<string, SavedState>(collection.Count, StringComparer.Ordinal);
        foreach (var card in collection)
        {
            saved.TryAdd(card.Index.ToString(System.Globalization.CultureInfo.InvariantCulture), new SavedState(card.Copies));
        }
        var unrecorded = new SavedState();
        return record => saved.GetValueOrDefault(record.Key, unrecorded);
    }

    private sealed record SavedState(int? Copies = null, string Description = "Not recorded; availability unknown")
    {
        public string Text => Copies is { } copies ? $"{copies} saved copies" : Description;
    }

    private TableRow Row(ReferenceRecord record, Func<ReferenceRecord, SavedState> state) => new(record.Key, new Dictionary<string, ViewNode> {
        ["image"] = ReferenceImages.Cell(record, references.AssetProvenance),
        ["name"] = new SectionNode(record.Name, [new TextNode(record.AttributionText)]) {
            Badge = record.Attributes.ContainsKey("nativeIdStatus") ? "Native ID unknown" : "Inferred mapping" },
        ["raw"] = new TextNode(record.Key) { Monospace = true }, ["category"] = new TextNode(record.Category ?? "Unclassified"),
        ["owned"] = new TextNode(state(record).Text),
        ["details"] = new SectionNode("Reference details", [new TextNode(record.Description ?? "No description."),
            new TextNode(record.AttributionText), .. ReferenceImages.Sources(record, references.AssetProvenance),
            .. record.Attributes.Select(a => (ViewNode)new TextNode($"{a.Key}: {a.Value}"))]) { Collapsed = true },
    });
}
