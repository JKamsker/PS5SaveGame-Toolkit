using System.Text.Json;
using SaveToolkit.Core.Editing;
using SaveToolkit.Core.Tests.Fakes;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Views;
using SaveToolkit.Testing;

namespace SaveToolkit.Core.Tests.Spatial;

public sealed class SpatialViewTests
{
    private static GraphNode Graph => new("Skills", [new GraphItem("a", "Dash", GraphState.Available),
        new GraphItem("b", "Glide", GraphState.Locked)], [new GraphEdge("a", "b")], "Dash unlocks Glide.");
    private static MapNode Map => new("Region", new MapBounds(0, 0, 100, 100),
        [new MapMarker("a", "Cache", MapMarkerStatus.Unknown, "Loot")], "Cache location is unknown.");

    [Fact]
    public void SyntheticPlugin_RoundTripsBothNewVariants_AndIndexesOnlyRealControls()
    {
        var plugin = new SpatialPlugin();
        var file = MemorySaveSet.SingleFile("spatial.dat", SpatialPlugin.Create());
        using var doc = plugin.Open(file);
        var view = doc.BuildView();
        var json = JsonSerializer.Serialize(view, ToolkitJson.Options);
        var decoded = JsonSerializer.Deserialize<ViewNode>(json, ToolkitJson.Options)!;
        Assert.Equal(json, JsonSerializer.Serialize(decoded, ToolkitJson.Options));
        var root = Assert.IsType<SectionNode>(decoded);
        var graph = Assert.IsType<GraphNode>(root.Children[0]);
        Assert.Equal("skill/dash", graph.Items[1].Target!.Id);
        Assert.Equal(GraphState.Available, graph.Items[1].State);
        var map = Assert.IsType<MapNode>(root.Children[1]);
        Assert.Equal(-1, map.Transform.ScaleY);
        Assert.NotNull(map.CoordinateAttribution);
        var external = Assert.IsType<MapNode>(root.Children[2]);
        Assert.Null(external.Background);
        Assert.All(external.Markers, marker => { Assert.Null(marker.X); Assert.Null(marker.Y); });
        var index = new ViewIndex();
        index.Add(decoded);
        Assert.NotNull(index.Spec("skill/dash"));
        Assert.NotNull(index.Action("reset"));
        Assert.False(index.KnowsBinding("dash"));
        Assert.Equal(SpatialPlugin.Create(), doc.Serialize()[0].Content.ToArray());
    }

    [Fact]
    public void Bounds_AcceptExactItemLimits_RejectOversizedNodesAndEdges()
    {
        var graph = Graph with { Items = Enumerable.Range(0, SpatialViewLimits.MaxItems)
            .Select(i => new GraphItem(i.ToString(System.Globalization.CultureInfo.InvariantCulture), "Skill", GraphState.Unknown)).ToArray(), Edges = [] };
        SpatialViewValidation.Validate(graph);
        Reject(graph with { Items = [.. graph.Items, new GraphItem("extra", "Extra", GraphState.Unknown)] });
        Reject(Graph with { Edges = Enumerable.Repeat(new GraphEdge("a", "b"), SpatialViewLimits.MaxEdges + 1).ToArray() });
        var markers = Enumerable.Range(0, SpatialViewLimits.MaxItems).Select(i => new MapMarker(
            i.ToString(System.Globalization.CultureInfo.InvariantCulture), "Location", MapMarkerStatus.Unknown, "Other")).ToArray();
        SpatialViewValidation.Validate(Map with { Markers = markers });
        Reject(Map with { Markers = [.. markers, new MapMarker("extra", "Extra", MapMarkerStatus.Unknown, "Other")] });
    }

    [Fact]
    public void InvalidGraphs_AreRejectedBeforePublication()
    {
        Reject(Graph with { Items = [Graph.Items[0], Graph.Items[0]] });
        Reject(Graph with { Edges = [new GraphEdge("a", "absent")] });
        Reject(Graph with { Edges = [new GraphEdge("a", "b"), new GraphEdge("b", "a")] });
        Reject(Graph with { Edges = [new GraphEdge("a", "a")] });
        Reject(Graph with { Edges = [new GraphEdge("a", "b"), new GraphEdge("a", "b")] });
        Reject(Graph with { TextAlternative = " " });
        Reject(Graph with { TextAlternative = new string('a', SpatialViewLimits.MaxTextLength + 1) });
        Reject(Graph with { Items = [Graph.Items[0] with { Target = new ViewTarget(ViewTargetKind.Field, "a") { Path = ["invalid"] } }] });
        Reject(Graph with { Items = [Graph.Items[0] with { State = (GraphState)999 }] });
    }

    [Theory]
    [InlineData("http://example.org/map")]
    [InlineData("javascript:alert(1)")]
    [InlineData("data:image/png;base64,AAAA")]
    [InlineData("//example.org/map")]
    [InlineData("https://user:password@example.org/map")]
    [InlineData("https://example.org\\@evil.example/map")]
    [InlineData("https://example.org/\nmap")]
    public void UnsafeLinks_AreRejectedEverywhere(string url)
    {
        var link = new ExternalViewLink(url, "Guide", "Example authors, unverified licence; link only.");
        Reject(Map with { ExternalSource = link });
        Reject(Map with { Markers = [Map.Markers[0] with { ExternalLink = link }] });
    }

    [Fact]
    public void HttpsLinks_NeedAttribution_AndNeverBecomeAssetsOrBindings()
    {
        var link = new ExternalViewLink("https://example.org/map?region=1#cache", "Guide", "Example authors, link only.");
        var index = new ViewIndex();
        index.Add(Map with { ExternalSource = link });
        Assert.False(index.KnowsAsset(link.Url));
        Assert.Empty(index.BindingIds);
        Reject(Map with { ExternalSource = link with { Attribution = "" } });
    }

    [Fact]
    public void EmbeddedMaps_NeedLocalAssets_ProvenanceAndExplicitPermissions()
    {
        var background = new MapBackground(SpatialPlugin.AssetId, "Original schematic", SpatialPlugin.OriginalAttribution, true);
        var marker = Map.Markers[0] with { X = 30, Y = 40 };
        var map = Map with { Background = background, Markers = [marker], CoordinateAttribution = SpatialPlugin.OriginalAttribution,
            CoordinateEmbeddingPermitted = true, Transform = new MapTransform(2, -2, 0, 100) };
        SpatialViewValidation.Validate(map);
        Assert.False(new ViewIndex().KnowsAsset(background.AssetId)); // F2 endpoint, not plugin GetAsset.
        Reject(map with { Background = background with { EmbeddingPermitted = false } });
        Reject(map with { Background = background with { AssetId = "https://example.org/image.png" } });
        Reject(map with { CoordinateEmbeddingPermitted = false });
        Reject(map with { CoordinateAttribution = null });
        Reject(map with { Background = background with { Attribution = background.Attribution with { Author = "" } } });
        Reject(map with { Markers = [marker with { X = 60 }] }); // Transformed outside bounds.
        Reject(map with { Markers = [marker with { Y = null }] });
        Reject(map with { Markers = [marker with { X = double.NaN }] });
        Reject(map with { Bounds = new MapBounds(0, 0, 0, 100) });
        Reject(map with { Transform = new MapTransform(ScaleX: 0) });
        Reject(map with { Markers = [marker, marker] });
        Reject(map with { TextAlternative = "" });
    }

    private static void Reject(ViewNode node) => Assert.Throws<InvalidOperationException>(() => new ViewIndex().Add(node));
}
