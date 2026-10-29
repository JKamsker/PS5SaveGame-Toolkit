using System.Collections.Frozen;
using System.Text.Json;
using SaveToolkit.Sdk;
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
        "Extracted type IDs, names, categories and AP tables; added attributed official descriptions, common item-effect summaries and compact asset links.");

    public static AssetContent? Asset(string assetId)
    {
        var separator = assetId.IndexOf(':');
        if (separator <= 0 || separator == assetId.Length - 1) return null;
        var pack = assetId[..separator];
        var path = assetId[(separator + 1)..];
        if (pack is not ("ff7r.remake.en" or "ff7r.rebirth.en") || path.Contains("..", StringComparison.Ordinal))
            return null;
        using var stream = typeof(EmbeddedCatalogs).Assembly.GetManifestResourceStream($"refpacks/{pack}/{path}");
        if (stream is null) return null;
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        var contentType = path.EndsWith(".webp", StringComparison.OrdinalIgnoreCase) ? "image/webp" :
            path.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ? "image/png" : "image/jpeg";
        return new AssetContent(contentType, memory.ToArray());
    }

    public static ReferenceDatasetInfo? AssetProvenance(string assetId)
    {
        var separator = assetId.IndexOf(':');
        if (separator <= 0 || separator == assetId.Length - 1) return null;
        var pack = assetId[..separator];
        var path = assetId[(separator + 1)..];
        if (pack is not ("ff7r.remake.en" or "ff7r.rebirth.en")) return null;
        var dataset = path.StartsWith("maps/", StringComparison.Ordinal) ? "maps" :
            path.StartsWith("icons/", StringComparison.Ordinal) ? "icons" : null;
        return dataset is null ? null : new ReferenceDatasetInfo(pack, dataset, ReferenceDatasetKind.Assets,
            dataset == "maps" ? MapAttribution : IconAttribution);
    }

    private static readonly ReferenceAttribution IconAttribution = new(
        "https://finalfantasy.fandom.com/wiki/Category:Final_Fantasy_VII_Remake_item_images",
        "Final Fantasy Wiki contributors; Square Enix rights holder",
        "Rights reserved by Square Enix; used with attribution per owner decision 2026-10-29",
        "https://finalfantasy.fandom.com/wiki/Final_Fantasy_Wiki:Copyrights", new(2026, 10, 29),
        "Selected item, materia and weapon imagery; downscaled to 64x64 WebP for the toolkit.");

    private static readonly ReferenceAttribution MapAttribution = new(
        "https://www.powerpyx.com/final-fantasy-7-vii-rebirth-full-world-map-revealed/",
        "PowerPyx; Square Enix rights holder",
        "Rights reserved by Square Enix; used with attribution per owner decision 2026-10-29",
        "https://www.powerpyx.com/terms-of-service/", new(2026, 10, 29),
        "Six regional map captures; downscaled to a 2048px maximum edge and WebP quality 65.");

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
                        AssetId = row.TryGetProperty("assetId", out var asset) && asset.GetString() is { } path
                            ? $"ff7r.{game}.en:{path}" : null,
                        Dataset = dataset,
                    })]);
            }
        }

        return catalogs.ToFrozenDictionary(StringComparer.Ordinal);
    }
}
