using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.SuckerPunch;

/// <summary>Read-only F9 presentations over evidence-qualified Tsushima reference data.</summary>
internal sealed class TsushimaSpatialViews(
    IReferenceContext references,
    IReadOnlyList<TsushimaIdentifier> identifiers)
{
    private const string ShrineMapUrl = "https://www.powerpyx.com/ghost-of-tsushima-wiki-strategy-guide/";

    public GraphNode TechniqueGraph()
    {
        var catalog = references.Catalog(TsushimaIds.Techniques);
        var items = identifiers
            .Where(identifier => identifier.Group == IdentifierGroup.Technique)
            .OrderBy(identifier => identifier.Offset)
            .Take(SpatialViewLimits.MaxItems)
            .Select(identifier => TechniqueItem(identifier, catalog, catalog?.Find(identifier.Name)))
            .ToArray();

        var labels = string.Join(", ", items.Select(item => item.Label));
        var alternative = BoundText(
            "Observed technique identifiers: " + (labels.Length == 0 ? "none" : labels) +
            ". Every state and prerequisite is Unknown; no prerequisite edge is asserted from identifier presence. " +
            "The table and attributed technique guide remain the authoritative text fallback.");

        return new GraphNode("Technique identifiers and prerequisites (read-only)", items, [], alternative);
    }

    public MapNode CollectibleMap()
    {
        var catalog = references.Catalog(TsushimaIds.WorldGuide);
        var records = catalog?.Search(new ReferenceQuery { Limit = ReferenceQuery.MaxLimit }).Items ?? [];
        var markers = records
            .Take(SpatialViewLimits.MaxItems)
            .Select(record => new MapMarker(
                record.Key,
                record.Name,
                MapMarkerStatus.Unknown,
                record.Category ?? "Unknown")
            {
                Description = BoundText(
                    $"{record.Description ?? "Reference entry."} Discovery, completion and reward state are Unknown. " +
                    record.AttributionText),
                Target = new ViewTarget(ViewTargetKind.Row, record.Key) { Path = ["tab:World"] },
                ExternalLink = new ExternalViewLink(
                    record.Source ?? record.Dataset.Attribution.SourceUrl,
                    $"Open {record.Name} reference",
                    record.Dataset.Attribution.ToShortText()),
                Confidence = Confidence.Unknown,
            })
            .ToArray();

        var labels = string.Join(", ", markers.Select(marker => marker.Label));
        var alternative = BoundText(
            "Schematic shrine and collectible reference list: " + (labels.Length == 0 ? "no pinned entries" : labels) +
            ". Coordinates and saved discovery, completion and reward state are not established. " +
            "Use the external guide for map imagery; no map art or community coordinates are embedded.");

        return new MapNode(
            "Tsushima collectibles (reference-only map)",
            new MapBounds(0, 0, 100, 100),
            markers,
            alternative)
        {
            CoordinateSpace = "Schematic; no save-coordinate transform is known",
            ExternalSource = new ExternalViewLink(
                ShrineMapUrl,
                "Open external Tsushima collectible guide",
                "PowerPyx; link only, no guide data or coordinates are embedded."),
        };
    }

    private static GraphItem TechniqueItem(TsushimaIdentifier identifier, IReferenceCatalog? catalog, ReferenceRecord? record)
    {
        var confidence = record?.Attributes.GetValueOrDefault("mappingConfidence") ?? "Unknown";
        var label = record is null || record.Name == identifier.Name ? identifier.Name : record.Name;
        var description = record is null
            ? "Observed identifier; no pinned friendly-name mapping."
            : record.Description ?? "Attributed technique reference.";
        var attribution = record?.AttributionText ?? catalog?.Dataset.Attribution.ToShortText() ??
            "No pinned reference catalog is available.";
        return new GraphItem($"technique:{identifier.Name}", label, GraphState.Unknown)
        {
            Branch = record?.Category ?? "Observed identifier",
            Description = BoundText(
                $"{description} Mapping confidence: {confidence}. State and prerequisites remain Unknown. " +
                attribution),
            Target = new ViewTarget(ViewTargetKind.Field, $"{TsushimaIds.Techniques}/{identifier.Name}/reference")
            {
                Path = ["tab:Techniques"],
            },
        };
    }

    private static string BoundText(string value)
    {
        const int maxLength = SpatialViewLimits.MaxTextLength;
        return value.Length <= maxLength ? value : value[..(maxLength - 1)] + '\u2026';
    }
}
