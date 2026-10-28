using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.UnrealGvas.Hogwarts;

/// <summary>
/// Link-first spatial projections for the pinned collectible catalogue. These are navigation aids,
/// not claims about saved completion, game coordinates, or prerequisite semantics.
/// </summary>
internal sealed class HogwartsSpatialViews
{
    private const string GuideUrl = "https://github.com/Malin001/Legilimens-Hogwarts-Legacy-Collectible-Finder/tree/02525dde0fd9bc0be72982478680377dccb41246";
    private const string ExternalMapUrl = "https://mapgenie.io/hogwarts-legacy/maps/hogwarts";
    private const string Attribution = "Legilimens contributors, MIT; link only for external guide/map navigation. No map art, guide prose, or coordinates are embedded.";
    private static readonly ReferenceAttribution SchematicAttribution = new(
        "https://github.com/JKamsker/PS5SaveGame-Toolkit",
        "PS5 Save Game Toolkit contributors",
        "GPL-3.0-or-later",
        "https://www.gnu.org/licenses/gpl-3.0.html",
        new DateOnly(2026, 10, 28),
        "Original schematic positions group attributed catalogue regions; no game coordinates or map art were copied.");
    private readonly IReadOnlyList<ReferenceRecord> _records;

    public HogwartsSpatialViews(IReferenceCatalog? catalog)
    {
        _records = catalog is null ? [] : ReadAll(catalog).ToArray();
    }

    public IReadOnlyList<ViewNode> Build()
    {
        if (_records.Count == 0)
            return [new TextNode("The attributed collectible catalogue is unavailable; no map or graph is inferred from raw IDs.") { Tone = Tone.Warning }];

        return [
            new TextNode("Spatial views are an attributed navigation aid. Marker state, physical coordinates, saved completion, fast-travel unlocks and prerequisite effects are Unknown. Open the external map or query the exact saved IDs in Collections.") { Tone = Tone.Warning },
            BuildMap(),
            BuildCatalogueGraph(),
        ];
    }

    private MapNode BuildMap()
    {
        var regions = _records.GroupBy(Region, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .ToArray();
        var markers = regions.Select((group, index) =>
        {
            var x = 8 + index % 6 * 16;
            var y = 8 + index / 6 * 20;
            var categories = string.Join(", ", group.Select(record => record.Category ?? "Uncategorised")
                .Distinct(StringComparer.Ordinal).OrderBy(value => value, StringComparer.Ordinal).Take(8));
            var source = group.First().AttributionText;
            return new MapMarker("region:" + Slug(group.Key), group.Key, MapMarkerStatus.Unknown, "Collectible region")
            {
                X = x,
                Y = y,
                Confidence = Confidence.Inferred,
                Description = $"{group.Count()} catalogue records; categories: {categories}. Saved state and physical coordinates are Unknown. {source}",
                ExternalLink = new ExternalViewLink(GuideUrl, "Open attributed collectible guide", Attribution),
            };
        }).ToList();
        markers.Add(new MapMarker("domain:fast-travel", "Fast travel / Floo Flames", MapMarkerStatus.Unknown, "Fast travel")
        {
            Confidence = Confidence.Unknown,
            Description = "No PS5 fast-travel identity or unlock join is established. Use the external map for navigation only.",
            ExternalLink = new ExternalViewLink(ExternalMapUrl, "Open external Hogwarts map", "MapGenie contributors; link only, no artwork or coordinates imported; reuse licence unconfirmed."),
        });
        return new MapNode("Hogwarts collectible and location schematic", new(0, 0, 100, 100), markers,
            $"{regions.Length} attributed regions and one unjoined fast-travel domain. All marker states are Unknown; positions are plugin-authored schematic coordinates.")
        {
            CoordinateSpace = "Plugin-authored schematic grid; region grouping only",
            CoordinateAttribution = SchematicAttribution,
            CoordinateEmbeddingPermitted = true,
            ExternalSource = new ExternalViewLink(ExternalMapUrl, "Open external Hogwarts map", "MapGenie contributors; link only, no artwork or coordinates imported; reuse licence unconfirmed."),
        };
    }

    private GraphNode BuildCatalogueGraph()
    {
        var regions = _records.Select(Region).Distinct(StringComparer.Ordinal).OrderBy(value => value, StringComparer.Ordinal).ToArray();
        var categories = _records.Select(record => record.Category ?? "Uncategorised")
            .Distinct(StringComparer.Ordinal).OrderBy(value => value, StringComparer.Ordinal).ToArray();
        var items = regions.Select(region => new GraphItem("region:" + Slug(region), region, GraphState.Unknown)
        {
            Branch = "Catalogue region",
            Description = $"Attributed catalogue region; saved completion and physical location are Unknown. {Attribution}",
        }).Concat(categories.Select(category => new GraphItem("category:" + Slug(category), category, GraphState.Unknown)
        {
            Branch = "Collectible category",
            Description = $"Attributed catalogue category; saved state and gameplay effect are Unknown. {Attribution}",
        })).ToArray();
        var edges = _records.Select(record => new GraphEdge("region:" + Slug(Region(record)), "category:" + Slug(record.Category ?? "Uncategorised")))
            .Distinct()
            .ToArray();
        return new GraphNode("Hogwarts collectible catalogue domains", items, edges,
            $"The graph groups {_records.Count} attributed collectible records into {regions.Length} regions and {categories.Length} categories. Edges are catalogue grouping only; no saved prerequisite or completion relationship is claimed.");
    }

    private static string Region(ReferenceRecord record) => record.Attributes.GetValueOrDefault("region") ?? "Unknown region";

    private static IEnumerable<ReferenceRecord> ReadAll(IReferenceCatalog catalog)
    {
        for (var offset = 0; offset < catalog.Count; offset += ReferenceQuery.MaxLimit)
            foreach (var record in catalog.Search(new ReferenceQuery { Offset = offset, Limit = ReferenceQuery.MaxLimit }).Items)
                yield return record;
    }

    private static string Slug(string value)
    {
        var chars = value.ToLowerInvariant().Select(character => char.IsAsciiLetterOrDigit(character) ? character : '-').ToArray();
        var result = new string(chars).Trim('-');
        while (result.Contains("--", StringComparison.Ordinal)) result = result.Replace("--", "-", StringComparison.Ordinal);
        return result.Length == 0 ? "unknown" : result;
    }
}
