using System.Globalization;
using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.EldenRing;

/// <summary>Optional F2 art/placement, kept separate from save bit interpretation.</summary>
internal sealed class ProgressMapAssets(IReferenceContext references)
{
    public const string CatalogId = "eldenring-map-positions";
    public const string Source = "https://github.com/EthanShoeDev/elden-ring-compass/tree/be49e96b0513d8e08d22806b5b3fee9654e3e9de";
    public const string PackId = "eldenring.names.en";
    public const string Rights = "Rights reserved by FromSoftware / Bandai Namco; used with attribution per owner decision 2026-10-29";
    internal const double NativeSize = 10496;
    private readonly Lazy<IReferenceCatalog?> _positions = new(() => references.Catalog(CatalogId));

    public MapNode Decorate(MapNode fallback, ProgressRegion region)
    {
        var catalog = _positions.Value;
        if (catalog is null || !SupportedProvenance(catalog.Dataset)) return fallback;
        var positions = region.Entries.Select(entry => Position(catalog.Find(entry.EventId.ToString(CultureInfo.InvariantCulture))))
            .ToArray();
        // A region can contain multiple layers. Place only its dominant layer; retain every other candidate in the list.
        var layer = positions.OfType<Placement>().GroupBy(position => position.Layer)
            .OrderByDescending(group => group.Count()).ThenBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => group.Key).FirstOrDefault();
        if (layer is null) return fallback;
        var markers = fallback.Markers.Select((marker, index) => positions[index] is { } position && position.Layer == layer
            ? marker with { X = position.X, Y = position.Y,
                Description = PlacementDescription(marker.Description, position.Method) }
            : marker).ToArray();
        var placed = markers.Count(marker => marker.X.HasValue);
        var assetId = $"{PackId}:maps/{layer}.webp";
        var provenance = references.AssetProvenance(assetId);
        var background = provenance is not null && SupportedProvenance(provenance) && references.GetAsset(assetId) is not null
            ? new MapBackground(assetId, LayerName(layer) + " · extracted base map", provenance.Attribution, EmbeddingPermitted: true)
            : null;
        return fallback with
        {
            Title = region.Title + " · " + LayerName(layer),
            Bounds = new(0, 0, NativeSize, NativeSize),
            Markers = markers,
            CoordinateSpace = "Compass native master pixels (10496 square); X east, Y south; identity display transform",
            CoordinateAttribution = catalog.Dataset.Attribution,
            CoordinateEmbeddingPermitted = true,
            Background = background,
            TextAlternative = $"{placed} of {markers.Length} source candidates positioned on {LayerName(layer)}. "
                + "All statuses remain Unknown. Alignment is Inferred; legacy dungeons use nearest-basepoint translation. "
                + "Missing, ambiguous, unsupported or other-layer locations stay unpositioned in the text list. "
                + "The table retains raw source bits and evidence; no completion or quest semantics are inferred.",
        };
    }

    private static bool SupportedProvenance(ReferenceDatasetInfo dataset) => dataset.PackId == PackId
        && dataset.Attribution.SourceUrl == Source && dataset.Attribution.License == Rights;

    private static string PlacementDescription(string? description, string method)
    {
        var suffix = " Placement: Inferred datamined " + method + "; not console-verified.";
        var available = SpatialViewLimits.MaxTextLength - suffix.Length;
        if (description?.Length > available)
        {
            if (char.IsHighSurrogate(description[available - 1])) available--;
            description = description[..available];
        }
        return description + suffix;
    }

    private static Placement? Position(ReferenceRecord? record)
    {
        if (record is null || record.Attributes.GetValueOrDefault("layer") is not ("M00" or "M01" or "M10")
            || record.Attributes.GetValueOrDefault("projection") is not ("overworld-grid" or "legacy-nearest-basepoint")
            || !Coordinate(record, "x", out var x) || !Coordinate(record, "y", out var y)) return null;
        return new(record.Attributes["layer"], x, y, record.Attributes["projection"]);
    }

    private static bool Coordinate(ReferenceRecord record, string key, out double value) =>
        double.TryParse(record.Attributes.GetValueOrDefault(key), NumberStyles.Float, CultureInfo.InvariantCulture, out value)
        && double.IsFinite(value) && value is >= 0 and <= NativeSize;

    private static string LayerName(string layer) => layer switch
    {
        "M00" => "Lands Between surface", "M01" => "Lands Between underground", "M10" => "Realm of Shadow surface",
        _ => throw new ArgumentOutOfRangeException(nameof(layer)),
    };

    private sealed record Placement(string Layer, double X, double Y, string Method);
}
