using System.Buffers.Binary;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.FinalFantasy16;

internal static class FaithLocationView
{
    public const string GuideUrl = "https://finalfantasy.fandom.com/wiki/Final_Fantasy_XVI_locations";

    public static MapNode? Build(byte[] bytes, FaithContainer container, FaithCatalog catalog)
    {
        var block = FaithSchema.Unique(container, FaithSchema.PartyType, FaithSchema.SupportsParty);
        if (block is null) return null;
        var id = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(block.PayloadOffset + FaithContextLayout.MapOffset));
        var record = catalog.Find(FaithCatalog.MapsId, id);
        var map = new MapNode("Location — candidate map ID", new MapBounds(0, 0, 1, 1),
            [new MapMarker("party-map", record is null ? $"Unknown map {id}" : $"{FaithCatalog.PlainText(record.Name)} (ID {id})",
                MapMarkerStatus.Unknown, "Stored location")
            {
                Confidence = record is null ? Confidence.Unknown : Confidence.Inferred,
                Description = "Candidate PartyData map ID only. No visited/completed state, position, obelisk or collectible is inferred. " +
                    "Map subrows and sky variants are not unique coordinates. " +
                    (record is null ? "No attributed label is available; exact raw ID retained." : FaithCatalog.Describe(record)),
                Target = new ViewTarget(ViewTargetKind.Field, "party/map")
                {
                    Path = ["tab:Character context", "section:Location / party — Inferred"],
                },
            }],
            "One stored candidate location; its status is Unknown. Open the map-ID control for the exact value and provenance, " +
            "or the external location guide. No art, coordinates, transforms, completion totals or teleportation are supplied.")
        {
            CoordinateSpace = "External guide only; save-to-map transform Unknown",
            ExternalSource = new ExternalViewLink(GuideUrl, "Final Fantasy Wiki location guide",
                "Final Fantasy Wiki contributors. External link only; no artwork or coordinates imported."),
        };
        SpatialViewValidation.Validate(map);
        return map;
    }
}
