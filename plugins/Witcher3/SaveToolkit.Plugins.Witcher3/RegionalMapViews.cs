using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.Witcher3;

/// <summary>Reference art stays separate from saved tags until native region/position joins are proven.</summary>
internal sealed class RegionalMapViews(IReferenceContext references)
{
    public const string Prefix = "map-art/";
    public const string PackId = "witcher3.maps";
    public const string SourceUrl = "https://github.com/witcher3map/witcher3map-maps/tree/df7fa74609df7587afc324e569d2574cf5b4f1ec";
    private static readonly (string Id, string Title, int Width, int Height)[] Regions = [
        ("white_orchard", "White Orchard", 1280, 1024), ("velen", "Velen and Novigrad", 1792, 2048),
        ("skellige", "Skellige", 2048, 2048), ("kaer_morhen", "Kaer Morhen", 1024, 1280),
        ("toussaint", "Toussaint", 1280, 1280),
    ];

    public IReadOnlyList<ViewNode> Build() => Regions.Select(region => (ViewNode)SectionNode.Lazy(
        $"Reference map · {region.Title} (saved tag alignment unknown)", Prefix + region.Id)).ToArray();

    public IReadOnlyList<ViewNode> Expand(string lazyId)
    {
        var region = Regions.SingleOrDefault(r => Prefix + r.Id == lazyId);
        if (region.Id is null) { throw new KeyNotFoundException(lazyId); }

        var assetId = $"{PackId}:maps/{region.Id}.webp";
        var provenance = references.AssetProvenance(assetId);
        var available = provenance is not null && references.GetAsset(assetId) is not null;
        var explanation = $"Reference artwork for {region.Title}. Native saved-tag region and coordinate joins are unknown. " +
            "No saved markers are plotted or assigned to this region. Use the separate saved map-tag text list for discovery memberships. " +
            (available ? "Publisher map art embedded with attribution per owner decision 2026-10-29. Unofficial fan work, not endorsed by CD PROJEKT RED." :
                "The local map pack is unavailable; saved tags and the external regional guide remain available.");
        return [new MapNode($"{region.Title} · reference artwork", new MapBounds(0, 0, region.Width, region.Height), [], explanation) {
            CoordinateSpace = "Reference artwork pixels; no native save-coordinate transform is known",
            Background = available ? new MapBackground(assetId, $"{region.Title} map artwork, with no saved marker overlay",
                provenance!.Attribution, EmbeddingPermitted: true) : null,
            ExternalSource = new(MapViews.SourceUrl, "Community regional maps",
                "untamed0 and witcher3map contributors (CC BY-NC-SA); CD PROJEKT RED (artwork). Native saved-tag alignment unknown."),
        }];
    }
}
