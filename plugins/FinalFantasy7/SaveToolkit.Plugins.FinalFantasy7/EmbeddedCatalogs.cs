using System.Collections.Frozen;
using System.Text.Json;
using SaveToolkit.Sdk.References;

namespace SaveToolkit.Plugins.FinalFantasy7;

/// <summary>Offline fallback for direct Open callers; the host loads the same F2 packs.</summary>
internal static class EmbeddedCatalogs
{
    public const string Revision = "c7bdd6d8228304b87dcdf15e57155d092eb06fd2";
    public const string SourceRoot = "https://github.com/RyudoSynbios/game-tools-collection/blob/" + Revision;
    public const string Attribution = "RyudoSynbios / Game Tools Collection, MIT (revision " + Revision + ").";
    private static readonly FrozenDictionary<string, ReferenceRecord[]> Catalogs = Load();

    public static IReadOnlyList<ReferenceRecord> Records(Ff7Schema schema, string catalogId) =>
        Catalogs[$"{(schema.IsRemake ? "remake" : "rebirth")}/{catalogId}"];

    public static ReferenceAttribution Provenance(string game, string file) => new(
        $"{SourceRoot}/src/lib/templates/final-fantasy-vii-{game}/saveEditor/{file}",
        "RyudoSynbios / Game Tools Collection", "MIT", $"{SourceRoot}/LICENSE.md", new(2026, 10, 28),
        "Extracted type IDs, names, categories, AP tables and distinct progress bits; added original metadata summaries.");

    private static FrozenDictionary<string, ReferenceRecord[]> Load()
    {
        var catalogs = new Dictionary<string, ReferenceRecord[]>(StringComparer.Ordinal);
        foreach (var game in new[] { "remake", "rebirth" })
        {
            foreach (var id in new[] { "ff7r-items", "ff7r-materia", "ff7r-weapons" })
            {
                using var stream = typeof(EmbeddedCatalogs).Assembly.GetManifestResourceStream(
                    $"refpacks/ff7r.{game}.en/{id}.json")
                    ?? throw new InvalidOperationException("Embedded FFVII catalog is missing.");
                using var json = JsonDocument.Parse(stream);
                var dataset = new ReferenceDatasetInfo($"ff7r.{game}.en", id, ReferenceDatasetKind.Records,
                    Provenance(game, "utils/resource.ts"));
                catalogs.Add($"{game}/{id}", [.. json.RootElement.GetProperty("records").EnumerateArray().Select(row =>
                    new ReferenceRecord(row.GetProperty("id").GetString()!, row.GetProperty("name").GetString()!)
                    {
                        Category = row.GetProperty("category").GetString(),
                        Description = row.GetProperty("description").GetString(),
                        Source = row.GetProperty("source").GetString(),
                        Attributes = row.GetProperty("attributes").EnumerateObject()
                            .ToFrozenDictionary(value => value.Name, value => value.Value.GetString()!, StringComparer.Ordinal),
                        Dataset = dataset,
                    })]);
            }
        }

        return catalogs.ToFrozenDictionary(StringComparer.Ordinal);
    }
}
