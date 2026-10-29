using System.Globalization;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.FinalFantasy7;

/// <summary>Queryable projections share existing scalar bindings; raw slot identities stay stable.</summary>
internal sealed class DocumentCollections
{
    private readonly Dictionary<string, CollectionQuery> _collections = new(StringComparer.Ordinal);

    public DocumentCollections(GameplayFields gameplay, ReferenceData references, Func<ScalarField, FieldNode> bind,
        ProgressData progress)
    {
        foreach (var materia in new[] { false, true })
        {
            var id = materia ? "materia" : "inventory";
            var count = materia ? GameplayFields.MateriaCount : GameplayFields.InventoryCount;
            List<QueryableColumn> columns =
            [
                new("slot", "Slot", IntegerSpec.UInt32), new("name", "Name"), new("id", "Type ID"),
                new("category", "Category"), new("quantity", materia ? "AP" : "Quantity", IntegerSpec.Int32),
                new("description", "Description / source"), new("details", "Details / optional image"),
            ];
            if (materia)
            {
                columns.Add(new("level", "Stored stars", IntegerSpec.UInt8));
            }

            Add(new(new(id, materia ? "Materia" : "Inventory", columns, count)
            {
                EmptyRows = EmptySlots.Presentation,
                Description = "Empty slots are hidden by default; Show reveals every fixed slot without changing saved data. Existing mapped non-equipment quantities are experimental; counts do not update reward/acquisition flags. " + EmbeddedCatalogs.Attribution,
            }, () => Enumerable.Range(0, count).Select(slot =>
            {
                var type = gameplay.TypeId(materia, slot);
                var empty = gameplay.IsEmpty(materia, slot);
                var record = references.Record(materia ? "ff7r-materia" : "ff7r-items", type);
                var values = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["slot"] = slot.ToString(CultureInfo.InvariantCulture),
                    ["name"] = empty ? EmptySlots.Label : record?.Name ?? $"Unknown ID 0x{type:X}",
                    ["id"] = $"0x{type:X8}", ["category"] = record?.Category ?? "Unknown / empty",
                    ["quantity"] = materia ? gameplay.Ap(slot).ToString(CultureInfo.InvariantCulture) : gameplay.Quantity(slot).ToString(CultureInfo.InvariantCulture),
                    ["description"] = empty ? "Unused slot; original fields and bytes are preserved." : references.Describe(materia, type),
                };
                if (materia)
                {
                    values["level"] = gameplay.Level(slot).ToString(CultureInfo.InvariantCulture);
                }

                return new CollectionEntry($"{id}/{slot}", values, () =>
                {
                    var cells = gameplay.Row(materia, slot, bind).Cells.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
                    cells["category"] = new TextNode(values["category"]);
                    cells["description"] = new TextNode(values["description"]);
                    cells["details"] = empty ? new TextNode(values["description"]) : Details(record);
                    return new($"{id}/{slot}", cells);
                }) { IsEmpty = empty };
            })));
        }

        foreach (var catalogId in new[] { "ff7r-items", "ff7r-materia", "ff7r-weapons" })
        {
            var records = references.Records(catalogId);
            var id = $"catalog-{catalogId[5..]}";
            Add(new(new(id, $"{catalogId[5..]} reference catalog", CatalogColumns(), records.Count)
            {
                Description = "Descriptions summarize source metadata; effect text and legality are unknown unless explicitly supplied. " + EmbeddedCatalogs.Attribution,
            }, () => records.Select(record => CatalogEntry(id, record))));
        }

        var cards = references.Records("ff7r-items").Where(record => record.Category == "Queen's Blood Cards").ToArray();
        if (cards.Length > 0)
        {
            Add(new(new("cards", "Queen's Blood collection", [.. CatalogColumns(), new("quantity", "Stored quantity", IntegerSpec.Int64)], cards.Length)
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
    public void Add(CollectionQuery collection) => _collections.Add(collection.Metadata.Id, collection);

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
            cells["details"] = Details(record);
            return new(key, cells);
        });
    }

    private static ViewNode Details(SaveToolkit.Sdk.References.ReferenceRecord? record) => record is null
        ? new TextNode("Unknown type; raw identity is preserved.")
        : new SectionNode(record.Name, [new TextNode(record.Description ?? "No effect description supplied."),
            new TextNode(record.AttributionText), .. (record.AssetId is { } asset ? new ViewNode[] { new ImageNode(asset, record.Name) } : [])])
        { Collapsed = true, Description = record.AttributionText };
}
