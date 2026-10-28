using SaveToolkit.Sdk.Views;
using System.Security.Cryptography;
using System.Text;

namespace SaveToolkit.Plugins.GenericJson;

/// <summary>Observed minigame records, with link-only location guidance and no invented coordinates.</summary>
internal static class JsonProfileMap
{
    internal const string GuideUrl = "https://www.powerpyx.com/it-takes-two-all-minigame-locations/";

    public static MapNode Build(JsonOverlay overlay)
    {
        var markers = overlay.Groups.Where(n => n.Label.StartsWith("Minigame", StringComparison.Ordinal))
            .GroupBy(n => JsonOverlayViews.GameAndField(n.Label).Game, StringComparer.Ordinal)
            .Select(group =>
            {
                var first = group.First();
                var identity = "minigame/" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(group.Key)));
                return new MapMarker(identity, Clip(JsonLabels.Friendly(group.Key)), MapMarkerStatus.Unknown, "Present minigame records")
                {
                    Description = "This profile contains " + group.Count() + " related fields. Stored flags/counters do not prove completion, location or prerequisites. "
                        + JsonOverlay.Provenance,
                    Confidence = Confidence.Inferred,
                    Target = JsonNavigation.Field(first),
                };
            }).ToArray();
        return new("Recorded minigames — external location guide", new(0, 0, 1, 1), markers,
            "Only minigames with present profile fields are listed. All completion/location states are unknown. Open a marker's text link to its raw field; use the external guide for locations. No map art or coordinates are embedded.")
        {
            CoordinateSpace = "External guide only; coordinates unknown",
            ExternalSource = new(GuideUrl, "PowerPyx minigame locations", "PowerPyx, 2021-03-26. Research/location guidance only; reuse licence unestablished. Link only, no guide text, artwork or coordinates imported."),
        };
    }

    private static string Clip(string text) => string.IsNullOrWhiteSpace(text) ? "(empty minigame key)"
        : text.Length <= 512 ? text : text[..512] + "…";
}
