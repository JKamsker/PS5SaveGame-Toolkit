using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.AstroPlayroom;

/// <summary>Original schematic positions, never game coordinates or prerequisite claims.</summary>
internal sealed class AstroSpatialViews(AstroSchema schema, IReadOnlyList<AstroField> fields,
    AstroReferences references, AstroLocations locations, Func<AstroField, FieldValue> current)
{
    public const string LazyPrefix = "astro-map:";
    private const int PageSize = 64;
    private const int Columns = 8;
    private const string GuideUrl = "https://www.powerpyx.com/astros-playroom-all-collectible-locations-artefacts-puzzle-pieces/";
    private const string OriginalSource = "https://github.com/JKamsker/PS5SaveGame-Toolkit/tree/main/plugins/AstroPlayroom";
    private static readonly ExternalViewLink Guide = new(GuideUrl, "Open collectible guide",
        "PowerPyx / Gage; Sony Interactive Entertainment game artwork. Artifact thumbnails and short game captions are attributed in Sources.");
    private readonly IReadOnlyDictionary<string, int> _groupCounts = schema.Records.GroupBy(record => record.Group)
        .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);

    public IReadOnlyList<ViewNode> Build() => [
        new TextNode("Original schematic and saved-record lists. Positions show list order, not geography. Found/Missing refer only to the stated saved flag; they do not prove gameplay completion or physical pickup locations. Oversized unknown paths remain in Raw instead of the bounded maps."),
        SectionNode.Lazy("World selection artwork (Inferred anchors)", LazyPrefix + "Artwork:0"),
        SectionNode.Lazy("Level schematic", LazyPrefix + "World:0"),
        SectionNode.Lazy("Artifact locations", LazyPrefix + "Artifact:0"),
        SectionNode.Lazy("Puzzle locations (unresolved)", LazyPrefix + "Puzzle:0"),
        SectionNode.Lazy("Prop locations (unresolved)", LazyPrefix + "Prop:0")
    ];

    public IReadOnlyList<ViewNode> Expand(string lazyId)
    {
        if (lazyId == LazyPrefix + "Artwork:0")
        {
            var artwork = AstroWorldMap.Create(LevelMarkers(), fields, references);
            return artwork is null
                ? [new TextNode("Reference artwork or matching schema unavailable; original schematic fallback."), .. Expand(LazyPrefix + "World:0")]
                : [new TextNode("Reference screenshot: its crowns and artwork are static. Only overlay markers reflect this save. Stage anchors are Inferred; use the original schematic or text list when uncertain."), artwork];
        }
        var parts = lazyId[LazyPrefix.Length..].Split(':');
        if (parts.Length != 2 || !int.TryParse(parts[1], out var page) || page < 0)
            throw new KeyNotFoundException("Unknown ASTRO map page.");

        var markers = parts[0] switch
        {
            "World" => LevelMarkers(),
            "Artifact" or "Puzzle" or "Prop" => CollectionMarkers(parts[0]),
            _ => throw new KeyNotFoundException("Unknown ASTRO map group.")
        };
        if (page > Math.Max(0, (markers.Count - 1) / PageSize))
            throw new KeyNotFoundException("Unknown ASTRO map page.");

        var schematic = parts[0] == "World";
        var map = new MapNode(schematic ? "Levels — original schematic" : $"{parts[0]} — saved collectible records",
            new MapBounds(0, 0, Columns, Columns), markers.Skip(page * PageSize).Take(PageSize).Select((marker, index) =>
                schematic ? marker with { X = index % Columns + 0.5, Y = index / Columns + 0.5 } : marker).ToArray(),
            schematic ? "Each point is an existing boolean world flag. Positions are original list-layout slots; no route, adjacency or prerequisite is asserted. Use the text list to open its exact progress control."
                : "Each entry is an existing saved collectible. Physical locations are unconfirmed, so no coordinates are supplied. Filter Missing to find unowned records; open the record for its exact key and ownership preview.")
        {
            ExternalSource = Guide,
            CoordinateSpace = "Original list-order schematic; not game geography",
            CoordinateEmbeddingPermitted = schematic,
            CoordinateAttribution = schematic ? new ReferenceAttribution(OriginalSource, "PS5 Save Game Toolkit contributors",
                "GPL-3.0-or-later (original layout)", "https://www.gnu.org/licenses/gpl-3.0.html", new DateOnly(2026, 10, 28),
                "Original eight-column list layout from saved flag order; no community coordinates or game artwork.") : null
        };
        SpatialViewValidation.Validate(map);
        return page * PageSize + PageSize < markers.Count
            ? [map, SectionNode.Lazy("More map records…", $"{LazyPrefix}{parts[0]}:{page + 1}")] : [map];
    }

    private IReadOnlyList<MapMarker> LevelMarkers() => fields
        .Where(field => field.Group == "World" && field.Spec is BoolSpec && field.Editable)
        .Where(field => field.Label is "IsCleared" or "IsVisited")
        .Select(field =>
        {
            var path = field.Path["PreInstall/".Length..];
            var parent = path[..path.LastIndexOf('/')];
            var reference = references.World(parent);
            return new MapMarker(field.Token.Id, $"{reference?.Name ?? parent}: {field.Label}",
                current(field).AsBool() ? MapMarkerStatus.Found : MapMarkerStatus.Missing, field.Label)
            {
                Confidence = reference is null ? Confidence.Unknown : Confidence.Inferred,
                Description = $"{path}. Status means this saved boolean only. {reference?.AttributionText}",
                Target = new ViewTarget(ViewTargetKind.Field, field.Token.Id) { Path = ["tab:Progress"] }
            };
        }).Where(IsPublishable).ToArray();

    private IReadOnlyList<MapMarker> CollectionMarkers(string group) => schema.Records.Where(record => record.Group == group)
        .Select(record =>
        {
            var reference = locations.Reference(record);
            var title = group switch { "Artifact" => "Artifacts", "Puzzle" => "Puzzle pieces", _ => "Props" };
            return new MapMarker(record.Element.Id, $"{reference?.Name ?? record.Element.Name} ({AstroLocations.Key(record)})",
                record.Ownership is not { } flag ? MapMarkerStatus.Unknown : current(flag).AsBool()
                    ? MapMarkerStatus.Found : MapMarkerStatus.Missing, locations.World(record)?.Name ?? reference?.Category ?? "Unmapped")
            {
                Confidence = reference is null ? Confidence.Unknown : Confidence.Inferred,
                Description = $"Saved IsGot only; no physical coordinates or pickup order established. {reference?.Description} {reference?.AttributionText}",
                ExternalLink = Guide,
                Target = CollectionTarget(record, title)
            };
        }).Where(IsPublishable).ToArray();

    private ViewTarget CollectionTarget(AstroCollectionRecord record, string title)
    {
        if (_groupCounts[record.Group] <= AstroCollectionQuery.EagerLimit)
            return new ViewTarget(ViewTargetKind.Row, record.Element.Id) { Path = ["tab:Collectibles", $"section:{title}"] };
        // F9 cannot search an unloaded F8 row. Navigate to its exact lazy Raw field instead.
        var flag = record.Ownership ?? record.Fields.FirstOrDefault();
        if (flag is null) return new ViewTarget(ViewTargetKind.Action, AstroPlanning.CollectAllAction) { Path = ["tab:Collectibles"] };
        var path = new List<string> { "tab:Raw" };
        var id = "";
        foreach (var part in flag.Element.Id.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            id += "/" + part;
            path.Add("section:" + id);
        }
        return new ViewTarget(ViewTargetKind.Field, flag.Token.Id) { Path = path };
    }

    // Very long unknown paths remain available in Raw; F9 has stricter publication limits.
    private static bool IsPublishable(MapMarker marker) => marker.Id.Length <= SpatialViewLimits.MaxIdLength &&
        marker.Label.Length <= SpatialViewLimits.MaxTextLength && marker.Description?.Length <= SpatialViewLimits.MaxTextLength &&
        marker.Category.Length <= SpatialViewLimits.MaxTextLength && marker.Target is { } target &&
        target.Id.Length <= SpatialViewLimits.MaxIdLength && target.Path.Count <= SpatialViewLimits.MaxPathParts &&
        target.Path.All(part => part.Length <= SpatialViewLimits.MaxTextLength);
}
