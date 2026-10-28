using System.Diagnostics;
using System.Text.Json;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Core.Storage;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.Cyberpunk2077.Tests;

public sealed class SpatialSampleTests(ITestOutputHelper output)
{
    [Fact]
    public void PublishedMapsAndPerksHaveWorkingReadOnlyTargetsBeforeAnyQueryRowsAreLoaded()
    {
        var file = ProgressionSampleTests.Files()[0];
        var (plugin, _, context) = ReferencePackFixture.Embedded();
        var timer = Stopwatch.StartNew();
        using var document = plugin.Open(file, context);
        using var session = new DocumentSession("spatial", (DiskSaveFile)file, plugin, document, references: context);
        var root = Assert.IsType<TabsNode>(session.BuildView());
        output.WriteLine($"Open + root: {timer.Elapsed.TotalMilliseconds:F1} ms");
        Assert.True(JsonSerializer.Serialize<ViewNode>(root).Length < 100_000);
        var allGraphItems = new List<GraphItem>();
        var allMarkers = new List<MapMarker>();
        foreach (var tab in root.Tabs.Where(tab => tab.Title is "World" or "Skills and perks"))
        {
            foreach (var shell in tab.Children.OfType<SectionNode>().Where(section => section.LazyId is not null))
            {
                var nodes = session.Expand(shell.LazyId!); // Core validates publication and registers the target fields.
                var fields = Assert.IsType<SectionNode>(nodes[1]).Children.Cast<FieldNode>().ToDictionary(field => field.ValueId);
                var targets = nodes[0] switch
                {
                    GraphNode graph => GraphTargets(graph),
                    MapNode map => MapTargets(map),
                    _ => throw new InvalidOperationException("Expected a spatial view."),
                };
                foreach (var target in targets)
                {
                    Assert.Equal(ViewTargetKind.Field, target.Kind);
                    Assert.Equal("tab:" + tab.Title, target.Path[0]);
                    Assert.Equal("section:" + shell.LazyId, target.Path[1]);
                    var field = fields[target.Id];
                    Assert.Equal("section:" + Assert.IsType<SectionNode>(nodes[1]).Title, target.Path[2]);
                    Assert.True(field.Spec.ReadOnly);
                    Assert.Equal(field.Value, document.GetValue(target.Id));
                    Assert.False(session.ApplyEdits([new(target.Id, JsonSerializer.SerializeToElement("changed"))]).Ok);
                }
            }
        }
        Assert.Equal(6, allGraphItems.Count); // The supplied early save stores groups but no allocated modern perks.
        Assert.All(allGraphItems, item => Assert.Equal(GraphState.Unknown, item.State));
        Assert.Equal(232, allMarkers.Count);
        Assert.Equal("travel/" + 0x0000002486F51E9CUL, Assert.Single(allMarkers, marker => marker.Status == MapMarkerStatus.Found).Id);
        Assert.DoesNotContain(allMarkers, marker => marker.Status == MapMarkerStatus.Missing);
        Assert.False(session.State.CanUndo);
        Assert.Empty(session.Serialize()); // Viewing has not dirtied the host session.
        Assert.Equal(file.ReadAllBytes(), Assert.Single(document.Serialize()).Content.ToArray());
        output.WriteLine($"Open + all spatial pages + preservation: {timer.Elapsed.TotalMilliseconds:F1} ms");

        IEnumerable<ViewTarget> GraphTargets(GraphNode graph)
        {
            SpatialViewValidation.Validate(graph);
            Assert.Empty(graph.Edges);
            allGraphItems.AddRange(graph.Items);
            return graph.Items.Select(item => Assert.IsType<ViewTarget>(item.Target));
        }
        IEnumerable<ViewTarget> MapTargets(MapNode map)
        {
            SpatialViewValidation.Validate(map);
            Assert.Null(map.Background);
            Assert.All(map.Markers, marker => { Assert.Null(marker.X); Assert.Null(marker.Y); });
            allMarkers.AddRange(map.Markers);
            return map.Markers.Select(marker => Assert.IsType<ViewTarget>(marker.Target));
        }
    }

    [Fact]
    public void NoPackStillMapsTheSavedUnknownTravelIdAndUnverifiedVersionsNeverClaimRegistration()
    {
        var file = ProgressionSampleTests.Files()[0];
        var plugin = new Cyberpunk2077Plugin();
        using (var document = plugin.Open(file))
        {
            _ = document.BuildView();
            var map = Assert.IsType<MapNode>(document.ExpandNode("travel-map/0")[0]);
            var marker = Assert.Single(map.Markers);
            Assert.Equal("0x0000002486F51E9C", marker.Label);
            Assert.Equal(MapMarkerStatus.Found, marker.Status);
            Assert.Equal(Confidence.Unknown, marker.Confidence);
            Assert.Equal(file.ReadAllBytes(), Assert.Single(document.Serialize()).Content.ToArray());
        }
        var (packedPlugin, _, context) = ReferencePackFixture.Embedded();
        var bytes = file.ReadAllBytes();
        bytes[4] ^= 1;
        using var unsupported = packedPlugin.Open(SaveToolkit.Testing.MemorySaveSet.SingleFile("sav.dat", bytes, "PPSA04027"), context);
        var root = Assert.IsType<TabsNode>(unsupported.BuildView());
        Assert.Empty(root.Tabs.Single(tab => tab.Title == "Skills and perks").Children.OfType<SectionNode>());
        foreach (var shell in root.Tabs.Single(tab => tab.Title == "World").Children.OfType<SectionNode>())
        {
            var map = Assert.IsType<MapNode>(unsupported.ExpandNode(shell.LazyId!)[0]);
            Assert.All(map.Markers, marker => Assert.Equal(MapMarkerStatus.Unknown, marker.Status));
        }
        Assert.False(unsupported.CanEdit);
        Assert.Equal(bytes, Assert.Single(unsupported.Serialize()).Content.ToArray());
    }
}
