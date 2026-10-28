using System.Buffers.Binary;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.FinalFantasy16;

// Query the complete model first; construct rich cells/bindings only for the returned page.
internal sealed class FaithInventoryQuery(byte[] bytes, FaithInventory inventory, FaithCatalog catalog)
{
    private static readonly string[] NumericColumns = ["slot", "id", "quantity"];

    public QueryableCollectionPage Query(QueryableCollectionQuery query,
        Func<FaithItem, TableRow> row, QueryableCollection metadata)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (query.Cursor is not null) throw new ArgumentException("Main inventory uses offset paging.", nameof(query));
        if (query.Text?.Length > QueryableCollectionLimits.MaxQueryLength ||
            query.Filters.Count > QueryableCollectionLimits.MaxFilterCount ||
            query.Filters.Any(pair => pair.Key.Length > QueryableCollectionLimits.MaxFilterLength ||
                pair.Value.Length > QueryableCollectionLimits.MaxFilterLength))
            throw new ArgumentException("Inventory query exceeds the supported bounds.", nameof(query));
        var columns = metadata.Columns.Select(column => column.Key).ToHashSet(StringComparer.Ordinal);
        if (query.Filters.Keys.Any(key => !columns.Contains(key)) || query.SortColumn is { } sort && !columns.Contains(sort))
            throw new ArgumentException("Unknown inventory filter or sort column.", nameof(query));

        var matches = inventory.Items.Select(item => new Entry(item, catalog.Name(item.Id), catalog.Category(item.Id),
                BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(item.Offset + sizeof(int))),
                BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(item.Offset + 2 * sizeof(int)))))
            .Where(entry => (string.IsNullOrEmpty(query.Text) ||
                columns.Any(column => Value(entry, column).Contains(query.Text, StringComparison.OrdinalIgnoreCase))) &&
                query.Filters.All(filter => string.Equals(Value(entry, filter.Key), filter.Value, StringComparison.OrdinalIgnoreCase)))
            .ToArray();
        var sortColumn = query.SortColumn ?? "slot";
        IOrderedEnumerable<Entry> ordered;
        if (NumericColumns.Contains(sortColumn, StringComparer.Ordinal))
            ordered = query.Descending ? matches.OrderByDescending(entry => Number(entry, sortColumn))
                : matches.OrderBy(entry => Number(entry, sortColumn));
        else
            ordered = query.Descending ? matches.OrderByDescending(entry => Value(entry, sortColumn), StringComparer.OrdinalIgnoreCase)
                : matches.OrderBy(entry => Value(entry, sortColumn), StringComparer.OrdinalIgnoreCase);
        var offset = Math.Max(0, query.Offset);
        var limit = Math.Clamp(query.Limit, 1, QueryableCollectionLimits.MaxPageSize);
        return new(metadata.Id, metadata.Columns, ordered.ThenBy(entry => entry.Item.Slot)
            .Skip(offset).Take(limit).Select(entry => row(entry.Item)).ToArray(), inventory.Items.Count, matches.Length, offset, limit);
    }

    private static int Number(Entry entry, string column) => column switch
    {
        "id" => entry.Item.Id, "quantity" => entry.Quantity, _ => entry.Item.Slot,
    };

    private string Value(Entry entry, string column) => column switch
    {
        "slot" or "id" or "quantity" => FaithCatalog.Key(Number(entry, column)),
        "name" => entry.Name, "category" => entry.Category, "flags" => $"0x{entry.Flags:X8}",
        "details" => catalog.Provenance(entry.Item.Id), _ => "",
    };

    private sealed record Entry(FaithItem Item, string Name, string Category, int Quantity, int Flags);
}
