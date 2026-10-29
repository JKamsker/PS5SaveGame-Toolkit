namespace SaveToolkit.Sdk.References;

public static class ReferenceCatalogReader
{
    /// <summary>
    /// Reads the complete query suffix from Offset, preserving category, text and sorting.
    /// Null catalogs yield no records. Budget exhaustion or malformed paging throws InvalidOperationException;
    /// no partial list is returned as a complete index. Prefer Find/Search for direct lookups and UI pages.
    /// </summary>
    public static IReadOnlyList<ReferenceRecord> ReadBounded(IReferenceCatalog? catalog, ReferenceQuery query, int maxRecords)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentOutOfRangeException.ThrowIfNegative(maxRecords);
        ArgumentOutOfRangeException.ThrowIfNegative(query.Offset);
        if (query.Limit is < 1 or > ReferenceQuery.MaxLimit) throw new ArgumentOutOfRangeException(nameof(query));
        if (catalog is null) return [];
        var catalogCount = catalog.Count;
        if (catalogCount < 0) throw new InvalidOperationException("Catalog count cannot be negative.");
        var records = new List<ReferenceRecord>();
        var keys = new HashSet<string>(StringComparer.Ordinal);
        var offset = query.Offset;
        int? total = null;
        do
        {
            var page = catalog.Search(query with { Offset = offset });
            if (catalog.Count != catalogCount || page.Offset != offset || page.Total < 0 || page.Total > catalogCount ||
                total is { } previous && page.Total != previous || page.Items.Count > query.Limit ||
                page.Items.Count > Math.Max(0, page.Total - offset))
                throw new InvalidOperationException("Catalog returned inconsistent paging metadata.");
            total = page.Total;
            if (Math.Max(0, page.Total - query.Offset) > maxRecords)
                throw new InvalidOperationException($"Catalog query exceeds the {maxRecords}-record budget.");
            if (page.Items.Count == 0 && offset < page.Total)
                throw new InvalidOperationException("Catalog returned an empty page before the end of the query.");
            foreach (var record in page.Items)
            {
                if (string.IsNullOrEmpty(record.Key) || !keys.Add(record.Key))
                    throw new InvalidOperationException("Catalog returned empty or duplicate record keys.");
                records.Add(record);
            }
            offset += page.Items.Count; // Advance by actual progress, including short intermediate pages.
        } while (offset < total);
        return records;
    }
}
