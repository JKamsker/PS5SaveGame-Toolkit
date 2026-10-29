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

        var matches = new List<RagnarokCollectionRow>();
        for (var i = 0; i < rows.Count; i++)
            if (Matches(document, rows[i], text, filters)) matches.Add(rows[i]);

        if (query.SortColumn is { } column)
            matches.Sort((left, right) => Compare(document, left, right, column, query.Descending));
        return matches;
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

    private static int Compare(RagnarokDocument document, RagnarokCollectionRow left, RagnarokCollectionRow right,
        string column, bool descending)
    {
        var primary = column is "quantity" or "metadata" or "word"
            ? Number(document, left, column).CompareTo(Number(document, right, column))
            : StringComparer.OrdinalIgnoreCase.Compare(left.Cells.GetValueOrDefault(column) ?? "", right.Cells.GetValueOrDefault(column) ?? "");
        return primary == 0 ? StringComparer.Ordinal.Compare(left.Key, right.Key) : descending ? -primary : primary;
    }

    private static ulong Number(RagnarokDocument document, RagnarokCollectionRow row, string column) =>
        column == row.NumberColumn ? document.ReadWord(row.NumberOffset) : ulong.Parse(row.Cells[column], CultureInfo.InvariantCulture);
}
