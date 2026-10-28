using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.FinalFantasy7;

/// <summary>Resolves optional toolkit packs first, then the pinned MIT-derived fallback tables.</summary>
internal sealed class ReferenceData(IReferenceContext context, Ff7Schema schema)
{
    public string Item(uint id) => Find("ff7r-items", id) ?? Ff7Catalog.Item(schema, id);

    public string Materia(uint id) => Find("ff7r-materia", id) ?? Ff7Catalog.Materia(schema, id);

    public string Weapon(uint id) => Find("ff7r-weapons", id) ?? Ff7Catalog.Weapon(id);

    public string Character(int index) => Find("ff7r-characters", (uint)index) ?? Ff7Catalog.Character(schema, index);

    public IReadOnlyList<KeyValuePair<uint, string>> Entries(bool materia)
    {
        var catalog = context.Catalog(materia ? "ff7r-materia" : "ff7r-items", schema.Name);
        if (catalog is null)
        {
            return ItemNames.Entries(schema, materia);
        }

        var values = new List<KeyValuePair<uint, string>>();
        for (var offset = 0; offset < catalog.Count; offset += ReferenceQuery.MaxLimit)
        {
            var page = catalog.Search(new ReferenceQuery { Offset = offset, Limit = ReferenceQuery.MaxLimit });
            foreach (var item in page.Items)
            {
                if (uint.TryParse(item.Key, out var id))
                {
                    values.Add(new(id, item.Name));
                }
            }
        }

        return values.Count == 0 ? ItemNames.Entries(schema, materia) : values;
    }

    public IReadOnlyList<ViewNode> CatalogSections() =>
    [
        SectionNode.Lazy("Item catalog", "catalog/items/0", $"{Entries(false).Count} entries"),
        SectionNode.Lazy("Materia catalog", "catalog/materia/0", $"{Entries(true).Count} entries"),
    ];

    public TableNode CatalogPage(bool materia, int page)
    {
        var entries = Entries(materia);
        var start = page * 128;
        if (page < 0 || start >= entries.Count)
        {
            throw new KeyNotFoundException("Unknown FFVII catalog page.");
        }

        var rows = entries.Skip(start).Take(128).Select(pair => new TableRow(
            $"catalog/{(materia ? "materia" : "items")}/{pair.Key:X8}",
            new Dictionary<string, ViewNode>(StringComparer.Ordinal)
            {
                ["id"] = new TextNode($"0x{pair.Key:X8}") { Monospace = true },
                ["name"] = new TextNode(pair.Value),
                ["category"] = new TextNode(materia ? "Materia" : "Item"),
            })).ToArray();
        return new TableNode([new("id", "ID"), new("name", "Name"), new("category", "Category")], rows)
        {
            Title = materia ? "Materia catalog" : "Item catalog",
            Description = "Names are optional reference-pack records, falling back to the embedded MIT-derived table. Catalog entries are read-only and do not establish legal write limits.",
        };
    }

    private string? Find(string catalogId, uint id)
    {
        var catalog = context.Catalog(catalogId, schema.Name);
        return catalog?.Find(id.ToString())?.Name ?? catalog?.Find($"0x{id:X}")?.Name;
    }
}
