using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.TalesOfArise;

/// <summary>Read-only catalog enrichment. Catalog records never authorize save construction or replacement.</summary>
internal sealed class AriseReferences(IReferenceContext context)
{
    public const string ItemsCatalogId = "items";
    public const string EquipmentCatalogId = "equipment";
    private const string InspectPrefix = "references/inspect/";
    private readonly IReferenceContext _context = context;
    private readonly IReferenceCatalog? _items = context.Catalog(ItemsCatalogId);
    private readonly IReferenceCatalog? _equipment = context.Catalog(EquipmentCatalogId);

    public IReferenceCatalog? Items => _items;
    public IReferenceCatalog? Equipment => _equipment;

    public IReadOnlyList<ViewNode> BuildView() =>
    [
        new TextNode("Catalog names and categories are attributed factual references. They do not prove ownership, serialized equipment IDs, compatibility, availability or safe replacement. Unknown save IDs remain visible.") { Tone = Tone.Info },
        CatalogSection(ItemsCatalogId, _items),
        CatalogSection(EquipmentCatalogId, _equipment),
    ];

    public bool Handles(string actionId) => actionId.StartsWith(InspectPrefix, StringComparison.Ordinal);

    public ActionOutcome Inspect(string actionId, IReadOnlyDictionary<string, FieldValue> parameters)
    {
        var catalogId = actionId[InspectPrefix.Length..];
        var catalog = catalogId switch
        {
            ItemsCatalogId => _items,
            EquipmentCatalogId => _equipment,
            _ => null,
        } ?? throw new KeyNotFoundException($"Reference catalog '{catalogId}' is unavailable.");
        if (!parameters.TryGetValue("record", out var value) || value.Kind != FieldValueKind.Text ||
            catalog.Find(value.AsString()) is not { } record)
        {
            throw new ArgumentException("Choose an existing catalog record.", nameof(parameters));
        }

        return new ActionOutcome
        {
            Message = $"{record.Name} · ID {record.Key} · {record.Category ?? "Uncategorised"}\n" +
                $"{record.AttributionText}\nInspection only; the save was not changed.",
        };
    }

    public IReadOnlyList<QueryableCollection> Collections =>
    [
        Collection(ItemsCatalogId, "Items", _items),
        Collection(EquipmentCatalogId, "Equipment", _equipment),
    ];

    public QueryableCollectionPage Query(string collectionId, QueryableCollectionQuery query)
    {
        var catalog = collectionId switch
        {
            ItemsCatalogId => _items,
            EquipmentCatalogId => _equipment,
            _ => null,
        } ?? throw new KeyNotFoundException($"Unknown Tales of Arise catalog '{collectionId}'.");
        var category = query.Filters.GetValueOrDefault("category");
        var character = query.Filters.GetValueOrDefault("character");
        var limit = Math.Clamp(query.Limit, 1, ReferenceQuery.MaxLimit);
        var all = catalog.Search(new ReferenceQuery
        {
            Text = query.Text,
            Category = category,
            Limit = ReferenceQuery.MaxLimit,
        });
        var filtered = all.Items.Where(record => character is null ||
            string.Equals(record.Attributes.GetValueOrDefault("character"), character, StringComparison.OrdinalIgnoreCase)).ToArray();
        var records = filtered.Skip(Math.Max(0, query.Offset)).Take(limit).ToArray();
        var rows = records.Select(record => new TableRow(record.Key, new Dictionary<string, ViewNode>
        {
            ["id"] = ReadOnlyId(collectionId, record),
            ["name"] = new TextNode($"{record.Name} · {record.AttributionText}"),
            ["category"] = new TextNode(record.Category ?? "Unknown"),
            ["character"] = new TextNode(record.Attributes.GetValueOrDefault("character") ?? "All"),
        })).ToArray();
        return new QueryableCollectionPage(collectionId, Columns(collectionId), rows, catalog.Count, filtered.Length,
            Math.Max(0, query.Offset), rows.Length);
    }

    public FieldNode ReferenceField(string valueId, string label, string rawId, string catalogId, string? description = null) =>
        new(valueId, label, new ReferenceSpec(catalogId) { ReadOnly = true, AllowCustom = true }, FieldValue.Of(rawId))
        {
            Description = description ?? "Catalog lookup is informational; replacement is not enabled until serialized identity and compatibility are proven.",
            Confidence = Confidence.Inferred,
            Warning = "Read-only reference lookup. The numeric or symbolic ID is preserved exactly.",
        };

    private ViewNode CatalogSection(string id, IReferenceCatalog? catalog) => catalog is null
        ? new SectionNode(id, [new TextNode("No matching reference pack is pinned. Raw save identities remain available.") { Tone = Tone.Muted }])
        : new SectionNode(id,
        [
            new TextNode($"{catalog.Count} records · {catalog.Pack.Id} {catalog.Pack.Version} · {catalog.Dataset.Attribution.ToShortText()}"),
            new QueryableTableNode(id, catalog.Dataset.Title ?? id, Columns(id), catalog.Count)
            {
                Description = "Search by name or ID. Filter category exactly; equipment additionally supports the character filter.",
            },
            new ActionNode(InspectPrefix + id, "Inspect a catalog record")
            {
                Description = catalog.Dataset.Attribution.ToShortText(),
                Parameters = [new FieldNode("record", "Reference record", new ReferenceSpec(id), FieldValue.Of(catalog.Search(new ReferenceQuery { Limit = 1 }).Items.FirstOrDefault()?.Key ?? ""))],
            },
        ]);

    private static QueryableCollection Collection(string id, string title, IReferenceCatalog? catalog) =>
        new(id, title, Columns(id), catalog?.Count ?? 0)
        {
            Description = catalog is null ? "No reference pack is pinned." : catalog.Dataset.Attribution.ToShortText(),
        };

    private static IReadOnlyList<QueryableColumn> Columns(string id) =>
    [
        new("id", "ID", new ReferenceSpec(id) { AllowCustom = true }) { Width = ColumnWidth.Wide },
        new("name", "Name", new TextSpec()),
        new("category", "Category", new TextSpec()),
        new("character", "Character", new TextSpec()),
    ];

    private static FieldNode ReadOnlyId(string catalogId, ReferenceRecord record) =>
        new($"catalog/{catalogId}/{record.Key}/id", "ID", new ReferenceSpec(catalogId) { ReadOnly = true, AllowCustom = true }, FieldValue.Of(record.Key))
        {
            Description = record.AttributionText,
            Confidence = Confidence.Inferred,
        };
}
