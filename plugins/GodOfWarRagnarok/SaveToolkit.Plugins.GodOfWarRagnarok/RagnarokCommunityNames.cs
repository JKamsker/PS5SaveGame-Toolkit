using System.Text.Json;
using SaveToolkit.Sdk.References;

namespace SaveToolkit.Plugins.GodOfWarRagnarok;

/// <summary>Direct callers receive the same attributed identities without requiring a Core pack store.</summary>
internal static class RagnarokCommunityNames
{
    private const string PackId = "gow-ragnarok.community.en";
    private static readonly IReadOnlyDictionary<string, ReferenceRecord> Records = Load();

    public static ReferenceRecord? Find(string key) => Records.GetValueOrDefault(key);

    private static IReadOnlyDictionary<string, ReferenceRecord> Load()
    {
        var result = new Dictionary<string, ReferenceRecord>(StringComparer.Ordinal);
        using var manifest = Read("pack.json");
        foreach (var entry in manifest.RootElement.GetProperty("datasets").EnumerateArray())
        {
            var attribution = entry.GetProperty("attribution");
            var dataset = new ReferenceDatasetInfo(PackId, entry.GetProperty("id").GetString()!, ReferenceDatasetKind.Records,
                new ReferenceAttribution(Text(attribution, "sourceUrl"), Text(attribution, "author"), Text(attribution, "license"),
                    Text(attribution, "licenseUrl"), DateOnly.ParseExact(Text(attribution, "retrieved"), "yyyy-MM-dd",
                        System.Globalization.CultureInfo.InvariantCulture), Text(attribution, "modifications")));
            foreach (var file in entry.GetProperty("files").EnumerateArray())
            {
                using var records = Read(Text(file, "path"));
                foreach (var record in records.RootElement.GetProperty("records").EnumerateArray())
                {
                    var key = Text(record, "id");
                    result.Add(key, new(key, Text(record, "name"))
                    {
                        Category = Text(record, "category"), Description = Text(record, "description"),
                        Source = Text(record, "source"), Dataset = dataset,
                    });
                }
            }
        }

        return result;
    }

    private static JsonDocument Read(string file)
    {
        using var stream = typeof(RagnarokCommunityNames).Assembly.GetManifestResourceStream($"refpacks/{PackId}/{file}")
            ?? typeof(RagnarokCommunityNames).Assembly.GetManifestResourceStream($"refpacks/{PackId}\\{file}")
            ?? throw new InvalidOperationException($"Missing embedded Ragnarök community data: {file}.");
        return JsonDocument.Parse(stream);
    }

    private static string Text(JsonElement element, string property) => element.GetProperty(property).GetString()!;
}
