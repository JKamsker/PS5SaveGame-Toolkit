using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.TalesOfArise;

/// <summary>F8 view of existing inventory rows; it never creates or removes records.</summary>
internal static class AriseInventoryQuery
{
    public const string CollectionId = "inventory";

    private static readonly IReadOnlyList<QueryableColumn> Columns =
    [
        new("icon", "Icon"),
        new("id", "ID", new ReferenceSpec(AriseReferences.ItemsCatalogId) { ReadOnly = true, AllowCustom = true }),
        new("name", "Item", new TextSpec()),
        new("quantity", "Quantity", new IntegerSpec(0, AriseDocument.ConsumableStackLimit)),
        new("flags", "Raw flags", new TextSpec()),
        new("inspect", "Inspect"),
        new("restore", "Restore imported"),
    ];

    public static QueryableCollection Metadata(int count) => new(CollectionId, "Inventory", Columns, count)
    {
        Description = "All fixed inventory slots. Zero IDs mark empty slots, hidden by default; unknown nonzero IDs and zero-count items remain visible. IDs, flags and slot order are preserved.",
        EmptyRows = new EmptyRowPresentation("name"),
    };

    public static QueryableTableNode View(int count)
    {
        var metadata = Metadata(count);
        return new(CollectionId, metadata.Title, Columns, count)
        {
            Description = metadata.Description,
            EmptyRows = metadata.EmptyRows,
        };
    }

    public static QueryableCollectionPage Query(AriseDocument document, QueryableCollectionQuery query)
    {
        Validate(query);
        var all = document.ReadInventoryRows();
        var includeEmpty = query.IncludeEmpty ?? !Metadata(all.Count).EmptyRows!.HideByDefault;
        var candidates = all.Where(row => Matches(row, query)).ToArray();
        var hiddenEmptyCount = includeEmpty ? 0 : candidates.Count(row => row.IsEmpty);
        var matching = candidates.Where(row => includeEmpty || !row.IsEmpty).ToArray();
        var ordered = query.Descending
            ? matching.OrderByDescending(row => Cell(row, query.SortColumn ?? "id"), StringComparer.OrdinalIgnoreCase)
            : matching.OrderBy(row => Cell(row, query.SortColumn ?? "id"), StringComparer.OrdinalIgnoreCase);
        var offset = Math.Max(0, query.Offset);
        var limit = Math.Clamp(query.Limit, 1, ReferenceQuery.MaxLimit);
        var rows = ordered.ThenBy(row => row.Index).Skip(offset).Take(limit)
            .Select(document.BuildInventoryRow).ToArray();
        return new QueryableCollectionPage(CollectionId, Columns, rows, all.Count, matching.Length, offset, limit)
        {
            HiddenEmptyCount = hiddenEmptyCount,
        };
    }

    private static void Validate(QueryableCollectionQuery query)
    {
        var keys = Columns.Select(column => column.Key).ToHashSet(StringComparer.Ordinal);
        if (query.Cursor is not null || query.Filters.Keys.Any(key => !keys.Contains(key)) ||
            query.SortColumn is { } sort && !keys.Contains(sort))
        {
            throw new ArgumentException("Use offset paging and published inventory columns for filters and sorting.", nameof(query));
        }
    }

    private static bool Matches(AriseInventoryRow row, QueryableCollectionQuery query)
    {
        if (!string.IsNullOrWhiteSpace(query.Text) && !string.Join(' ', row.Id, DisplayName(row), row.Quantity, $"0x{row.Flags:X2}")
                .Contains(query.Text, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return query.Filters.All(filter => string.Equals(Cell(row, filter.Key), filter.Value, StringComparison.OrdinalIgnoreCase));
    }

    private static string Cell(AriseInventoryRow row, string column) => column switch
    {
        "id" => row.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
        "name" => DisplayName(row),
        "quantity" => row.Quantity.ToString(System.Globalization.CultureInfo.InvariantCulture),
        "flags" => $"0x{row.Flags:X2}",
        "inspect" => row.Name is null ? "—" : "Inspect",
        "restore" => "Restore imported consumable quantities",
        "icon" => row.Name ?? "No icon",
        _ => throw new ArgumentException($"Unknown inventory column '{column}'.", nameof(column)),
    };

    private static string DisplayName(AriseInventoryRow row) => row.IsEmpty ? AriseInventoryRow.EmptySlotLabel : row.Name ?? "Unknown";
}
