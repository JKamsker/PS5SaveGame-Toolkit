using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.FinalFantasy7;

/// <summary>Link-first regions; every marker targets a field in the same expanded section.</summary>
internal sealed class RegionalMaps(ProgressData progress)
{
    public const string TabTitle = "Regional maps";
    public const string ExternalMapUrl = "https://www.powerpyx.com/final-fantasy-7-vii-rebirth-full-world-map-revealed/";
    private readonly string[] _regions = progress.Flags.Where(flag => flag.Region != "General")
        .Select(flag => flag.Region).Distinct(StringComparer.Ordinal).ToArray();

    public IReadOnlyList<ViewNode> Sections() => _regions.Length == 0
        ? [new TextNode("Remake has no mapped world coordinates. Battle reports and dresses are available in Progress.")]
        : [new TextNode("Candidate bits are separate discovery, completion and repair states. Pins have no imported coordinates; open the attributed external map for geography. " + EmbeddedCatalogs.Attribution)
            { Tone = Tone.Info }, .. _regions.Select((region, index) => SectionNode.Lazy(region, $"spatial/map/{index}"))];

    public IReadOnlyList<ViewNode> Expand(int index)
    {
        if (index < 0 || index >= _regions.Length) throw new KeyNotFoundException("Unknown FFVII map region.");
        var region = _regions[index];
        var flags = progress.Flags.Where(flag => flag.Region == region).ToArray();
        var path = new[] { $"tab:{TabTitle}", $"section:spatial/map/{index}" };
        var markers = flags.Select(flag => new MapMarker(flag.Id, flag.Name,
            progress.Value(flag) == 1 ? MapMarkerStatus.Found : MapMarkerStatus.Missing, flag.Category)
        {
            Confidence = Confidence.Inferred,
            Description = $"Stored candidate bit: {progress.Value(flag)} at 0x{flag.Offset:X}, bit {flag.Bit}. " +
                $"Found/Missing represents this bit only, not a verified reward or complete objective. {flag.Source}; {EmbeddedCatalogs.Attribution}. {WriteWarnings.Progress}",
            Target = new(ViewTargetKind.Field, $"progress/{flag.Id}/bit") { Path = path },
        }).ToArray();
        var map = new MapNode(region + " candidates", new(0, 0, 1, 1), markers,
            $"{region}: {flags.Length} separately mapped candidate states. Found means stored bit 1; Missing means 0. " +
            "No in-game positions, artwork, fast-travel operation or aggregate completion claim. The marker list links to the exact stored-bit controls below.")
        {
            CoordinateSpace = "External map; coordinates not imported",
            ExternalSource = new(ExternalMapUrl, "PowerPyx: Rebirth regional maps",
                "PowerPyx, published 2024-02-29. External reference only; artwork and coordinates are not redistributed."),
        };
        return [map, new TableNode([new("name", "Objective"), new("domain", "Domain / state"), new("value", "Stored bit")],
            [.. flags.Select(flag => new TableRow($"map/{flag.Id}", new Dictionary<string, ViewNode>(StringComparer.Ordinal)
            {
                ["name"] = new TextNode(flag.Name), ["domain"] = new TextNode(flag.Category), ["value"] = progress.Field(flag),
            }))]) { Title = region + " stored states", Description = EmbeddedCatalogs.Attribution + " " + WriteWarnings.Progress }];
    }
}
