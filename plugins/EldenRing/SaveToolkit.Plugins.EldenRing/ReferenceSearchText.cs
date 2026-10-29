using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Collections;

namespace SaveToolkit.Plugins.EldenRing;

/// <summary>Searches immutable evidence without rendering every record on each query.</summary>
internal sealed class ReferenceSearchText
{
    private const int MaximumCachedEntries = 128;
    private const int MaximumCachedCharacters = 64 * 1024;
    private int _cachedEntries;
    private int _cachedCharacters;
    // Descriptions/attributes stay on their records. Only the shared dataset/source evidence
    // is cached, at most once per distinct pair in the browser's bounded, pinned catalog.
    // Dataset identity avoids repeatedly hashing its potentially long modification text.
    private readonly Dictionary<ReferenceDatasetInfo, DatasetProvenance> _provenance = new(ReferenceEqualityComparer.Instance);

    public static IEnumerable<ReferenceRecord> Filter(IReadOnlyList<ReferenceRecord> records,
        QueryableCollectionQuery query, Func<ReferenceRecord, string, bool> matches)
    {
        // BrowseQuery will receive Text=null because this step handles text matching.
        // Retain its original text-length validation before removing that part of the query.
        if (query.Text?.Length > QueryableCollectionLimits.MaxQueryLength)
            throw new ArgumentException("Collection query exceeds protocol limits.", nameof(query));
        var search = query.Text?.Trim();
        return string.IsNullOrEmpty(search) ? records : records.Where(record => matches(record, search));
    }

    public bool ContainsDetails(ReferenceRecord record, string search)
    {
        // Any substring crossing a joined-part boundary necessarily includes the middle dot.
        // Keep exact rendered-text behavior for those searches using the original formatter.
        if (search.Contains('·')) return Contains(ReferenceBrowser.Details(record), search);
        if (!string.IsNullOrWhiteSpace(record.Description) && Contains(record.Description, search)) return true;
        if (!_provenance.TryGetValue(record.Dataset, out var provenance))
        {
            if (_provenance.Count >= MaximumCachedEntries)
                return Contains(ReferenceBrowser.Details(record with { Description = null }), search);
            provenance = new();
            _provenance.Add(record.Dataset, provenance);
        }
        return Contains(provenance.Get(record, ref _cachedEntries, ref _cachedCharacters), search);
    }

    public static bool Contains(string text, string search) => text.Contains(search, StringComparison.OrdinalIgnoreCase);

    private sealed class DatasetProvenance
    {
        private string? _withoutSource;
        private readonly Dictionary<string, string> _withSources = new(StringComparer.Ordinal);

        public string Get(ReferenceRecord record, ref int cachedEntries, ref int cachedCharacters)
        {
            if (record.Source is null && _withoutSource is { } shared) return shared;
            if (record.Source is { } knownSource && _withSources.TryGetValue(knownSource, out var cached)) return cached;
            var text = ReferenceBrowser.Details(record with { Description = null });
            if (cachedEntries < MaximumCachedEntries && text.Length <= MaximumCachedCharacters - cachedCharacters)
            {
                if (record.Source is { } source) _withSources.Add(source, text);
                else _withoutSource = text;
                cachedEntries++;
                cachedCharacters += text.Length;
            }
            return text;
        }
    }
}
