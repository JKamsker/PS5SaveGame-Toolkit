using System.Globalization;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.Cyberpunk2077;

/// <summary>Link-first map over the same attributed saved/catalog union used by the F8 table.</summary>
internal sealed class TravelMapInspection(IReadOnlyList<InspectionRow> rows)
{
    public const string Prefix = "travel-map/";
    public const string SourceUrl = "https://maps.piggyback.com/cyberpunk-2077/maps/night-city";
    public const string SourceAttribution = "Piggyback Interactive Limited / CD PROJEKT RED, official interactive map; " +
        "all rights reserved, consulted 2026-10-28. External link only; no map art, coordinates or location data imported. " +
        "The external map may describe another build; saved marker IDs have no verified external-location join.";
    private const string DetailsTitle = "Travel marker details";
    private const string Explanation = "Fast-travel registration list with an external Night City map. " +
        "Found means registered in saved FastTravelSystem only, not currently accessible or completed. " +
        "Unknown includes unregistered catalog entries and unavailable decoding; absence never means locked. " +
        "No coordinates, geographic placement, artwork or collectible completion have been inferred.";
    private readonly InspectionRow[] _rows = rows.OrderBy(row => row.Key, StringComparer.Ordinal).ToArray();

    public IReadOnlyList<ViewNode> Build() => [.. Enumerable.Range(0, (_rows.Length + SpatialDetails.PageSize - 1) / SpatialDetails.PageSize)
        .Select(page => SectionNode.Lazy($"Fast-travel map · page {page + 1}", Prefix + page.ToString(CultureInfo.InvariantCulture)))];

    public IReadOnlyList<ViewNode> Expand(string lazyId, Func<string, string, FieldSpec, string, FieldNode> readField)
    {
        if (!lazyId.StartsWith(Prefix, StringComparison.Ordinal) || !int.TryParse(lazyId.AsSpan(Prefix.Length),
                NumberStyles.None, CultureInfo.InvariantCulture, out var page) || page < 0 ||
            page >= (_rows.Length + SpatialDetails.PageSize - 1) / SpatialDetails.PageSize)
            throw new KeyNotFoundException($"Unknown travel map page '{lazyId}'.");
        var selected = _rows.Skip(page * SpatialDetails.PageSize).Take(SpatialDetails.PageSize).ToArray();
        var map = new MapNode("Fast-travel registration", new MapBounds(0, 0, 1, 1), selected.Select(row =>
            new MapMarker(row.Key, row.Values["name"], row.Values["state"] == WorldInspection.RegisteredTravelState
                ? MapMarkerStatus.Found : MapMarkerStatus.Unknown, "Fast travel")
            {
                Description = SpatialDetails.Summary(row.Values["id"] + " · " + row.Values["state"] + "\n" +
                    row.Values["details"] + "\n" + row.Attribution),
                Confidence = row.Confidence,
                Target = new ViewTarget(ViewTargetKind.Field, "map/" + row.Key)
                    { Path = ["tab:World", "section:" + lazyId, "section:" + DetailsTitle] },
            }).ToArray(), Explanation)
        {
            CoordinateSpace = "External-only; positions not imported",
            ExternalSource = new ExternalViewLink(SourceUrl, "Open official Night City map", SourceAttribution),
        };
        SpatialViewValidation.Validate(map);
        return [map, new SectionNode(DetailsTitle, selected.Select(row =>
            (ViewNode)SpatialDetails.Field("map/" + row.Key, row, readField)).ToArray()) { Collapsed = true }];
    }
}
