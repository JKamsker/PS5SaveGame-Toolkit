using System.Globalization;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.Pragmata.Views;

/// <summary>Community pixel locations are reference facts, never decoded save checkpoints.</summary>
internal sealed class CommunityAtlas(IReferenceContext references)
{
    internal const string CatalogId = "pragmata-map-locations";
    internal const string BackgroundId = "pragmata.evidence.en:maps/cradle.webp";
    internal const double SourceWidth = 7168;
    internal const double SourceHeight = 2048;
    internal const double DisplayWidth = 2048;
    internal const double DisplayHeight = 585;

    public MapNode Attach(MapNode map)
    {
        if (references.Catalog(CatalogId) is not { } locations || references.GetAsset(BackgroundId) is null ||
            references.AssetProvenance(BackgroundId) is not { } art)
            return map;

        var markers = locations.Search(new ReferenceQuery { Limit = LazyPages.PageSize }).Items
            .Where(record => Position(record, "x", SourceWidth) is not null && Position(record, "y", SourceHeight) is not null)
            .Select(record => new MapMarker($"community/{record.Key}", record.Name, MapMarkerStatus.Unknown, record.Category ?? "Escape hatch")
            {
                X = Position(record, "x", SourceWidth), Y = Position(record, "y", SourceHeight),
                Confidence = Confidence.Inferred,
                Description = "Community escape-hatch reference. Save checkpoint-hash association, unlocked state and completion are Unknown. " +
                    $"{record.Description} {record.AttributionText}",
                ExternalLink = new ExternalViewLink(CheckpointMap.ExternalMapUrl, "Open community map", record.AttributionText),
            });
        return map with
        {
            Title = "Cradle reference atlas and stored checkpoint hashes",
            Bounds = new MapBounds(0, 0, DisplayWidth, DisplayHeight),
            Transform = new MapTransform(DisplayWidth / SourceWidth, DisplayHeight / SourceHeight),
            CoordinateSpace = "Itemaps Cradle atlas, original 7168 × 2048 top-left image pixels; X rightwards, Y downwards",
            CoordinateAttribution = locations.Dataset.Attribution,
            CoordinateEmbeddingPermitted = true,
            Background = new MapBackground(BackgroundId, "The Cradle: community atlas of Pragmata's regions", art.Attribution,
                EmbeddingPermitted: true),
            Markers = [.. map.Markers, .. markers],
            TextAlternative = "The background and 26 positioned escape hatches are community references. " +
                "All saved unlock/completion states are Unknown. Stored checkpoint hashes remain in the text list without positions; " +
                "no mapping between those hashes and the reference locations is claimed. " +
                "Coordinates use the source image's pixel frame, scaled to the local background. The list remains available without the image.",
        };
    }

    private static double? Position(ReferenceRecord record, string key, double maximum) =>
        double.TryParse(record.Attributes.GetValueOrDefault(key), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) &&
        double.IsFinite(value) && value >= 0 && value <= maximum ? value : null;
}
