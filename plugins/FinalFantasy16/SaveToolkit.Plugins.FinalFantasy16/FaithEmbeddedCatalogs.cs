using System.Collections.Frozen;
using System.Globalization;
using System.Text.Json;
using SaveToolkit.Sdk.References;

namespace SaveToolkit.Plugins.FinalFantasy16;

// Assembly resources only. Runtime plugins never read files or download reference data.
internal static class FaithEmbeddedCatalogs
{
    private static readonly FrozenDictionary<string, FrozenDictionary<string, ReferenceRecord>> Catalogs = Load();

    public static ReferenceRecord? Find(string catalog, string key) =>
        Catalogs.GetValueOrDefault(catalog)?.GetValueOrDefault(key);

    public static ReferencePage Search(string catalog, ReferenceQuery query)
    {
        if (!Catalogs.TryGetValue(catalog, out var records)) return new([], 0, Math.Max(0, query.Offset));
        var offset = Math.Max(0, query.Offset);
        var limit = Math.Clamp(query.Limit, 1, ReferenceQuery.MaxLimit);
        var text = string.IsNullOrWhiteSpace(query.Text) ? null : query.Text.Trim();
        IEnumerable<ReferenceRecord> matches = records.Values.Where(record =>
            (query.Category is null || string.Equals(record.Category, query.Category, StringComparison.OrdinalIgnoreCase)) &&
            (text is null || record.Name.Contains(text, StringComparison.OrdinalIgnoreCase) ||
             record.Key.Contains(text, StringComparison.OrdinalIgnoreCase)));
        if (query.SortBy is { } sort)
        {
            Func<ReferenceRecord, string?> key = sort switch
            {
                ReferenceSortKey.Key => record => record.Key,
                ReferenceSortKey.Name => record => record.Name,
                ReferenceSortKey.Category => record => record.Category,
                _ => throw new ArgumentException("Unknown reference sort key.", nameof(query)),
            };
            matches = (query.Descending
                ? matches.OrderByDescending(key, StringComparer.OrdinalIgnoreCase)
                : matches.OrderBy(key, StringComparer.OrdinalIgnoreCase)).ThenBy(record => record.Key, StringComparer.Ordinal);
        }
        else
        {
            matches = matches.OrderBy(record => record.Key, StringComparer.Ordinal);
        }

        var all = matches.ToArray();
        return new(all.Skip(offset).Take(limit).ToArray(), all.Length, offset);
    }

    private static FrozenDictionary<string, FrozenDictionary<string, ReferenceRecord>> Load()
    {
        var catalogs = new Dictionary<string, FrozenDictionary<string, ReferenceRecord>>(StringComparer.Ordinal);
        var assembly = typeof(FaithEmbeddedCatalogs).Assembly;
        foreach (var name in assembly.GetManifestResourceNames().Where(name =>
                     name.StartsWith("refpacks/", StringComparison.Ordinal) && name.EndsWith("/pack.json", StringComparison.Ordinal)))
        {
            using var stream = assembly.GetManifestResourceStream(name)!;
            using var manifest = JsonDocument.Parse(stream);
            var root = manifest.RootElement;
            var prefix = name[..^"pack.json".Length];
            foreach (var data in root.GetProperty("datasets").EnumerateArray().Where(data => data.GetProperty("kind").GetString() == "records"))
            {
                var attribution = data.GetProperty("attribution");
                var id = data.GetProperty("id").GetString()!;
                var dataset = new ReferenceDatasetInfo(root.GetProperty("id").GetString()!, id, ReferenceDatasetKind.Records,
                    new ReferenceAttribution(Text(attribution, "sourceUrl"), Text(attribution, "author"), Text(attribution, "license"),
                        Text(attribution, "licenseUrl"), DateOnly.ParseExact(Text(attribution, "retrieved"), "yyyy-MM-dd", CultureInfo.InvariantCulture),
                        Text(attribution, "modifications")));
                var rows = new Dictionary<string, ReferenceRecord>(StringComparer.Ordinal);
                foreach (var file in data.GetProperty("files").EnumerateArray())
                {
                    using var resource = assembly.GetManifestResourceStream(prefix + Text(file, "path"))
                        ?? throw new InvalidOperationException("Embedded FFXVI catalog is missing.");
                    using var json = JsonDocument.Parse(resource);
                    foreach (var row in json.RootElement.GetProperty("records").EnumerateArray())
                    {
                        var key = Text(row, "id");
                        rows.Add(key, new ReferenceRecord(key, Text(row, "name"))
                        {
                            Category = Optional(row, "category"), Source = Optional(row, "source"),
                            Description = Optional(row, "description"), Dataset = dataset,
                            AssetId = Optional(row, "assetId") is { } asset ? dataset.PackId + ":" + asset : null,
                            Attributes = row.TryGetProperty("attributes", out var attributes)
                                ? attributes.EnumerateObject().ToFrozenDictionary(value => value.Name, value => value.Value.GetString()!, StringComparer.Ordinal)
                                : FrozenDictionary<string, string>.Empty,
                        });
                    }
                }
                catalogs.Add(id, rows.ToFrozenDictionary(StringComparer.Ordinal));
            }
        }
        return catalogs.ToFrozenDictionary(StringComparer.Ordinal);
    }

    private static string Text(JsonElement element, string key) => element.GetProperty(key).GetString()!;
    private static string? Optional(JsonElement element, string key) => element.TryGetProperty(key, out var value) ? value.GetString() : null;
}
