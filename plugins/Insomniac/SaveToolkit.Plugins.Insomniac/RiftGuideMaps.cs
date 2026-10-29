using System.Globalization;
using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.Insomniac;

internal static class RiftGuideMaps
{
    internal const string CatalogId = "rift-guide-markers";
    internal const string SectionTitle = "Community guide maps";
    private const string BackgroundPackId = "insomniac.rift.maps.en";
    private const string UnjoinedWarning = "Community guide coordinates only. Pickup status and guide-to-save identity " +
        "are unknown; these markers do not reflect your collection progress.";

    internal static SectionNode? Build(IReferenceCatalog? catalog, IReferenceContext references,
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
            lazy[id] = () => [Map(catalog, region, first, width, height, references)];
            regions.Add(new SectionNode(region, [])
            {
                LazyId = id,
                Description = UnjoinedWarning + " " + first.AttributionText,
            });
        }
        return new SectionNode(SectionTitle, regions) { Description = UnjoinedWarning };
    }

    private static MapNode Map(IReferenceCatalog catalog, string region, ReferenceRecord first, double width, double height,
        IReferenceContext references)
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
            Background = Background(first, region, references),
            CoordinateAttribution = first.Dataset.Attribution,
            CoordinateEmbeddingPermitted = true,
            ExternalSource = new(RiftCollectibleMap.GuideUrl, "Open regional guide index", first.AttributionText),
        };
    }

    private static MapBackground? Background(ReferenceRecord first, string region, IReferenceContext references)
    {
        // A user catalogue may reuse the map ID with a different coordinate extent or revision.
        // Only the pinned embedded marker snapshot is aligned with these embedded backgrounds.
        if (first.Dataset.PackId != BackgroundPackId) return null;
        var assetId = $"{BackgroundPackId}:maps/{first.Attributes["mapId"]}.webp";
        var provenance = references.AssetProvenance(assetId);
        return provenance is not null && references.GetAsset(assetId) is not null
            ? new(assetId, region + " guide map; full extent, top-left origin, uniformly downscaled without cropping.",
                provenance.Attribution, EmbeddingPermitted: true)
            : null;
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
