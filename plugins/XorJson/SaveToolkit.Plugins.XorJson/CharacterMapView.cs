using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.XorJson;

internal static class CharacterMapView
{
    private const string Title = "Stored locations (unpositioned)";
    private const string Controls = "Location controls";
    private const int LabelLimit = 512;
    private const string Source = "https://github.com/IshiakiZ/mcd2-save-editor/blob/84ffee67b73e49d5b888e31257ce9767aa8ab826/dungeons2_editor/data/world.json";

    public static ViewNode Build(JsonValueToken root, IReferenceContext references, Func<JsonValueToken, ViewNode> view)
    {
        var hero = root.Member("CharacterSaveV1")!;
        var world = hero.Member("WorldExploration");
        var location = hero.Member("MetaData")?.Member("CurrentLocation");
        var tokens = (world is null ? Enumerable.Empty<JsonValueToken>() : JsonEditModel.Walk(world))
            .Concat(location is null ? [] : [location])
            .Where(node => node.Scalar.Kind == FieldValueKind.Text && IsLocation(node.Scalar.AsString()))
            .Take(SpatialViewLimits.MaxItems + 1).ToArray();
        var shown = tokens.Take(SpatialViewLimits.MaxItems).ToArray();
        var catalog = references.Catalog("xorjson-world");
        var markers = shown.Select(node =>
        {
            var id = node.Scalar.AsString();
            var record = catalog?.Find(id);
            return new MapMarker(node.ValueId, Clip(record?.Name ?? id), MapMarkerStatus.Unknown, "Stored location identifier")
            {
                Confidence = record is null ? Confidence.Unknown : Confidence.Inferred,
                Description = $"{Clip(id)}. Stored in this save; discovery and map position are unverified. " +
                    (record is null ? "No matching PC catalog record." : record.AttributionText),
                Target = new(ViewTargetKind.Field, node.ValueId) { Path = ["tab:World", "section:" + Title, "section:" + Controls] },
                ExternalLink = new(Source, "Community world identifiers", "Ishiaki and contributors; MIT; pinned 84ffee67. Link only, no coordinates/art imported."),
            };
        }).ToArray();
        return new SectionNode(Title, [new MapNode("Stored location identifiers", new(0, 0, 1, 1), markers,
            "This list has no embedded coordinates or map art. Presence in the save does not prove discovery. " +
            (tokens.Length > shown.Length ? "Only the first 512 identifiers are shown; all others remain in the queryable/raw views." : "Every listed identifier links to its stored field."))
        {
            CoordinateSpace = "Unknown; no positions imported",
            ExternalSource = new(Source, "Community world reference", "Ishiaki and contributors; MIT; PC relationships and PS5 applicability unverified."),
        }, new SectionNode(Controls, shown.Select(view).ToArray()) { Collapsed = true }]);
    }

    private static bool IsLocation(string id) => id.StartsWith("SW.MinecartStation.", StringComparison.Ordinal) ||
        id.StartsWith("SW.Area.", StringComparison.Ordinal) || id.StartsWith("SW.Region.", StringComparison.Ordinal);
    private static string Clip(string text) => text.Length <= LabelLimit ? text : text[..LabelLimit] + "…";
}
