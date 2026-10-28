using System.Globalization;
using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.Insomniac;

internal static class RiftGuideMaps
{
    internal const string CatalogId = "rift-guide-markers";
    internal const string SectionTitle = "Community guide maps";
    private const string UnjoinedWarning = "Community guide coordinates only. Pickup status and guide-to-save identity " +
        "are unknown; these markers do not reflect your collection progress. No publisher artwork is embedded.";

    internal static SectionNode? Build(IReferenceCatalog? catalog,
        IDictionary<string, Func<IReadOnlyList<ViewNode>>> lazy)
    {
        if (catalog is null) return null;
        var regions = new List<ViewNode>();
        foreach (var region in catalog.Categories.Take(SpatialViewLimits.MaxItems))
        {
            var first = catalog.Search(new() { Category = region, Limit = 1 }).Items.FirstOrDefault();
            if (first is null || !TryGeometry(first, out _, out _, out var width, out var height) ||
                !int.TryParse(first.Attributes.GetValueOrDefault("mapId"), CultureInfo.InvariantCulture, out var mapId)) continue;
            var id = $"guide-map/{mapId}";
            if (lazy.ContainsKey(id)) continue;
            lazy[id] = () => [Map(catalog, region, first, width, height)];
            regions.Add(new SectionNode(region, [])
            {
                LazyId = id,
                Description = UnjoinedWarning + " " + first.AttributionText,
            });
        }
        return new SectionNode(SectionTitle, regions) { Description = UnjoinedWarning };
    }

    private static MapNode Map(IReferenceCatalog catalog, string region, ReferenceRecord first, double width, double height)
    {
        var markers = new List<MapMarker>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var offset = 0; offset < SpatialViewLimits.MaxItems; offset += ReferenceQuery.MaxLimit)
        {
            var page = catalog.Search(new()
            {
                Category = region, Offset = offset,
                Limit = Math.Min(ReferenceQuery.MaxLimit, SpatialViewLimits.MaxItems - offset),
            });
            foreach (var record in page.Items)
            {
                if (!TryGeometry(record, out var x, out var y, out var recordWidth, out var recordHeight) ||
                    recordWidth != width || recordHeight != height ||
                    record.Attributes.GetValueOrDefault("mapId") != first.Attributes.GetValueOrDefault("mapId") ||
                    !seen.Add(record.Key)) continue;
                markers.Add(new("guide/" + record.Key, record.Name, MapMarkerStatus.Unknown,
                    record.Attributes.GetValueOrDefault("guideCategory") ?? "Other")
                {
                    X = x, Y = y, Confidence = Confidence.Inferred,
                    Description = UnjoinedWarning + " " + record.AttributionText,
                    ExternalLink = record.Source is { } source ? new(source, "Open guide marker", record.AttributionText) : null,
                });
            }
            if (page.Items.Count == 0 || offset + page.Items.Count >= page.Total) break;
        }
        return new MapNode(region + " · community guide", new(0, 0, width, height), markers,
            $"{markers.Count} reference markers shown (at most {SpatialViewLimits.MaxItems} per region). " + UnjoinedWarning)
        {
            CoordinateSpace = "Guides4Gamers map pixels; origin top-left, X rightwards, Y downwards. " +
                "No transform from save/fog/world coordinates has been established.",
            Transform = new(),
            CoordinateAttribution = first.Dataset.Attribution,
            CoordinateEmbeddingPermitted = true,
            ExternalSource = new(RiftCollectibleMap.GuideUrl, "Open regional guide index", first.AttributionText),
        };
    }

    private static bool TryGeometry(ReferenceRecord record, out double x, out double y, out double width, out double height)
    {
        x = y = width = height = 0;
        return Number("x", out x) && Number("y", out y) && Number("width", out width) && Number("height", out height) &&
            width > 0 && height > 0 && x >= 0 && x <= width && y >= 0 && y <= height;

        bool Number(string key, out double value) =>
            double.TryParse(record.Attributes.GetValueOrDefault(key), NumberStyles.Float, CultureInfo.InvariantCulture, out value) &&
            double.IsFinite(value) && Math.Abs(value) <= SpatialViewLimits.MaxCoordinate;
    }
}
