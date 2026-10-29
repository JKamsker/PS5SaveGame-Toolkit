using System.Globalization;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.AtomicHeart;

/// <summary>Queries the live slot model before creating bounded field/detail cells.</summary>
internal sealed class InventoryCollection(InventoryState state, InventoryViews views)
{
    internal const string Id = "atomic-heart.inventory";
    private static readonly IReadOnlyList<QueryableColumn> Columns = [
        new("container", "Container", new TextSpec()), new("icon", "Icon"), new("asset", "Item reference", new TextSpec()),
        new("category", "Category", new TextSpec()), new("raw", "Saved object path", new TextSpec()),
        new("instance", "ItemId", IntegerSpec.Int64), new("amount", "Amount", IntegerSpec.Int64),
        new("action", "Bulk edit"), new("details", "Details"), new("span", "Original decoded span"),
    ];

    internal QueryableCollection Info => new(Id, "Inventory slots", Columns, state.Items.Count);
    internal QueryableTableNode View => new(Id, Info.Title, Columns, state.Items.Count) {
        Description = "Searches all slots by current name, category, raw ID, container, ItemId or Amount. Exact filters and numeric sorts use current values. Empty slots remain visible. Select all matching for an Amount preview.",
    };

    internal QueryableCollectionPage Query(QueryableCollectionQuery query)
    {
        if (query.Cursor is not null) throw new ArgumentException("Use offset paging for inventory slots.", nameof(query));
        IEnumerable<InventoryItem> entries = state.Items;
        if (!string.IsNullOrWhiteSpace(query.Text)) entries = entries.Where(item =>
            Text(item, "asset").Contains(query.Text, StringComparison.OrdinalIgnoreCase) ||
            Text(item, "raw").Contains(query.Text, StringComparison.OrdinalIgnoreCase) ||
            Text(item, "container").Contains(query.Text, StringComparison.OrdinalIgnoreCase) ||
            Text(item, "category").Contains(query.Text, StringComparison.OrdinalIgnoreCase) ||
            Text(item, "instance").Contains(query.Text, StringComparison.OrdinalIgnoreCase) ||
            Text(item, "amount").Contains(query.Text, StringComparison.OrdinalIgnoreCase));
        foreach (var (column, value) in query.Filters)
        {
            ValidateColumn(column);
            entries = entries.Where(item => Text(item, column).Equals(value, StringComparison.OrdinalIgnoreCase));
        }
        var filtered = entries.ToArray();
        var sort = query.SortColumn ?? "container";
        ValidateColumn(sort);
        IOrderedEnumerable<InventoryItem> ordered;
        if (sort is "amount" or "instance") ordered = query.Descending
            ? filtered.OrderByDescending(item => Number(item, sort)) : filtered.OrderBy(item => Number(item, sort));
        else ordered = query.Descending ? filtered.OrderByDescending(item => Text(item, sort), StringComparer.OrdinalIgnoreCase)
            : filtered.OrderBy(item => Text(item, sort), StringComparer.OrdinalIgnoreCase);
        var offset = Math.Max(0, query.Offset);
        var limit = Math.Clamp(query.Limit, 1, QueryableCollectionLimits.MaxPageSize);
        var rows = ordered.ThenBy(item => item.Key, StringComparer.Ordinal).Skip(offset).Take(limit).Select(views.Row).ToArray();
        return new(Id, Columns, rows, state.Items.Count, filtered.Length, offset, limit);
    }

    private long Number(InventoryItem item, string column) => column == "instance" ? item.InstanceId : state.Current(item.Quantity).AsInt64();
    private string Text(InventoryItem item, string column) => column switch {
        "asset" => state.Catalog?.Find(InventoryState.ShortId(state.Asset(item)))?.Name ?? InventoryLabels.Name(InventoryState.ShortId(state.Asset(item))),
        "raw" => state.Asset(item), "container" => item.Container, "category" => InventoryLabels.Category(item, state),
        "amount" or "instance" => Number(item, column).ToString(CultureInfo.InvariantCulture),
        _ => throw new ArgumentException($"Unknown inventory column '{column}'.", nameof(column)),
    };
    private static void ValidateColumn(string column)
    {
        if (column is not ("asset" or "raw" or "container" or "category" or "amount" or "instance"))
            throw new ArgumentException($"Unknown inventory column '{column}'.", nameof(column));
    }
}
