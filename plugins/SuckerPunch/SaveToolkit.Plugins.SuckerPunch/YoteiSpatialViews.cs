using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.SuckerPunch;

internal sealed class YoteiSpatialViews(IReferenceContext references,
    Func<string, string, ReferenceRecord?, string, FieldNode> reference)
{
    private const string MapAsset = "suckerpunch.yotei:maps/horizon-view.webp";
    private const int MapWidth = 1920;
    private const int MapHeight = 1080;

    public IReadOnlyList<ViewNode> Techniques()
    {
        var records = Records(YoteiViews.TechniquesCatalog);
        var items = records.Select(row => new GraphItem(row.Key, row.Name, GraphState.Unknown)
        {
            Branch = row.Category, Description = row.Description + " " + row.AttributionText +
                " Saved unlock state and prerequisite edges are unknown; this is an unjoined guide identity.",
            Target = new ViewTarget(ViewTargetKind.Field, "yotei/guide/techniques/" + row.Key) { Path = ["tab:Techniques"] },
        }).ToArray();
        return [new TextNode("Reference guide only. No technique hash, unlock flag, skill-point balance or prerequisite " +
            "has been joined to these saves. Statistics such as SkillShrineHonored are not technique ownership.") { Tone = Tone.Warning },
            new GraphNode("Technique reference (saved states Unknown)", items, [],
                $"{records.Count} technique guide entries grouped by branch; all saved states and prerequisites are Unknown. " +
                "No prerequisite edges are asserted. The table provides browse-only references and provenance."),
            Table(records, YoteiViews.TechniquesCatalog, "techniques")];
    }

    public IReadOnlyList<ViewNode> World()
    {
        var records = Records(YoteiViews.WorldCatalog);
        var provenance = references.AssetProvenance(MapAsset);
        var background = references.GetAsset(MapAsset) is not null && provenance is not null
            ? new MapBackground(MapAsset, "Horizon View Hot Spring map excerpt; the rest of Ezo is outside this image.",
                provenance.Attribution, EmbeddingPermitted: true) : null;
        var markers = records.Select(row => new MapMarker(row.Key, row.Name, MapMarkerStatus.Unknown, row.Category ?? "Hot spring")
        {
            // Original source screenshot is kept at its original size: this pixel is its visible Horizon View pin.
            // This is not a transform from save coordinates or a position for any other location.
            X = background is not null && row.Key == "horizon-view" ? 960 : null,
            Y = background is not null && row.Key == "horizon-view" ? 550 : null,
            Confidence = Confidence.Unknown,
            Description = row.Description + " " + row.AttributionText + " Saved discovery, completion and rewards are Unknown.",
            Target = new ViewTarget(ViewTargetKind.Row, row.Key) { Path = ["tab:World"] },
            ExternalLink = new ExternalViewLink(row.Source ?? row.Dataset.Attribution.SourceUrl, "Open location guide", row.AttributionText),
        }).ToArray();
        return [new TextNode("Hot spring guide, not a save completion tracker. This background is a local map excerpt. " +
            "Only Horizon View's visible screenshot pin has image-pixel placement; all other locations are unpositioned. " +
            "No save-to-world coordinate transform is known.") { Tone = Tone.Warning },
            new MapNode("Hot spring reference / Horizon View excerpt", new MapBounds(0, 0, MapWidth, MapHeight), markers,
                $"{records.Count} named hot spring references grouped by region, with Unknown saved state. " +
                "Only Horizon View can use the reference screenshot pin; other locations have no positions. " +
                "The full table is available without artwork.")
            {
                Background = background, CoordinateSpace = "Original reference screenshot pixels; no save-coordinate transform",
                CoordinateAttribution = background is not null ? provenance!.Attribution : null,
                CoordinateEmbeddingPermitted = background is not null,
                ExternalSource = new ExternalViewLink(YoteiViews.HotSpringUrl, "Open hot spring guide", "PowerPyx / Sony Interactive Entertainment / Sucker Punch Productions"),
            }, Table(records, YoteiViews.WorldCatalog, "world")];
    }

    private TableNode Table(IReadOnlyList<ReferenceRecord> records, string catalog, string category) => new(
        [new("icon", "Image"), new("name", "Browse reference"), new("category", "Category"),
            new("description", "Description"), new("state", "Saved state"), new("source", "Provenance")],
        records.Select(row => new TableRow(row.Key, new Dictionary<string, ViewNode>
        {
            ["icon"] = row.AssetId is { } asset && references.GetAsset(asset) is not null ? new ImageNode(asset, row.Name) : new TextNode("—"),
            ["name"] = reference(catalog, row.Key, row, $"yotei/guide/{category}/{row.Key}"),
            ["category"] = new TextNode(row.Category ?? "Unknown"), ["description"] = new TextNode(row.Description ?? ""),
            ["state"] = new TextNode("Unknown") { Tone = Tone.Warning }, ["source"] = new TextNode(row.AttributionText),
        })).ToArray());

    private IReadOnlyList<ReferenceRecord> Records(string catalog) => references.Catalog(catalog)?.Search(
        new ReferenceQuery { Limit = ReferenceQuery.MaxLimit, SortBy = ReferenceSortKey.Category }).Items ?? [];
}
