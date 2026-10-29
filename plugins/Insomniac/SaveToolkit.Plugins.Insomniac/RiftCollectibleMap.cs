using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.Insomniac;

internal static class RiftCollectibleMap
{
    internal const string GuideUrl = "https://guides4gamers.com/ratchet-and-clank-rift-apart/maps/";
    internal const string GuideAttribution = "Guides4Gamers, Ratchet & Clank: Rift Apart interactive maps; " +
        "accessed 2026-10-28. Link only for these storage bits: no guide-to-save identity join. " +
        "Separate community guide maps contain attributed reference coordinates and Guides4Gamers backgrounds " +
        "(including Insomniac/SIE material, used with attribution per owner decision 2026-10-29).";
    private const string FieldsTitle = "Map storage values";
    private const int MaskBits = 64;

    internal static IReadOnlyList<ViewNode> Build(DdlSections sections, IReferenceCatalog? catalog,
        Func<string, string, FieldSpec, FieldValue, FieldNode> bind)
    {
        var mask = sections.Roots.GetValueOrDefault(RiftPlayerView.CampaignTag)?.GetValueOrDefault("RYNOPlansCollected");
        if (mask is not { Values.Count: 1 } || mask.Values[0].Kind != FieldValueKind.Integer) return [];
        var bits = mask.Values[0].AsUInt64();
        var fields = new List<ViewNode>();
        var markers = Enumerable.Range(0, MaskBits).Select(bit =>
        {
            var reference = catalog?.Find($"spybot/{bit}");
            var set = (bits & (1UL << bit)) != 0;
            var id = $"spatial/spybot/{bit}";
            var label = $"Bit {bit}: {reference?.Name ?? "unresolved identity"}";
            var description = $"Stored RYNOPlansCollected bit {bit} is {(set ? "set" : "clear")}. " +
                "A clear/reserved bit does not establish a missing pickup; found/missing status and physical coordinates are unconfirmed. " +
                (reference is null ? "No catalog join. " + DdlSections.Attribution : reference.Description + " " + reference.AttributionText);
            fields.Add(bind(id, label, new BoolSpec(), FieldValue.Of(set)) with
            {
                Confidence = Confidence.Verified,
                Description = description,
            });
            return new MapMarker(id, label, MapMarkerStatus.Unknown, reference?.Name ?? "Unresolved bits")
            {
                Description = description[..Math.Min(description.Length, SpatialViewLimits.MaxTextLength)],
                Confidence = reference is null ? Confidence.Unknown : Confidence.Inferred,
                Target = new(ViewTargetKind.Field, id) { Path = ["tab:Collectibles", $"section:{FieldsTitle}"] },
            };
        }).ToArray();
        return
        [
            new MapNode("Spybot storage and external planet maps", new(0, 0, 1, 1), markers,
                "All 64 stored spybot bits with inferred catalog labels and exact set/clear values in their descriptions. " +
                "Unknown means uncalibrated pickup status, including reserved bits. No positions, map art, fog transform or prerequisite joins are claimed.")
            {
                CoordinateSpace = "External guide only; no calibrated save coordinates",
                ExternalSource = new(GuideUrl, "Open planet maps (guides4gamers.com)", GuideAttribution),
            },
            new SectionNode(FieldsTitle, fields),
        ];
    }
}
