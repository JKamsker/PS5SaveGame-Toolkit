using System.Text.Json;
using SaveToolkit.Core.References;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Core.Storage;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.Insomniac.Tests;

public sealed class SpatialViewTests
{
    [Fact]
    public void MapsPreserveAllBitsWithoutInventingCoordinatesOrPickupStatusAcrossTheCorpus()
    {
        var plugin = new InsomniacPlugin();
        var context = new ReferencePackStore([plugin]).Pin(plugin, "PPSA01474");
        var files = SampleSaves.Load("PPSA01474").AllDiskFiles.Where(f => f.FileName == "i29-s.save" && !f.Directory.IsBackup).ToArray();
        Assert.Equal(36, files.Length);
        foreach (var file in files)
        {
            using var document = plugin.Open(file, context);
            var collectibles = Assert.Single(Assert.IsType<TabsNode>(document.BuildView()).Tabs, t => t.Title == "Collectibles");
            var map = Assert.Single(collectibles.Children.OfType<MapNode>());
            SpatialViewValidation.Validate(map);
            Assert.Equal(64, map.Markers.Count);
            Assert.Null(map.Background);
            Assert.Equal(RiftCollectibleMap.GuideUrl, map.ExternalSource!.Url);
            Assert.Contains("Link only", map.ExternalSource.Attribution);
            var bits = PlayerCorpusTests.Fields(file, RiftPlayerView.CampaignTag)["RYNOPlansCollected"].Values[0].AsUInt64();
            var values = Assert.Single(collectibles.Children.OfType<SectionNode>(), s => s.Title == "Map storage values");
            foreach (var (marker, bit) in map.Markers.Select((marker, bit) => (marker, bit)))
            {
                Assert.Equal(MapMarkerStatus.Unknown, marker.Status);
                Assert.Null(marker.X);
                Assert.Null(marker.Y);
                Assert.NotNull(marker.Target);
                var field = Assert.Single(values.Children.OfType<FieldNode>(), f => f.ValueId == marker.Target.Id);
                Assert.Equal((bits & (1UL << bit)) != 0, field.Value.AsBool());
                Assert.Equal(field.Value, document.GetValue(field.ValueId));
                Assert.Equal(["tab:Collectibles", "section:Map storage values"], marker.Target.Path);
                Assert.True(field.Spec.ReadOnly);
            }
            Assert.Equal(file.ReadAllBytes(), Assert.Single(document.Serialize()).Content.ToArray());
        }
    }

    [Fact]
    public void CoreExpandsQueryableGraphsAndEveryTargetResolvesToItsExactStoredMask()
    {
        var plugin = new InsomniacPlugin();
        var context = new ReferencePackStore([plugin]).Pin(plugin, "PPSA01474");
        var file = Assert.IsType<DiskSaveFile>(PlayerCorpusTests.Find("i29slot0-s-autosave", 0));
        using var document = plugin.Open(file, context);
        using var session = new DocumentSession("spatial", file, plugin, document, references: context);
        _ = session.BuildView();
        var blocks = InventoryReader.Read(Dat1Container.Parse(file.ReadAllBytes()));
        var count = 0;
        for (var offset = 0; ; offset += 7)
        {
            var page = session.QueryCollection("arsenal/graphs", new() { Offset = offset, Limit = 7 });
            if (page.Rows.Count == 0) break;
            foreach (var row in page.Rows)
            {
                var lazy = Assert.IsType<SectionNode>(row.Cells["diagram"]);
                var children = session.Expand(lazy.LazyId!);
                var graph = Assert.Single(children.OfType<GraphNode>());
                SpatialViewValidation.Validate(graph);
                Assert.Equal(16, graph.Items.Count);
                Assert.Empty(graph.Edges);
                var values = Assert.Single(children.OfType<SectionNode>()).Children.OfType<FieldNode>().ToArray();
                var record = blocks.SelectMany(block => block.Records.Select(record => (block, record)))
                    .Single(pair => $"upgrades/{pair.block.Key}/{pair.record.Key}" == row.Key).record;
                Assert.All(graph.Items, item =>
                {
                    Assert.Equal(GraphState.Unknown, item.State);
                    Assert.Null(item.Cost);
                    Assert.NotNull(item.Target);
                    Assert.Contains($"row:{row.Key}", item.Target.Path);
                    Assert.Contains($"section:{lazy.LazyId}", item.Target.Path);
                    Assert.Equal(document.GetValue(item.Target.Id), Assert.Single(values, v => v.ValueId == item.Target.Id).Value);
                });
                Assert.Equal(record.Upgrades!.Grid.Select(v => (ulong)v), values.Select(v => v.Value.AsUInt64()));
                Assert.IsType<GraphNode>(JsonSerializer.Deserialize<ViewNode>(JsonSerializer.Serialize<ViewNode>(graph)));
                count++;
            }
        }
        Assert.True(count > 10);
        Assert.Equal(file.ReadAllBytes(), Assert.Single(document.Serialize()).Content.ToArray());
    }

    [Fact]
    public void MissingCatalogsKeepMapLabelsUnknownAndHighBitsVisible()
    {
        using var document = new InsomniacPlugin().Open(PlayerCorpusTests.Find("i29slot0-s-autosave", 0));
        var map = Assert.Single(Assert.IsType<TabsNode>(document.BuildView()).Tabs.SelectMany(t => t.Children).OfType<MapNode>());
        Assert.All(map.Markers, marker => Assert.Equal(Confidence.Unknown, marker.Confidence));
        Assert.Contains(map.Markers, marker => marker.Id == "spatial/spybot/63");
    }

    [Fact]
    public void InventoryPagesExposeCatalogIdsAndLazyDetailsWithoutRequiringWholeContainers()
    {
        var plugin = new InsomniacPlugin();
        var context = new ReferencePackStore([plugin]).Pin(plugin, "PPSA01474");
        var file = Assert.IsType<DiskSaveFile>(PlayerCorpusTests.Find("i29slot0-s-autosave", 0));
        using var document = plugin.Open(file, context);
        using var session = new DocumentSession("inventory-paging", file, plugin, document, references: context);
        _ = session.BuildView();
        var inventory = session.QueryableCollections.Where(c => c.Id.StartsWith("inventory/", StringComparison.Ordinal)).ToArray();
        Assert.NotEmpty(inventory);
        foreach (var collection in inventory)
        {
            var page = session.QueryCollection(collection.Id, new() { Limit = 3 });
            Assert.InRange(page.Rows.Count, 1, 3);
            foreach (var row in page.Rows)
            {
                var type = Assert.IsType<FieldNode>(row.Cells["type"]);
                Assert.Equal("rift-item-types", Assert.IsType<ReferenceSpec>(type.Spec).CatalogId);
                Assert.NotNull(context.Catalog("rift-item-types")!.Find(type.Value.AsString()));
                Assert.True(type.Spec.ReadOnly);
                Assert.NotEmpty(session.Expand(Assert.IsType<SectionNode>(row.Cells["state"]).LazyId!));
            }
        }
        var config = Assert.IsType<FieldNode>(Assert.Single(session.QueryCollection("arsenal/weapons", new()
        {
            Text = "Burst Pistol", Filters = new Dictionary<string, string> { ["container"] = "62d731c8/0" },
        }).Rows).Cells["config"]);
        Assert.Equal("rift-weapons", Assert.IsType<ReferenceSpec>(config.Spec).CatalogId);
        Assert.NotNull(context.Catalog("rift-weapons")!.Find(config.Value.AsString()));
    }
}
