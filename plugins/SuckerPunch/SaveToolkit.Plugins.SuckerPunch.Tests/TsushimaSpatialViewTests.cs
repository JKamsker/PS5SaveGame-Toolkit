using SaveToolkit.Core.References;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Core.Storage;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Views;
using SaveToolkit.Testing;
using static SaveToolkit.Plugins.SuckerPunch.Tests.SuckerPunchTestData;

namespace SaveToolkit.Plugins.SuckerPunch.Tests;

public sealed class TsushimaSpatialViewTests
{
    [Fact]
    public void EquipmentCollectionIsServedThroughCoreSession()
    {
        var plugin = new SuckerPunchPlugin();
        var file = Assert.IsType<DiskSaveFile>(Slot("suckerpunchUser1"));
        var context = new ReferencePackStore([plugin]).Pin(plugin, TsushimaTitle);
        using var document = plugin.Open(file, context);
        using var session = new DocumentSession("equipment", file, plugin, document, references: context);
        _ = session.BuildView();

        var metadata = Assert.Single(session.QueryableCollections);
        var page = session.QueryCollection(metadata.Id,
            new QueryableCollectionQuery { Text = "Armor", Limit = 12 });

        Assert.Equal(metadata.Id, page.CollectionId);
        Assert.InRange(page.Rows.Count, 1, 12);
        Assert.All(page.Rows, row => Assert.Contains("Armor", row.Key, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void EquipmentIdentifiersUseBoundedSearchablePages()
    {
        var plugin = new SuckerPunchPlugin();
        using var document = plugin.Open(Slot("suckerpunchUser1"),
            new ReferencePackStore([plugin]).Pin(plugin, TsushimaTitle));
        var queryable = Assert.IsAssignableFrom<IQueryableDocument>(document);
        var metadata = Assert.Single(queryable.Collections);

        var page = queryable.Query(metadata.Id, new QueryableCollectionQuery { Limit = 7 });
        Assert.Equal(7, page.Rows.Count);
        Assert.Equal(metadata.TotalCount, page.TotalCount);
        Assert.Equal(metadata.TotalCount, page.FilteredCount);
        Assert.All(page.Rows, row => Assert.NotEmpty(row.Key));

        var filtered = queryable.Query(metadata.Id, new QueryableCollectionQuery { Text = "Armor", Limit = 200 });
        Assert.NotEmpty(filtered.Rows);
        Assert.True(filtered.FilteredCount < filtered.TotalCount);
        Assert.All(filtered.Rows, row => Assert.Contains("Armor", row.Key, StringComparison.OrdinalIgnoreCase));

        var categoryFiltered = queryable.Query(metadata.Id, new QueryableCollectionQuery
        {
            Filters = new Dictionary<string, string> { ["raw"] = "Armor" },
            SortColumn = "location",
            Descending = true,
            Limit = 12,
        });
        Assert.NotEmpty(categoryFiltered.Rows);
        Assert.All(categoryFiltered.Rows, row => Assert.Contains("Armor", row.Key, StringComparison.OrdinalIgnoreCase));
        var offsets = categoryFiltered.Rows.Select(row => Convert.ToInt32(
            Assert.IsType<TextNode>(row.Cells["location"]).Text[2..], 16)).ToArray();
        Assert.True(offsets.Zip(offsets.Skip(1)).All(pair => pair.First >= pair.Second));
    }

    [Fact]
    public void TechniquesPublishAnAttributedUnknownGraphWithFieldTargets()
    {
        var plugin = new SuckerPunchPlugin();
        using var document = plugin.Open(Slot("suckerpunchUser1"),
            new ReferencePackStore([plugin]).Pin(plugin, TsushimaTitle));
        var techniques = Assert.Single(Assert.IsType<TabsNode>(document.BuildView()).Tabs,
            tab => tab.Title == "Techniques");
        var graph = Assert.IsType<GraphNode>(techniques.Children.Single(node => node is GraphNode));

        SpatialViewValidation.Validate(graph);
        Assert.NotEmpty(graph.Items);
        Assert.Empty(graph.Edges);
        Assert.Contains("prerequisite", graph.TextAlternative, StringComparison.OrdinalIgnoreCase);
        Assert.All(graph.Items, item =>
        {
            Assert.Equal(GraphState.Unknown, item.State);
            Assert.NotNull(item.Target);
            Assert.Equal(ViewTargetKind.Field, item.Target!.Kind);
            Assert.Contains("Unknown", item.Description, StringComparison.Ordinal);
            Assert.Contains("CC-BY-SA-3.0", item.Description, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void WorldPublishesAttributedIslandArtworkWithUnpositionedUnknownCollectibles()
    {
        var plugin = new SuckerPunchPlugin();
        using var document = plugin.Open(Slot("suckerpunchUser1"),
            new ReferencePackStore([plugin]).Pin(plugin, TsushimaTitle));
        var world = Assert.Single(Assert.IsType<TabsNode>(document.BuildView()).Tabs,
            tab => tab.Title == "World");
        var map = Assert.IsType<MapNode>(world.Children.Single(node => node is MapNode));

        SpatialViewValidation.Validate(map);
        Assert.Equal(17, map.Markers.Count);
        var background = Assert.IsType<MapBackground>(map.Background);
        Assert.True(background.EmbeddingPermitted);
        Assert.Equal("suckerpunch.tsushima:maps/tsushima.webp", background.AssetId);
        Assert.Contains("owner decision 2026-10-29", background.Attribution.License);
        Assert.Equal(new MapBounds(0, 0, 1152, 2048), map.Bounds);
        Assert.False(map.CoordinateEmbeddingPermitted);
        Assert.NotNull(map.ExternalSource);
        Assert.All(map.Markers, marker =>
        {
            Assert.Equal(MapMarkerStatus.Unknown, marker.Status);
            Assert.Null(marker.X);
            Assert.Null(marker.Y);
            Assert.Equal(ViewTargetKind.Row, marker.Target!.Kind);
            Assert.Equal(["tab:World"], marker.Target.Path);
            Assert.Contains("Unknown", marker.Description, StringComparison.Ordinal);
            Assert.Contains("CC-BY-SA-3.0", marker.Description, StringComparison.Ordinal);
            Assert.NotNull(marker.ExternalLink);
        });
        Assert.Contains("Coordinates", map.TextAlternative, StringComparison.Ordinal);
        Assert.Contains("alignment is unverified", map.TextAlternative, StringComparison.Ordinal);
    }
}
