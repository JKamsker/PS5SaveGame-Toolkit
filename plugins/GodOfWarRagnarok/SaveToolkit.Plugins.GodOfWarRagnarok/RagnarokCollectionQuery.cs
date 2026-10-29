using System.Globalization;
using SaveToolkit.Sdk.Collections;

namespace SaveToolkit.Plugins.GodOfWarRagnarok;

/// <summary>Queries model rows without constructing cells or strings for off-page counters.</summary>
internal static class RagnarokCollectionQuery
{
    public static IReadOnlyList<RagnarokCollectionRow> Match(RagnarokDocument document,
        IReadOnlyList<RagnarokCollectionRow> rows, QueryableCollectionQuery query)
    {
        var text = query.Text?.Trim();
        var filters = query.Filters.ToArray();
        // Every collection is built in increasing physical offset order, including filtered subsets.
        // The default page therefore needs neither a full-model copy nor a sort.
        if (string.IsNullOrEmpty(text) && filters.Length == 0 && query.SortColumn is null) return rows;

        var matches = new List<RagnarokCollectionRow>(rows.Count);
        for (var i = 0; i < rows.Count; i++)
            if (Matches(document, rows[i], text, filters)) matches.Add(rows[i]);

        if (query.SortColumn is not { } column) return matches;
        // Project one typed key per row rather than repeating document/dictionary lookups inside
        // every comparison. The secondary identity order stays ascending in both directions.
        IOrderedEnumerable<RagnarokCollectionRow> sorted;
        if (column is "quantity" or "metadata" or "word")
            sorted = query.Descending ? matches.OrderByDescending(row => Number(document, row, column))
                : matches.OrderBy(row => Number(document, row, column));
        else
            sorted = query.Descending ? matches.OrderByDescending(row => row.Cells.GetValueOrDefault(column) ?? "", StringComparer.OrdinalIgnoreCase)
                : matches.OrderBy(row => row.Cells.GetValueOrDefault(column) ?? "", StringComparer.OrdinalIgnoreCase);
        return sorted.ThenBy(row => row.Key, StringComparer.Ordinal).ToArray();
    }

    private static bool Matches(RagnarokDocument document, RagnarokCollectionRow row, string? text,
        KeyValuePair<string, string>[] filters)
    {
        // UInt32's longest decimal representation is ten characters. Use the same invariant text
        // for substring and exact filters, so leading zero filters do not acquire numeric semantics.
        const int MaximumQuantityDigits = 10;
        Span<char> buffer = stackalloc char[MaximumQuantityDigits];
        var digits = 0;
        if (row.NumberColumn is not null)
            document.ReadWord(row.NumberOffset).TryFormat(buffer, out digits, provider: CultureInfo.InvariantCulture);
        var numberText = buffer[..digits];

        foreach (var filter in filters)
        {
            var equal = filter.Key == row.NumberColumn
                ? numberText.Equals(filter.Value, StringComparison.OrdinalIgnoreCase)
                : string.Equals(row.Cells.GetValueOrDefault(filter.Key), filter.Value, StringComparison.OrdinalIgnoreCase);
            if (!equal) return false;
        }
        if (string.IsNullOrEmpty(text)) return true;
        if (row.NumberColumn is not null && numberText.Contains(text, StringComparison.OrdinalIgnoreCase)) return true;
        foreach (var cell in row.Cells)
            if (cell.Key != row.NumberColumn && cell.Value.Contains(text, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    private static ulong Number(RagnarokDocument document, RagnarokCollectionRow row, string column) =>
        column == row.NumberColumn ? document.ReadWord(row.NumberOffset) : row.FixedNumber;
}
