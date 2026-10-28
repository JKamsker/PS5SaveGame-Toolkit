using SaveToolkit.Sdk;
using SaveToolkit.Sdk.References;

namespace SaveToolkit.Core.References;

/// <summary>A validated, fully loaded pack. Immutable and shared by every document that pins it.</summary>
public sealed class LoadedReferencePack
{
    private readonly IReadOnlyDictionary<string, ReferenceCatalog> _catalogs;
    private readonly IReadOnlyDictionary<string, LoadedAsset> _assets;

    internal LoadedReferencePack(
        ReferencePackInfo info,
        IReadOnlyDictionary<string, ReferenceCatalog> catalogs,
        IReadOnlyDictionary<string, LoadedAsset> assets)
    {
        Info = info;
        _catalogs = catalogs;
        _assets = assets;
    }

    public ReferencePackInfo Info { get; }

    public string Id => Info.Id;

    public IReferenceCatalog? Catalog(string catalogId) => _catalogs.GetValueOrDefault(catalogId);

    /// <summary>Asset by pack-relative path.</summary>
    internal LoadedAsset? Asset(string path) => _assets.GetValueOrDefault(path);

    /// <summary>True when the pack applies to documents opened by <paramref name="pluginId"/> for <paramref name="titleId"/>.</summary>
    public bool AppliesTo(string pluginId, string? titleId) =>
        Info.PluginIds.Contains(pluginId, StringComparer.OrdinalIgnoreCase)
        && (Info.TitleIds.Count == 0 || (titleId is not null && Info.TitleIds.Contains(titleId, StringComparer.OrdinalIgnoreCase)));
}

internal sealed record LoadedAsset(AssetContent Content, ReferenceDatasetInfo Dataset);

/// <summary>Records of one dataset with O(1) key lookup and paged search.</summary>
internal sealed class ReferenceCatalog : IReferenceCatalog
{
    private readonly IReadOnlyList<ReferenceRecord> _records;
    private readonly Dictionary<string, ReferenceRecord> _byKey;

    public ReferenceCatalog(ReferencePackInfo pack, ReferenceDatasetInfo dataset, IReadOnlyList<ReferenceRecord> records)
    {
        Pack = pack;
        Dataset = dataset;
        _records = records;
        _byKey = records.ToDictionary(r => r.Key, StringComparer.Ordinal);
        Categories = [.. records.Select(r => r.Category).OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase)];
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
        IEnumerable<ReferenceRecord> records = _records.Where(record => Matches(record, text, query.Category));
        if (query.SortBy is { } sort)
        {
            Func<ReferenceRecord, string?> key = sort switch
            {
                ReferenceSortKey.Key => r => r.Key,
                ReferenceSortKey.Name => r => r.Name,
                ReferenceSortKey.Category => r => r.Category,
                _ => throw new ArgumentException("Unknown reference sort key.", nameof(query)),
            };
            records = (query.Descending
                ? records.OrderByDescending(key, StringComparer.OrdinalIgnoreCase)
                : records.OrderBy(key, StringComparer.OrdinalIgnoreCase))
                .ThenBy(r => r.Key, StringComparer.Ordinal);
        }
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

    private static bool Matches(ReferenceRecord record, string? text, string? category) =>
        (category is null || string.Equals(record.Category, category, StringComparison.OrdinalIgnoreCase))
        && (text is null
            || record.Name.Contains(text, StringComparison.OrdinalIgnoreCase)
            || record.Key.Contains(text, StringComparison.OrdinalIgnoreCase));
}
