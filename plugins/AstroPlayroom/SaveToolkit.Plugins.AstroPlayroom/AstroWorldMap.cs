using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.AstroPlayroom;

/// <summary>Manually digitized selection-screen anchors; never physical collectible coordinates.</summary>
internal static class AstroWorldMap
{
    public const string AssetId = "astro.catalogue.en:maps/world-selection.webp";
    private const string SourceUrl = "https://github.com/JKamsker/PS5SaveGame-Toolkit/tree/main/plugins/AstroPlayroom";
    private const double CropLeft = 40;
    private const double CropTop = 50;
    private const double Width = 1140;
    private const double Height = 930;
    private const double FlagSeparation = 12;
    private static readonly IReadOnlyDictionary<string, (double X, double Y)> Anchors =
        new Dictionary<string, (double X, double Y)>(StringComparer.Ordinal)
        {
            ["World/CPU_PLAZA"] = (620, 525),
            ["World/PS_LABO"] = (390, 870),
            ["World/COOLING_SYSTEM/BEACH"] = (620, 350),
            ["World/COOLING_SYSTEM/SPRING_1"] = (390, 350),
            ["World/COOLING_SYSTEM/ICE_PLATFORMING"] = (390, 185),
            ["World/COOLING_SYSTEM/SPRING_2"] = (620, 185),
            ["World/GPU_JUNGLE/FOREST"] = (850, 525),
            ["World/GPU_JUNGLE/CLIMBING_1"] = (1085, 525),
            ["World/GPU_JUNGLE/HIGH_PLATFORMING"] = (1085, 350),
            ["World/GPU_JUNGLE/CLIMBING_2"] = (850, 350),
            ["World/MEMORY_HEAVEN/WEATHER"] = (390, 525),
            ["World/MEMORY_HEAVEN/BALL_1"] = (160, 525),
            ["World/MEMORY_HEAVEN/CLOUD_PLATFORMING"] = (160, 700),
            ["World/MEMORY_HEAVEN/BALL_2"] = (390, 700),
            ["World/SSD_SPEEDWAY/GLIDING"] = (620, 700),
            ["World/SSD_SPEEDWAY/JETPACK_1"] = (850, 700),
            ["World/SSD_SPEEDWAY/SPACE_PLATFORMING"] = (850, 870),
            ["World/SSD_SPEEDWAY/JETPACK_2"] = (620, 870),
        };

    public static MapNode? Create(IReadOnlyList<MapMarker> markers, IReadOnlyList<AstroField> fields,
        AstroReferences references)
    {
        // A raw/unknown schema must not acquire inferred positions merely because art is present.
        if (references.World("World/CPU_PLAZA") is null || references.Asset(AssetId) is null ||
            references.AssetProvenance(AssetId) is not { } provenance) return null;
        var paths = fields.ToDictionary(field => field.Token.Id, StringComparer.Ordinal);
        var positioned = markers.Select(marker => Position(marker, paths[marker.Id])).ToArray();
        var map = new MapNode("World selection — reference artwork", new MapBounds(0, 0, Width, Height), positioned,
            "Reference world-selection artwork with Inferred stage anchors. Markers show this save's IsVisited/IsCleared flags only; baked-in crowns and artwork are static source state. Unmapped world flags remain in the text list; the original schematic is also available.")
        {
            Background = new MapBackground(AssetId, "ASTRO's PLAYROOM world-selection diagram; static reference artwork",
                provenance.Attribution, EmbeddingPermitted: true),
            Transform = new MapTransform(1, 1, -CropLeft, -CropTop),
            CoordinateSpace = "Original 1920x1080 screenshot pixels; crop origin (40,50), 1140x930 background",
            CoordinateEmbeddingPermitted = true,
            CoordinateAttribution = new ReferenceAttribution(SourceUrl, "PS5 Save Game Toolkit contributors",
                "GPL-3.0-or-later (original digitized anchors)", "https://www.gnu.org/licenses/gpl-3.0.html",
                new DateOnly(2026, 10, 29),
                "Manually digitized stage-tile centers in the ActuGaming screenshot; candidate aliases/order are Inferred. IsVisited is 12 px left and IsCleared 12 px right of each center. No physical locations or prerequisite edges inferred."),
        };
        SpatialViewValidation.Validate(map);
        return map;
    }

    private static MapMarker Position(MapMarker marker, AstroField field)
    {
        var path = field.Path["PreInstall/".Length..];
        var parent = path[..path.LastIndexOf('/')];
        return Anchors.TryGetValue(parent, out var point)
            ? marker with
            {
                X = point.X + (field.Label == "IsCleared" ? FlagSeparation : -FlagSeparation),
                Y = point.Y,
                Description = marker.Description + " Inferred selection-screen tile anchor; not a physical pickup position."
            }
            : marker;
    }
}
