using System.Globalization;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.Witcher3;

/// <summary>Presentation and queries operate on the complete inventory, never on a previously loaded page.</summary>
internal sealed class InventoryViews(
    IReadOnlyList<InventoryItem> items, IReferenceCatalog? catalog, Func<InventoryItem, FieldNode> quantity,
    Func<string, ReferenceDatasetInfo?>? assetProvenance = null)
{
    public const string CollectionId = "player-inventory";
    public const string LookupActionId = "catalog/inspect";
    public const string CatalogId = "items";
    private const string UnknownCategory = "Unmapped";

    private static IReadOnlyList<QueryableColumn> Columns => [new("name", "Item"), new("raw", "Native ID"),
        new("category", "Category"), new("quantity", "Quantity", IntegerSpec.UInt16), new("slot", "Saved slot"), new("equipped", "Equipped (inferred)"),
        new("durability", "Stored durability", FloatSpec.Single), new("details", "Details")];

    public QueryableCollection Collection => new(CollectionId, "Player inventory", Columns, items.Count) {
        Description = "Player identity matched by IdTag. Search names, native IDs, categories, slots and instance IDs across all records.",
    };

    public IReadOnlyList<ViewNode> Build() => [
        new TextNode(catalog is null ? "No item pack is available. Native IDs remain visible; metadata stays unresolved." :
            $"Item labels are inferred cross-build joins. {catalog.Dataset.Attribution.ToShortText()}"),
        new TextNode($"{items.Count(i => catalog?.Find(i.Name) is null)} of {items.Count} records have no catalog match. " +
            "Saved durability is read-only; -1 is retained as a sentinel. Legal stack caps are unknown."),
        new QueryableTableNode(CollectionId, Collection.Title, Columns, items.Count) { Description = Collection.Description },
        new ActionNode(LookupActionId, "Look up catalog item") {
            Description = "Browse attributed reference data. This lookup does not replace, create or equip an item.",
            Parameters = [new FieldNode("item", "Item", new ReferenceSpec(CatalogId),
                FieldValue.Of(items.FirstOrDefault(item => catalog?.Find(item.Name) is not null)?.Name ?? ""))],
        },
    ];

    public QueryableCollectionPage Query(QueryableCollectionQuery query)
    {
        if (query.Cursor is not null || query.Offset < 0 || query.Text?.Length > QueryableCollectionLimits.MaxQueryLength ||
            query.Filters.Count > QueryableCollectionLimits.MaxFilterCount ||
            query.Filters.Any(f => f.Value.Length > QueryableCollectionLimits.MaxFilterLength || !FilterColumns.Contains(f.Key, StringComparer.Ordinal)) ||
            query.SortColumn is { } column && !SortColumns.Contains(column, StringComparer.Ordinal))
        {
            throw new ArgumentException("Unsupported inventory query, filter, sort or cursor.");
        }

        var filtered = items.Where(item => Matches(item, query));
        var ordered = query.SortColumn switch {
            "quantity" => Order(filtered, i => i.Quantity, query.Descending),
            "durability" => Order(filtered, i => i.Durability, query.Descending),
            "raw" => Order(filtered, i => i.Name, query.Descending, StringComparer.Ordinal),
            "category" => Order(filtered, Category, query.Descending, StringComparer.OrdinalIgnoreCase),
            "slot" => Order(filtered, i => i.Slot, query.Descending, StringComparer.Ordinal),
            "name" => Order(filtered, Label, query.Descending, StringComparer.OrdinalIgnoreCase),
            "equipped" => Order(filtered, i => i.Equipped, query.Descending),
            _ => Order(filtered, i => i.Id, query.Descending),
        };
        var matches = ordered.ToArray();
        var limit = Math.Clamp(query.Limit, 1, QueryableCollectionLimits.MaxPageSize);
        return new QueryableCollectionPage(CollectionId, Columns, matches.Skip(query.Offset).Take(limit).Select(Row).ToArray(),
            items.Count, matches.Length, query.Offset, limit);
    }

    public ActionOutcome Inspect(IReadOnlyDictionary<string, FieldValue> parameters)
    {
        if (!parameters.TryGetValue("item", out var value) || catalog?.Find(value.AsString()) is not { } record)
        {
            throw new ArgumentException("Choose an item from the pinned catalog. No save data was changed.");
        }

        return new ActionOutcome { Message = $"{record.Name} [{record.Key}] · {record.Category}\n{record.Description}\n{record.AttributionText}" };
    }

    private static IReadOnlyList<string> FilterColumns => ["name", "raw", "category", "slot", "equipped"];
    private static IReadOnlyList<string> SortColumns => ["name", "raw", "category", "quantity", "slot", "durability", "equipped", "details"];
    private string Label(InventoryItem item) => catalog?.Find(item.Name)?.Name ?? item.Name;
    private string Category(InventoryItem item) => catalog?.Find(item.Name)?.Category ?? UnknownCategory;

    private bool Matches(InventoryItem item, QueryableCollectionQuery query)
    {
        string[] text = [Label(item), item.Name, Category(item), item.Slot, item.Id.ToString(CultureInfo.InvariantCulture)];
        return (string.IsNullOrWhiteSpace(query.Text) || text.Any(t => t.Contains(query.Text, StringComparison.OrdinalIgnoreCase))) &&
            query.Filters.All(filter => string.Equals(filter.Key switch {
                "name" => Label(item), "raw" => item.Name, "category" => Category(item), "slot" => item.Slot,
                "equipped" => item.Equipped ? "true" : "false", _ => "",
            }, filter.Value, StringComparison.OrdinalIgnoreCase));
    }

    private static IOrderedEnumerable<InventoryItem> Order<T>(IEnumerable<InventoryItem> source,
        Func<InventoryItem, T> key, bool descending, IComparer<T>? comparer = null) =>
        (descending ? source.OrderByDescending(key, comparer) : source.OrderBy(key, comparer)).ThenBy(i => i.Id);

    private TableRow Row(InventoryItem item)
    {
        var record = catalog?.Find(item.Name);
        List<ViewNode> details = [
            new TextNode($"Instance {item.Id}; flags 0x{item.Flags:X8}; equipped flag: {item.Equipped} (inferred)."),
            new TextNode(record?.Description ?? "No reviewed item description; retain the native ID."),
            new TextNode(record?.AttributionText ?? "Source: saved native item name; no reference mapping."),
            new TextNode($"Opaque prefix: {item.OpaquePrefix}; saved prefix names: {string.Join(", ", item.PrefixNames)}"),
            new TextNode($"Tags: {string.Join(", ", item.Tags)}"),
            .. item.Attributes.Select(a => new TextNode($"{a.Name}: raw 0x{a.RawValue:X8}, type byte {a.Type} (unknown semantics)")),
        ];
        if (record?.AssetId is { } asset && assetProvenance?.Invoke(asset) is { } provenance)
        {
            details.Add(new ImageNode(asset, record.Name));
            details.Add(new TextNode($"Image source: {provenance.Attribution.ToShortText()}"));
        }

        return new TableRow(item.Id.ToString(CultureInfo.InvariantCulture), new Dictionary<string, ViewNode> {
            ["name"] = new SectionNode(Label(item), [new TextNode(record?.AttributionText ?? "Source: save-native ID.")]) {
                Badge = record is null ? "Unknown mapping" : "Inferred mapping", Description = record?.Description,
            },
            ["raw"] = new TextNode(item.Name) { Monospace = true },
            ["category"] = new TextNode(Category(item)), ["quantity"] = quantity(item),
            ["equipped"] = new TextNode(item.Equipped ? "true" : "false"),
            ["slot"] = new TextNode(item.Slot.Length == 0 ? "No saved mounted slot" : item.Slot),
            ["durability"] = new TextNode(item.Durability.ToString("R", CultureInfo.InvariantCulture)),
            ["details"] = new SectionNode($"Instance {item.Id}", details) { Collapsed = true },
        });
    }
}
