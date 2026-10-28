using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.TalesOfArise.Tests;

public sealed class AriseSpatialViewTests
{
    [Fact]
    public void SkillAndArteGraphsAreBoundedAttributedAndActionLinked()
    {
        var context = new SaveToolkit.Core.References.ReferencePackStore([new TalesOfArisePlugin()]).Pin(new TalesOfArisePlugin(), "PPSA02745");
        using var document = new TalesOfArisePlugin().Open(AriseSampleTests.GameplayFiles().First(), context);
        var tabs = Assert.IsType<TabsNode>(document.BuildView());
        var graphNodes = Walk(tabs).OfType<GraphNode>().ToArray();
        Assert.Equal(12, graphNodes.Length);
        Assert.All(graphNodes, graph =>
        {
            SpatialViewValidation.Validate(graph);
            Assert.InRange(graph.Items.Count, 1, SpatialViewLimits.MaxItems);
            Assert.NotEmpty(graph.TextAlternative);
            Assert.NotEmpty(graph.Edges);
            Assert.All(graph.Items, item => Assert.Equal(ViewTargetKind.Action, item.Target?.Kind));
        });
        Assert.Contains(graphNodes.SelectMany(graph => graph.Edges), edge => edge.From.StartsWith("title:", StringComparison.Ordinal));
    }

    [Fact]
    public void ProgressMapUsesOriginalSchematicCoordinatesAndExternalLinks()
    {
        var context = new SaveToolkit.Core.References.ReferencePackStore([new TalesOfArisePlugin()]).Pin(new TalesOfArisePlugin(), "PPSA02745");
        using var document = new TalesOfArisePlugin().Open(AriseSampleTests.GameplayFiles().First(), context);
        var maps = Walk(document.BuildView()).OfType<MapNode>().ToArray();
        var map = Assert.Single(maps);
        SpatialViewValidation.Validate(map);
        Assert.True(map.CoordinateEmbeddingPermitted);
        Assert.NotNull(map.CoordinateAttribution);
        Assert.Null(map.Background);
        Assert.True(map.Markers.Count > 100);
        Assert.All(map.Markers, marker =>
        {
            Assert.Equal(MapMarkerStatus.Unknown, marker.Status);
            Assert.Equal(Confidence.Unknown, marker.Confidence);
            Assert.NotNull(marker.ExternalLink);
            if (!marker.Id.StartsWith("domain:", StringComparison.Ordinal))
            {
                Assert.Equal(ViewTargetKind.Row, marker.Target?.Kind);
            }
        });
        Assert.Contains(map.Markers, marker => marker.Id == "domain:owls");
        Assert.Contains(map.Markers, marker => marker.Id == "domain:camps");
    }

    [Fact]
    public void InventoryF8RowsSupportSearchAndStableKeysWithoutAddingWritableQueryBindings()
    {
        var context = new SaveToolkit.Core.References.ReferencePackStore([new TalesOfArisePlugin()]).Pin(new TalesOfArisePlugin(), "PPSA02745");
        using var document = new TalesOfArisePlugin().Open(AriseSampleTests.GameplayFiles().First(file => file.FileName == "SaveData_002.sav"), context);
        var query = Assert.IsAssignableFrom<IQueryableDocument>(document);
        var page = query.Query("inventory", new QueryableCollectionQuery { Text = "Apple Gel" });
        Assert.Contains(page.Rows, candidate => candidate.Key == "inventory/24/quantity");
        var row = page.Rows.First(candidate => candidate.Key == "inventory/24/quantity");
        Assert.Equal("inventory/24/quantity", row.Key);
        Assert.DoesNotContain(row.Cells.Values, cell => cell is FieldNode field && !field.Spec.ReadOnly);
        Assert.Throws<ArgumentException>(() => query.Query("inventory", new QueryableCollectionQuery { SortColumn = "unknown" }));
    }

    private static IEnumerable<ViewNode> Walk(ViewNode node)
    {
        yield return node;
        switch (node)
        {
            case TabsNode tabs:
                foreach (var child in tabs.Tabs.SelectMany(tab => tab.Children).SelectMany(Walk)) yield return child;
                break;
            case SectionNode section when section.LazyId is null:
                foreach (var child in section.Children.SelectMany(Walk)) yield return child;
                break;
        }
    }
}
