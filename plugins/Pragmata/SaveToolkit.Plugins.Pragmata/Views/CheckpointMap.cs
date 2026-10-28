using SaveToolkit.Plugins.Pragmata.Format;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.Pragmata.Views;

internal sealed class CheckpointMap(PragmataDocument document, LazyPages pages)
{
    internal const string ExternalMapUrl = "https://itemaps.com/maps/pragmata";
    private const string SectionId = "spatial/checkpoints";
    private const string ValuesTitle = "Checkpoint values";

    public IReadOnlyList<ViewNode> Build()
    {
        var checkpoints = document.Layout.Sections.FirstOrDefault(node => HashNames.All.GetValueOrDefault(node.ClassHash) == "app.CheckPointManager.SaveData")?
            .Children.FirstOrDefault(node => HashNames.All.GetValueOrDefault(node.Hash) == "ReleasedCheckPointHashs");
        return checkpoints is null ? [] : [pages.Section(SectionId, "Checkpoint map and values", checkpoints.Children.Count,
            (start, count) => Page(checkpoints, start, count))];
    }

    private IReadOnlyList<ViewNode> Page(SaveNode checkpoints, int start, int count)
    {
        var values = new List<ViewNode>();
        var markers = new List<MapMarker>();
        foreach (var node in checkpoints.Children.Skip(start).Take(count))
        {
            // Unknown future element representations stay in the typed evidence view.
            if (node.Type != FieldTags.UInt32) continue;
            var field = document.References.Primitive(node);
            values.Add(field);
            var raw = document.Read(node).AsUInt32();
            markers.Add(new MapMarker($"checkpoint/record-at-{node.Offset:x}", $"Checkpoint hash 0x{raw:x8}", MapMarkerStatus.Unknown, "Stored checkpoint reference")
            {
                Confidence = Confidence.Unknown,
                Description = $"Stored in ReleasedCheckPointHashs. {document.References.IdentifierSource(checkpoints.Hash)} " +
                    $"Location, discovered/completed state and map linkage are Unknown. {field.Description}",
                Target = new ViewTarget(ViewTargetKind.Field, field.ValueId)
                {
                    Path = ["tab:World", .. LazyPages.NavigationPath(SectionId, checkpoints.Children.Count, start), $"section:{ValuesTitle}"],
                },
            });
        }
        return [new MapNode("Checkpoint references and external map", new MapBounds(0, 0, 1, 1), markers,
            "Stored checkpoint hashes are listed with Unknown state and without coordinates. " +
            "The external community map is a separate manual reference; no checkpoint-to-marker mapping is claimed.")
        {
            CoordinateSpace = "External reference only; save locations unconfirmed",
            ExternalSource = new ExternalViewLink(ExternalMapUrl, "Open community Pragmata map",
                "itemaps contributors. Licence unverified; link only. No map artwork, coordinates, descriptions or completion totals imported."),
        }, new SectionNode(ValuesTitle, values)];
    }
}
