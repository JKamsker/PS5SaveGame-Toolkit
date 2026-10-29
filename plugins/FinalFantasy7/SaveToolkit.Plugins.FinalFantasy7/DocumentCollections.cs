using System.Globalization;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.FinalFantasy7;

/// <summary>Queryable projections share existing scalar bindings; raw slot identities stay stable.</summary>
internal sealed class DocumentCollections
{
    private readonly Dictionary<string, ICollectionProjection> _collections = new(StringComparer.Ordinal);

    public DocumentCollections(GameplayFields gameplay, ReferenceData references, Func<ScalarField, FieldNode> bind,
        ProgressData progress)
    {
        Add(new GameplayCollection(false, gameplay, references, bind));
        Add(new GameplayCollection(true, gameplay, references, bind));

        foreach (var catalogId in new[] { "ff7r-items", "ff7r-materia", "ff7r-weapons" })
        {
            var records = references.Records(catalogId);
            var id = $"catalog-{catalogId[5..]}";
            // Reference records are pinned for this document; defer the immutable projection until queried.
            var entries = new Lazy<CollectionEntry[]>(() => records.Select(record => CatalogEntry(id, record)).ToArray());
            Add(new CollectionQuery(new(id, $"{catalogId[5..]} reference catalog", CatalogColumns(), records.Count)
            {
                Description = "Descriptions summarize source metadata; effect text and legality are unknown unless explicitly supplied. " + EmbeddedCatalogs.Attribution,
            }, () => entries.Value));
        }

        var cards = references.Records("ff7r-items").Where(record => record.Category == "Queen's Blood Cards").ToArray();
        if (cards.Length > 0)
        {
            Add(new CollectionQuery(new("cards", "Queen's Blood collection", [.. CatalogColumns(), new("quantity", "Stored quantity", IntegerSpec.Int64)], cards.Length)
            {
                Description = "Inventory presence only; not deck membership, opponent wins or a verified 145-card display crosswalk. " + EmbeddedCatalogs.Attribution,
            }, () =>
            {
                var quantities = Enumerable.Range(0, GameplayFields.InventoryCount)
                    .GroupBy(slot => gameplay.TypeId(false, slot)).ToDictionary(group => group.Key,
                        group => group.Sum(slot => (long)gameplay.Quantity(slot)));
                return cards.Select(record =>
                {
                    var entry = CatalogEntry("cards", record);
                    var values = entry.Values.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
                    values["quantity"] = quantities.GetValueOrDefault(uint.Parse(record.Key, CultureInfo.InvariantCulture)).ToString(CultureInfo.InvariantCulture);
                    return new CollectionEntry(entry.Key, values);
                });
            }));
        }

        Add(progress.Collection());
    }

    public IReadOnlyList<QueryableCollection> Metadata => [.. _collections.Values.Select(collection => collection.Metadata)];
    public IReadOnlyList<ViewNode> CatalogViews() => [.. _collections.Values
        .Where(collection => collection.Metadata.Id.StartsWith("catalog-", StringComparison.Ordinal)).Select(collection => collection.View())];
    public QueryableTableNode View(string id) => _collections[id].View();
    public bool Contains(string id) => _collections.ContainsKey(id);
    public QueryableCollectionPage Query(string id, QueryableCollectionQuery query) => _collections.TryGetValue(id, out var collection)
        ? collection.Query(query) : throw new KeyNotFoundException($"Unknown FFVII collection '{id}'.");
    public void Add(ICollectionProjection collection) => _collections.Add(collection.Metadata.Id, collection);

    private static QueryableColumn[] CatalogColumns() =>
        [new("id", "Type ID"), new("name", "Name"), new("category", "Category"), new("description", "Description"), new("source", "Source / licence"), new("details", "Details / optional image")];

    private static CollectionEntry CatalogEntry(string collectionId, SaveToolkit.Sdk.References.ReferenceRecord record)
    {
        var key = $"{collectionId}/{record.Key}";
        var values = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["id"] = record.Key, ["name"] = record.Name, ["category"] = record.Category ?? "Unknown",
            ["description"] = record.Description ?? "No description supplied.", ["source"] = record.AttributionText,
        };
        return new(key, values, () =>
        {
            var cells = values.ToDictionary(pair => pair.Key, pair => (ViewNode)new TextNode(pair.Value), StringComparer.Ordinal);
            cells["details"] = Details(record, values["source"]);
            return new(key, cells);
        });
    }

    internal static ViewNode Details(SaveToolkit.Sdk.References.ReferenceRecord? record, string? source = null)
    {
        if (record is null) return new TextNode("Unknown type; raw identity is preserved.");
        source ??= record.AttributionText;
        return new SectionNode(record.Name, [new TextNode(record.Description ?? "No effect description supplied."),
            new TextNode(source), .. (record.AssetId is { } asset ? new ViewNode[] { new ImageNode(asset, record.Name) } : [])])
        { Collapsed = true, Description = source };
    }
}
