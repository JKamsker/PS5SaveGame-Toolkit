using System.Globalization;
using System.Text.Json;
using SaveToolkit.Sdk.References;

namespace SaveToolkit.Plugins.GodOfWarRagnarok;

/// <summary>Loads attributed embedded records once for direct callers without a Core reference context.</summary>
internal static class RagnarokReferenceLoader
{
    public static IReadOnlyDictionary<string, ReferenceRecord> Load(string packId)
    {
        var result = new Dictionary<string, ReferenceRecord>(StringComparer.Ordinal);
        using var manifest = Read(packId, "pack.json");
        foreach (var entry in manifest.RootElement.GetProperty("datasets").EnumerateArray())
        {
            var attribution = entry.GetProperty("attribution");
            var dataset = new ReferenceDatasetInfo(packId, Text(entry, "id"), ReferenceDatasetKind.Records,
                new ReferenceAttribution(Text(attribution, "sourceUrl"), Text(attribution, "author"), Text(attribution, "license"),
                    Text(attribution, "licenseUrl"), DateOnly.ParseExact(Text(attribution, "retrieved"), "yyyy-MM-dd",
                        CultureInfo.InvariantCulture), Text(attribution, "modifications")));
            foreach (var file in entry.GetProperty("files").EnumerateArray())
            {
                using var records = Read(packId, Text(file, "path"));
                foreach (var record in records.RootElement.GetProperty("records").EnumerateArray())
                {
                    var key = Text(record, "id");
                    result.Add(key, new(key, Text(record, "name"))
                    {
                        Category = Text(record, "category"), Description = Text(record, "description"),
                        Source = Text(record, "source"), Dataset = dataset,
                        Attributes = record.TryGetProperty("attributes", out var attributes)
                            ? attributes.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString()!, StringComparer.Ordinal)
                            : new Dictionary<string, string>(),
                    });
                }
            }
        }

        return result;
    }

    private static JsonDocument Read(string packId, string file)
    {
        using var stream = typeof(RagnarokReferenceLoader).Assembly.GetManifestResourceStream($"refpacks/{packId}/{file}")
            ?? typeof(RagnarokReferenceLoader).Assembly.GetManifestResourceStream($"refpacks/{packId}\\{file}")
            ?? throw new InvalidOperationException($"Missing embedded Ragnarök reference data: {packId}/{file}.");
        return JsonDocument.Parse(stream);
    }

    private static string Text(JsonElement element, string property) => element.GetProperty(property).GetString()!;
}
