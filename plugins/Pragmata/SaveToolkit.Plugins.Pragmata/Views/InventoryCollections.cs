using System.Globalization;
using SaveToolkit.Plugins.Pragmata.Editing;
using SaveToolkit.Plugins.Pragmata.Format;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.Pragmata.Views;

/// <summary>Pages large inventories and publishes primary bindings through normal F8 queries.</summary>
internal sealed class InventoryCollections(PragmataDocument document)
{
    internal const string CollectionId = "inventory/items";
    internal static readonly QueryableColumn[] Columns =
    [
        new("name", "Item ID (experimental replacement)", new TextSpec()),
        new("id", "Raw ID (hex / decimal)", new TextSpec()),
        new("category", "Category", new TextSpec()),
        new("quantity", "Quantity", IntegerSpec.Int32),
        new("equipped", "Equipped", new TextSpec()),
        new("details", "Details / sources"), new("action", "Selection action"),
    ];

    public bool Enabled => document.Layout.Items.Count > LazyPages.PageSize;
    public QueryableCollection Metadata => new(CollectionId, "_InventoryItems", Columns, document.Layout.Items.Count)
    {
        Description = "Search names, raw decimal/hex IDs, categories, quantities and equipped state. " +
            "Quantity sorting is numeric; other columns use ordinal text and stable physical row keys. " +
            "Names/categories come from attributed item details; game meanings and legal limits remain unverified.",
    };

    public QueryableTableNode Table() => new(CollectionId, Metadata.Title, Columns, Metadata.TotalCount)
        { Description = Metadata.Description };

    public QueryableCollectionPage Query(QueryableCollectionQuery query)
    {
        if (!Enabled) throw new KeyNotFoundException("This inventory uses an eager table.");
        CollectionQueries.Validate(query, Columns);
        var equipped = document.Read(document.Layout.Inventory!.Find(KnownHashes.EquippedItem)!).AsUInt32();
        var rows = document.Layout.Items.Select(item => Describe(item, equipped));
        if (!string.IsNullOrWhiteSpace(query.Text))
            rows = rows.Where(row => row.Search.Contains(query.Text, StringComparison.OrdinalIgnoreCase));
        foreach (var (column, value) in query.Filters)
            rows = rows.Where(row => string.Equals(row.Cell(column), value, StringComparison.OrdinalIgnoreCase));
        var sort = query.SortColumn ?? "id";
        IOrderedEnumerable<Row> ordered;
        if (sort == "quantity")
            ordered = query.Descending ? rows.OrderByDescending(row => row.Quantity) : rows.OrderBy(row => row.Quantity);
        else
            ordered = query.Descending ? rows.OrderByDescending(row => row.Cell(sort), StringComparer.Ordinal)
                : rows.OrderBy(row => row.Cell(sort), StringComparer.Ordinal);
        var filtered = ordered.ThenBy(row => row.Key, StringComparer.Ordinal).ToArray();
        var page = filtered.Skip(query.Offset).Take(query.Limit).Select(row => InventoryView.Row(document, row.Node)).ToArray();
        return new QueryableCollectionPage(CollectionId, Columns, page, document.Layout.Items.Count,
            filtered.Length, query.Offset, query.Limit);
    }

    private Row Describe(SaveNode item, uint equipped)
    {
        var id = document.Read(item.Find(KnownHashes.ItemId)!).AsUInt32();
        var record = document.References.Item(id);
        return new Row(item, QuantityActions.RowKey(item), $"0x{id:x8} / {id}",
            record?.Name ?? $"Unmapped item 0x{id:x8}", record?.Category ?? "unmapped",
            document.Read(item.Find(KnownHashes.Quantity)!).AsInt32(), id == equipped ? "Equipped" : "");
    }

    private sealed record Row(SaveNode Node, string Key, string Id, string Name, string Category, int Quantity, string Equipped)
    {
        public string Search => $"{Id} {Name} {Category} {Quantity.ToString(CultureInfo.InvariantCulture)} {Equipped}";
        public string Cell(string column) => column switch
        {
            "id" => Id, "name" => Name, "category" => Category, "quantity" => Quantity.ToString(CultureInfo.InvariantCulture),
            "equipped" => Equipped, "details" or "action" => "", _ => throw new ArgumentException("Unknown inventory column."),
        };
    }
}
