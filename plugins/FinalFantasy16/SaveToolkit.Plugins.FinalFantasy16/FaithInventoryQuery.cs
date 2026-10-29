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

        var filters = query.Filters.ToArray();
        IReadOnlyList<FaithItem> matches = string.IsNullOrEmpty(query.Text) && filters.Length == 0
            ? inventory.Items : inventory.Items.Where(item => Matches(item, query.Text, columns, filters)).ToArray();
        var sortColumn = query.SortColumn ?? "slot";
        var offset = Math.Max(0, query.Offset);
        var limit = Math.Clamp(query.Limit, 1, QueryableCollectionLimits.MaxPageSize);
        if (sortColumn == "slot")
        {
            // Parsing retains dense ascending native slots; filtering also preserves that order.
            // Slice it directly in either direction, without copying/sorting all allocated records.
            var rows = new TableRow[Math.Min(limit, Math.Max(0, matches.Count - offset))];
            for (var index = 0; index < rows.Length; index++)
                rows[index] = row(matches[query.Descending ? matches.Count - 1 - offset - index : offset + index]);
            return new(metadata.Id, metadata.Columns, rows, inventory.Items.Count, matches.Count, offset, limit);
        }

        IOrderedEnumerable<FaithItem> ordered;
        if (NumericColumns.Contains(sortColumn, StringComparer.Ordinal))
            ordered = query.Descending ? matches.OrderByDescending(item => Number(item, sortColumn))
                : matches.OrderBy(item => Number(item, sortColumn));
        else
            ordered = query.Descending ? matches.OrderByDescending(item => Value(item, sortColumn), StringComparer.OrdinalIgnoreCase)
                : matches.OrderBy(item => Value(item, sortColumn), StringComparer.OrdinalIgnoreCase);
        return new(metadata.Id, metadata.Columns, ordered.ThenBy(item => item.Slot)
            .Skip(offset).Take(limit).Select(row).ToArray(), inventory.Items.Count, matches.Count, offset, limit);
    }

    private bool Matches(FaithItem item, string? text, HashSet<string> columns, KeyValuePair<string, string>[] filters)
    {
        foreach (var filter in filters)
            if (!string.Equals(Value(item, filter.Key), filter.Value, StringComparison.OrdinalIgnoreCase)) return false;
        if (string.IsNullOrEmpty(text)) return true;
        foreach (var column in columns)
            if (Value(item, column).Contains(text, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    private int Number(FaithItem item, string column) => column switch
    {
        "id" => item.Id, "quantity" => ReadInt(item.Offset + sizeof(int)), _ => item.Slot,
    };

    private string Value(FaithItem item, string column) => column switch
    {
        "slot" or "id" or "quantity" => FaithCatalog.Key(Number(item, column)),
        "name" => catalog.Name(item.Id), "category" => catalog.Category(item.Id),
        FaithStoredStackState.ColumnKey => FaithStoredStackState.Label(Number(item, "quantity")),
        "flags" => $"0x{ReadInt(item.Offset + 2 * sizeof(int)):X8}",
        "details" => catalog.Provenance(item.Id), _ => "",
    };

    private int ReadInt(int offset) => BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(offset));
}
