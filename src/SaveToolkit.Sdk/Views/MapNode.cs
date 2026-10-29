using System.Text.Json.Serialization;
using SaveToolkit.Sdk.References;

namespace SaveToolkit.Sdk.Views;

/// <summary>Known discovery state of a map location; Unknown is distinct from a confirmed missing location.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<MapMarkerStatus>))]
public enum MapMarkerStatus
{
    /// <summary>Confirmed found or completed according to the marker description.</summary>
    Found,
    /// <summary>Confirmed not found or not completed.</summary>
    Missing,
    /// <summary>No confirmed discovery or completion state.</summary>
    Unknown,
}

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

/// <summary>A stable map location, optionally positioned or linked to an existing control or attributed source.</summary>
public sealed record MapMarker(string Id, string Label, MapMarkerStatus Status, string Category)
{
    /// <summary>Native coordinates; omit both for an external-only or not-yet-mapped location.</summary>
    public double? X { get; init; }
    /// <summary>Native vertical coordinate; set or omit together with X.</summary>
    public double? Y { get; init; }
    /// <summary>Optional explanation or attribution for the location and its status.</summary>
    public string? Description { get; init; }
    /// <summary>Optional navigation to an existing control or row.</summary>
    public ViewTarget? Target { get; init; }
    /// <summary>Optional attributed HTTPS source, opened only on user request.</summary>
    public ExternalViewLink? ExternalLink { get; init; }
    /// <summary>Evidence supporting this marker's interpretation.</summary>
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
    /// <summary>Explicit native-to-display scale and offset.</summary>
    public MapTransform Transform { get; init; } = new();
    /// <summary>Human-readable name for the native coordinate system.</summary>
    public string CoordinateSpace { get; init; } = "Schematic";
    /// <summary>Provenance required when any marker coordinates are embedded.</summary>
    public ReferenceAttribution? CoordinateAttribution { get; init; }
    /// <summary>Explicit plugin-author assertion that embedding the attributed coordinates is permitted.</summary>
    public bool CoordinateEmbeddingPermitted { get; init; }
    /// <summary>Optional attributed local background asset.</summary>
    public MapBackground? Background { get; init; }
    /// <summary>Optional attributed external map source; never automatically fetched or embedded.</summary>
    public ExternalViewLink? ExternalSource { get; init; }
}
