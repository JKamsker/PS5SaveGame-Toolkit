using System.Text.Json.Serialization;
using SaveToolkit.Sdk.References;

namespace SaveToolkit.Sdk.Views;

[JsonConverter(typeof(JsonStringEnumConverter<MapMarkerStatus>))]
public enum MapMarkerStatus { Found, Missing, Unknown }

/// <summary>An attributed link, never an image URL, iframe, tile source or fetch instruction. HTTPS only.</summary>
public sealed record ExternalViewLink(string Url, string Label, string Attribution);

/// <summary>Display bounds after transformation. X increases rightwards; Y increases downwards.</summary>
public sealed record MapBounds(double MinX, double MinY, double MaxX, double MaxY);

/// <summary>Display position = native position * scale + offset, separately for each axis.</summary>
public sealed record MapTransform(double ScaleX = 1, double ScaleY = 1, double OffsetX = 0, double OffsetY = 0);

/// <summary>
/// Local F2 asset (packId:path), never a remote URL. EmbeddingPermitted is the plugin author's explicit
/// assertion that the asset licence permits embedding. An unclear licence requires ExternalSource instead.
/// </summary>
public sealed record MapBackground(string AssetId, string Alt, ReferenceAttribution Attribution,
    bool EmbeddingPermitted = false);

public sealed record MapMarker(string Id, string Label, MapMarkerStatus Status, string Category)
{
    /// <summary>Native coordinates; omit both for an external-only or not-yet-mapped location.</summary>
    public double? X { get; init; }
    public double? Y { get; init; }
    public string? Description { get; init; }
    public ViewTarget? Target { get; init; }
    public ExternalViewLink? ExternalLink { get; init; }
    [JsonConverter(typeof(JsonStringEnumConverter<Confidence>))]
    public Confidence? Confidence { get; init; }
}

/// <summary>
/// Bounded, read-only map with a mandatory text summary and an always-available marker list.
/// Default: link to ExternalSource and omit artwork/coordinates. Embedded coordinates require full
/// provenance and an explicit permission assertion, including original schematic/save-derived data.
/// </summary>
public sealed record MapNode(string Title, MapBounds Bounds, IReadOnlyList<MapMarker> Markers,
    string TextAlternative) : ViewNode
{
    public MapTransform Transform { get; init; } = new();
    public string CoordinateSpace { get; init; } = "Schematic";
    public ReferenceAttribution? CoordinateAttribution { get; init; }
    public bool CoordinateEmbeddingPermitted { get; init; }
    public MapBackground? Background { get; init; }
    public ExternalViewLink? ExternalSource { get; init; }
}
