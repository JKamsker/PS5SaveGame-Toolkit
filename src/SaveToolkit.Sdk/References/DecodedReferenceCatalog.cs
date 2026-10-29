namespace SaveToolkit.Sdk.References;

internal sealed record DecodedAsset(Lazy<AssetContent> MaterializedContent, ReferenceDatasetInfo Dataset)
{
    public AssetContent Content => MaterializedContent.Value;
}

/// <summary>Records of one dataset with O(1) key lookup and paged search.</summary>
internal sealed class DecodedReferenceCatalog : IReferenceCatalog
{
    private readonly IReadOnlyList<ReferenceRecord> _records;
    private readonly Dictionary<string, ReferenceRecord> _byKey;
    private readonly Dictionary<(ReferenceSortKey Key, bool Descending), Lazy<ReferenceRecord[]>> _sorted = [];

    public DecodedReferenceCatalog(ReferencePackInfo pack, ReferenceDatasetInfo dataset, IReadOnlyList<ReferenceRecord> records)
    {
        Pack = pack;
        Dataset = dataset;
        _records = records;
        _byKey = records.ToDictionary(r => r.Key, StringComparer.Ordinal);
        Categories = [.. records.Select(r => r.Category).OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase)];
        // Packs are shared by all pinned documents. Only build an index if someone browses that
        // order; Lazy publishes it once under concurrent requests. At most six arrays are retained.
        foreach (var sort in SortKeys)
        {
            _sorted[(sort, false)] = new(() => Order(_records, sort, false).ToArray());
            _sorted[(sort, true)] = new(() => Order(_records, sort, true).ToArray());
        }
    }

    public string Id => Dataset.Id;
    public ReferencePackInfo Pack { get; }
    public ReferenceDatasetInfo Dataset { get; }
    public int Count => _records.Count;
    public IReadOnlyList<string> Categories { get; }
    public IReadOnlyList<ReferenceSortKey> SortKeys { get; } = Array.AsReadOnly(new[]
    {
        ReferenceSortKey.Key, ReferenceSortKey.Name, ReferenceSortKey.Category,
    });

    public ReferenceRecord? Find(string key) => _byKey.GetValueOrDefault(key);

    public ReferencePage Search(ReferenceQuery query)
    {
        var offset = Math.Max(0, query.Offset);
        var limit = Math.Clamp(query.Limit, 1, ReferenceQuery.MaxLimit);
        var text = string.IsNullOrWhiteSpace(query.Text) ? null : query.Text.Trim();
        if (text is null && query.Category is null)
        {
            IReadOnlyList<ReferenceRecord> ordered = _records;
            if (query.SortBy is { } sort)
            {
                if (!_sorted.TryGetValue((sort, query.Descending), out var index))
                    throw new ArgumentException("Unknown reference sort key.", nameof(query));
                ordered = index.Value;
            }

            var count = Math.Min(limit, Math.Max(0, ordered.Count - offset));
            var items = new ReferenceRecord[count];
            for (var i = 0; i < count; i++) items[i] = ordered[offset + i];
            return new ReferencePage(items, ordered.Count, offset);
        }

        IEnumerable<ReferenceRecord> records = _records.Where(record => Matches(record, text, query.Category));
        // Narrow searches sort only their matches, without constructing a full-catalog index.
        if (query.SortBy is { } filteredSort) records = Order(records, filteredSort, query.Descending);
        var page = new List<ReferenceRecord>(Math.Min(limit, _records.Count));
        var total = 0;
        foreach (var record in records)
        {
            if (total >= offset && page.Count < limit)
            {
                page.Add(record);
            }

            total++;
        }

        return new ReferencePage(page, total, offset);
    }

    private static IOrderedEnumerable<ReferenceRecord> Order(
        IEnumerable<ReferenceRecord> records, ReferenceSortKey sort, bool descending)
    {
        Func<ReferenceRecord, string?> key = sort switch
        {
            ReferenceSortKey.Key => r => r.Key,
            ReferenceSortKey.Name => r => r.Name,
            ReferenceSortKey.Category => r => r.Category,
            _ => throw new ArgumentException("Unknown reference sort key.", nameof(sort)),
        };
        return (descending
            ? records.OrderByDescending(key, StringComparer.OrdinalIgnoreCase)
            : records.OrderBy(key, StringComparer.OrdinalIgnoreCase))
            .ThenBy(r => r.Key, StringComparer.Ordinal);
    }

    private static bool Matches(ReferenceRecord record, string? text, string? category) =>
        (category is null || string.Equals(record.Category, category, StringComparison.OrdinalIgnoreCase))
        && (text is null
            || record.Name.Contains(text, StringComparison.OrdinalIgnoreCase)
            || record.Key.Contains(text, StringComparison.OrdinalIgnoreCase));
}
