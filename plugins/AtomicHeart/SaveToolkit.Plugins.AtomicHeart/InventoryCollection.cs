using System.Globalization;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.AtomicHeart;

/// <summary>Queries the live slot model before creating bounded field/detail cells.</summary>
internal sealed class InventoryCollection(InventoryState state, InventoryViews views)
{
    internal const string Id = "atomic-heart.inventory";
    internal const string QuantityStateColumn = "quantityState";
    private sealed record Description(string Asset, string Name, string Category);
    private readonly Dictionary<int, Description> descriptions = [];
    private static readonly EmptyRowPresentation EmptyRows = new("asset");
    private static readonly IReadOnlyList<QueryableColumn> Columns = [
        new("container", "Container", new TextSpec()), new("icon", "Icon"), new("asset", "Item reference", new TextSpec()),
        new("category", "Category", new TextSpec()), new("raw", "Saved object path", new TextSpec()),
        new("instance", "ItemId", IntegerSpec.Int64), new("amount", "Amount", IntegerSpec.Int64),
        new(QuantityStateColumn, "Quantity state", new TextSpec { ReadOnly = true }),
        new("action", "Bulk edit"), new("restore", "Restore imported"), new("details", "Details"), new("span", "Original decoded span"),
    ];

    internal QueryableCollection Info => new(Id, "Inventory slots", Columns, state.Items.Count) { EmptyRows = EmptyRows };
    internal QueryableTableNode View => new(Id, Info.Title, Columns, state.Items.Count) {
        EmptyRows = EmptyRows,
        Description = "Search all slots by current name, category, raw ID, container, ItemId, Amount or quantity state. " +
            $"Filter Quantity state by {InventoryState.ReducedQuantity} to preview restoring matching quantities. " +
            "Opening Amounts are in row details; re-import establishes a new baseline. This comparison does not identify gameplay consumption or editing. " +
            "Empty slots are hidden by default; show them to inspect their original values and bytes.",
    };

    internal QueryableCollectionPage Query(QueryableCollectionQuery query)
    {
        if (query.Cursor is not null) throw new ArgumentException("Use offset paging for inventory slots.", nameof(query));
        IEnumerable<InventoryItem> entries = state.Items;
        if (!string.IsNullOrWhiteSpace(query.Text)) entries = entries.Where(item => MatchesText(item, query.Text));
        foreach (var (column, value) in query.Filters)
        {
            ValidateColumn(column);
            entries = entries.Where(item => column is "amount" or "instance"
                ? MatchesNumber(item, column, value, exact: true)
                : Text(item, column).Equals(value, StringComparison.OrdinalIgnoreCase));
        }
        var matching = entries.ToArray();
        var includeEmpty = query.IncludeEmpty ?? !EmptyRows.HideByDefault;
        var hiddenEmptyCount = includeEmpty ? 0 : matching.Count(state.IsEmpty);
        var filtered = includeEmpty ? matching : matching.Where(item => !state.IsEmpty(item)).ToArray();
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
        return new(Id, Columns, rows, state.Items.Count, filtered.Length, offset, limit) { HiddenEmptyCount = hiddenEmptyCount };
    }

    private long Number(InventoryItem item, string column) => column == "instance" ? item.InstanceId : state.Current(item.Quantity).AsInt64();
    private string Text(InventoryItem item, string column) => column switch {
        "asset" => Describe(item).Name,
        "raw" => state.Asset(item), "container" => item.Container, "category" => Describe(item).Category,
        QuantityStateColumn => state.QuantityState(item),
        _ => throw new ArgumentException($"Unknown inventory column '{column}'.", nameof(column)),
    };

    private Description Describe(InventoryItem item)
    {
        var asset = state.Asset(item);
        var offset = item.Asset.Payload.Offset;
        if (descriptions.TryGetValue(offset, out var description) && description.Asset == asset) return description;
        var id = InventoryState.ShortId(asset);
        var reference = state.Catalog?.Find(id);
        description = new(asset, reference?.Name ?? InventoryLabels.Name(id), InventoryLabels.Category(item, asset, reference?.Category));
        // One last description per native slot, so edits and undo refresh the label without
        // growing a history of labels. Quantities and vacancy are always read from live state.
        descriptions[offset] = description;
        return description;
    }

    private bool MatchesText(InventoryItem item, string text)
    {
        var description = Describe(item);
        return description.Name.Contains(text, StringComparison.OrdinalIgnoreCase) ||
            description.Asset.Contains(text, StringComparison.OrdinalIgnoreCase) ||
            item.Container.Contains(text, StringComparison.OrdinalIgnoreCase) ||
            description.Category.Contains(text, StringComparison.OrdinalIgnoreCase) ||
            state.QuantityState(item).Contains(text, StringComparison.OrdinalIgnoreCase) ||
            MatchesNumber(item, "instance", text, exact: false) || MatchesNumber(item, "amount", text, exact: false);
    }

    private bool MatchesNumber(InventoryItem item, string column, string text, bool exact)
    {
        Span<char> buffer = stackalloc char[32];
        if (!Number(item, column).TryFormat(buffer, out var written, provider: CultureInfo.InvariantCulture)) return false;
        return exact ? buffer[..written].Equals(text.AsSpan(), StringComparison.OrdinalIgnoreCase)
            : buffer[..written].Contains(text.AsSpan(), StringComparison.OrdinalIgnoreCase);
    }
    private static void ValidateColumn(string column)
    {
        if (column is not ("asset" or "raw" or "container" or "category" or "amount" or "instance" or QuantityStateColumn))
            throw new ArgumentException($"Unknown inventory column '{column}'.", nameof(column));
    }
}
