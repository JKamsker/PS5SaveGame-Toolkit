using SaveToolkit.Core.References;
using SaveToolkit.Plugins.UnrealGvas;
using SaveToolkit.Sdk.Views;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.UnrealGvas.Tests;

public sealed class HogwartsSpatialViewTests
{
    [Fact]
    public void HogwartsSpatialViewsAreBoundedAttributedAndAcyclic()
    {
        var sample = SampleSaves.Load("PPSA01603").AllDiskFiles.First(file => file.Directory.Name == "HL0010");
        var plugin = new UnrealGvasPlugin();
        var references = new ReferencePackStore([plugin]).Pin(plugin, "PPSA01603");
        using var document = plugin.Open(sample, references);
        var tabs = Assert.IsType<TabsNode>(document.BuildView()).Tabs;
        var spatial = Assert.Single(tabs, tab => tab.Title == "Spatial");
        var map = Assert.Single(spatial.Children.OfType<MapNode>());
        var graph = Assert.Single(spatial.Children.OfType<GraphNode>());

        SpatialViewValidation.Validate(map);
        SpatialViewValidation.Validate(graph);
        Assert.Equal(26, map.Markers.Count);
        var background = Assert.IsType<MapBackground>(map.Background);
        Assert.Equal("hogwarts.legilimens:maps/highlands-schematic.webp", background.AssetId);
        Assert.True(background.EmbeddingPermitted);
        Assert.NotNull(document.GetAsset(background.AssetId));
        Assert.Contains(map.Markers, marker => marker.Category == "Fast travel" && marker.X is null);
        Assert.All(map.Markers.Where(marker => marker.X.HasValue), marker => Assert.Equal(Confidence.Inferred, marker.Confidence));
        Assert.Contains("catalogue grouping only", graph.TextAlternative, StringComparison.OrdinalIgnoreCase);
        Assert.All(graph.Items, item => Assert.Equal(GraphState.Unknown, item.State));
    }
}
