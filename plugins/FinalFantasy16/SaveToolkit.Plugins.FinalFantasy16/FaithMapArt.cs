using System.Globalization;
using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.FinalFantasy16;

internal static class FaithMapArt
{
    public const string CatalogId = "ffxvi_map_art";
    public const string RegionalLazyId = "regional-map-art";
    public const string OfficialUrl = "https://na.finalfantasyxvi.com/story";
    public const string RegionalUrl = "https://game8.co/games/Final-Fantasy-XVI/archives/410003";
    private const int MaximumImageDimension = 2048;
    private const int RegionCount = 4;

    public static MapNode WithBackground(MapNode map, IReferenceContext references, string key)
    {
        var record = references.Catalog(CatalogId)?.Find(key);
        if (record?.AssetId is not { } asset || references.GetAsset(asset) is null ||
            references.AssetProvenance(asset) is not { } provenance) return map;
        var width = Dimension(record, "width");
        var height = Dimension(record, "height");
        if (width is null || height is null) return map;
        return map with
        {
            Bounds = new MapBounds(0, 0, width.Value, height.Value),
            Background = new MapBackground(asset, record.Name + " — reference artwork", provenance.Attribution,
                EmbeddingPermitted: true),
        };
    }

    public static IReadOnlyList<ViewNode> Regions(IReferenceContext references)
    {
        var regions = references.Catalog(CatalogId)?.Search(new ReferenceQuery
        {
            Category = "regional", SortBy = ReferenceSortKey.Name, Limit = RegionCount,
        }).Items ?? [];
        if (regions.Count == 0)
            return [new TextNode("Regional artwork is unavailable. The stored location and external guide remain available.")];
        return [new TextNode("Spoilers: regional screenshots contain reference labels and obelisk symbols from the source. " +
            "These are part of the artwork, not discovery/completion markers read from your save. " +
            "No save-to-art coordinate transform has been established."),
            .. regions.Select(record =>
            {
                var map = WithBackground(new MapNode(record.Name + " — reference map", new MapBounds(0, 0, 1, 1), [],
                    "Regional reference artwork only. No saved positions, visited state or collectible totals are mapped. " +
                    "Consult the stored-location text list and the source guide for location information.")
                {
                    CoordinateSpace = "Image pixels; no positioned save markers or save-to-image transform",
                    ExternalSource = new ExternalViewLink(RegionalUrl, "Game8 regional map guide",
                        "Square Enix game artwork; Game8 screenshot and guide annotations."),
                }, references, record.Key);
                SpatialViewValidation.Validate(map);
                return (ViewNode)map;
            })];
    }

    private static int? Dimension(ReferenceRecord record, string key) =>
        int.TryParse(record.Attributes.GetValueOrDefault(key), NumberStyles.None, CultureInfo.InvariantCulture, out var value)
            && value is > 0 and <= MaximumImageDimension ? value : null;
}
