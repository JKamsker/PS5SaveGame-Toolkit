using System.Collections.Frozen;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.References;

namespace SaveToolkit.Plugins.FinalFantasy7;

/// <summary>Offline fallback for direct Open callers; the host loads the same F2 packs.</summary>
internal static class EmbeddedCatalogs
{
    public const string Revision = "c7bdd6d8228304b87dcdf15e57155d092eb06fd2";
    public const string SourceRoot = "https://github.com/RyudoSynbios/game-tools-collection/blob/" + Revision;
    public const string Attribution = "RyudoSynbios / Game Tools Collection, MIT (revision " + Revision + ").";
    private static readonly EmbeddedReferenceContext Embedded =
        EmbeddedReferenceContext.Load(typeof(EmbeddedCatalogs).Assembly);
    private static readonly int MaxRecords = new EmbeddedReferencePackLimits().MaxRecordsPerCatalog;
    private static readonly FrozenSet<string> ResourceNames = typeof(EmbeddedCatalogs).Assembly
        .GetManifestResourceNames().ToFrozenSet(StringComparer.Ordinal);

    public static IReadOnlyList<ReferenceRecord> Records(Ff7Schema schema, string catalogId)
    {
        var titleId = schema.IsRemake ? "PPSA01911" : schema.IsDemo ? "PPSA18936" : "PPSA08664";
        var catalog = Embedded.Pin("ff7r", titleId).Catalog(catalogId, schema.Name)
            ?? throw new InvalidOperationException("Embedded FFVII catalog is missing.");
        // Preserve the historical fallback attribution; host overlays retain their own dataset metadata.
        var dataset = new ReferenceDatasetInfo(catalog.Pack.Id, catalogId, ReferenceDatasetKind.Records,
            Provenance(schema.IsRemake ? "remake" : "rebirth", "utils/resource.ts"));
        return ReferenceCatalogReader.ReadBounded(catalog, new(), MaxRecords)
            .Select(record => record with { Dataset = dataset }).ToArray();
    }

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
        // Keep the old literal resource availability, including Windows backslashes. SDK decoding
        // normalizes those names; exposing newly resolved images here would change direct-open views.
        if (path.Contains("..", StringComparison.Ordinal) || !ResourceNames.Contains($"refpacks/{pack}/{path}"))
            return null;
        return Embedded.GetAsset(assetId.Replace('\\', '/'));
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
}
