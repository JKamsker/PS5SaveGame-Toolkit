using System.Globalization;
using System.Text.Json;
using SaveToolkit.Core.References;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Core.Storage;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Views;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.Insomniac.Tests;

public sealed class CommunityMapTests
{
    private const int MarkerCount = 403;
    private const int RegionCount = 9;
    private const string License = "No licence published; reused with attribution per owner decision 2026-10-28";

    [Fact]
    public void EmbeddedMarkersRetainSnapshotIdentityAttribution()
    {
        var plugin = new InsomniacPlugin();
        var store = new ReferencePackStore([plugin]);
        Assert.Empty(store.LoadErrors);
        var catalog = store.Pin(plugin, "PPSA01474").Catalog(RiftGuideMaps.CatalogId)!;
        Assert.Equal(MarkerCount, catalog.Count);
        Assert.Equal(RegionCount, catalog.Categories.Count);
        var keys = new HashSet<string>(StringComparer.Ordinal);
        for (var offset = 0; offset < catalog.Count; offset += ReferenceQuery.MaxLimit)
        foreach (var record in catalog.Search(new() { Offset = offset, Limit = ReferenceQuery.MaxLimit }).Items)
        {
            Assert.True(keys.Add(record.Key));
            Assert.Equal(License, record.Dataset.Attribution.License);
            Assert.Equal($"{record.Attributes["mapId"]}/{record.Attributes["markerId"]}", record.Key);
            Assert.Matches("^[0-9a-f]{64}$", record.Attributes["snapshotSha256"]);
            Assert.Contains(record.Source!.Split('#')[0], plugin.Info.References);
            Assert.Null(record.AssetId);
            Assert.Null(record.Limit);
            Assert.DoesNotContain("<", record.Name + record.Description);
            Assert.InRange(Number(record, "x"), 0, Number(record, "width"));
            Assert.InRange(Number(record, "y"), 0, Number(record, "height"));
        }
        Assert.Equal(MarkerCount, keys.Count);
        Assert.Null(store.Pin(plugin, "PPSA00000").Catalog(RiftGuideMaps.CatalogId));
    }

    [Fact]
    public void PinnedSnapshotVectorsAndRegionalCountsMatchTheCommunityData()
    {
        var plugin = new InsomniacPlugin();
        var catalog = new ReferencePackStore([plugin]).Pin(plugin, "PPSA01474").Catalog(RiftGuideMaps.CatalogId)!;
        var expected = new Dictionary<string, int>
        {
            ["Corson V"] = 31, ["Sargasso"] = 93, ["Scarstu Debris Field"] = 20,
            ["Savali"] = 56, ["Blizar Prime"] = 78, ["Torren IV"] = 23,
            ["Cordelion"] = 41, ["Ardolis"] = 30, ["Viceron"] = 31,
        };
        foreach (var (region, count) in expected)
            Assert.Equal(count, catalog.Search(new() { Category = region }).Total);
        var vector = catalog.Find("120/26577")!;
        Assert.Equal("Raritanium", vector.Name);
        Assert.Equal("Raritanium", vector.Attributes["guideCategory"]);
        Assert.Equal(1885, Number(vector, "x"));
        Assert.Equal(1308, Number(vector, "y"));
        Assert.Equal("2", vector.Attributes["mapVersion"]);
        Assert.Equal("a5e263afe4653aed0af5b90c516a2654895eb911200d0aecb05baf639e2e5511", vector.Attributes["snapshotSha256"]);
    }

    [Fact]
    public void CoreExpandsAllNineMapsWithUnknownStatusAndPreservesEveryPrimarySample()
    {
        var plugin = new InsomniacPlugin();
        var context = new ReferencePackStore([plugin]).Pin(plugin, "PPSA01474");
        var catalog = context.Catalog(RiftGuideMaps.CatalogId)!;
        var files = SampleSaves.Load("PPSA01474").AllDiskFiles
            .Where(file => file.FileName == "i29-s.save" && !file.Directory.IsBackup).ToArray();
        Assert.Equal(36, files.Length);
        foreach (var file in files)
        {
            using var document = plugin.Open(file, context);
            using var session = new DocumentSession("community-map", Assert.IsType<DiskSaveFile>(file), plugin, document, references: context);
            var root = Assert.IsType<TabsNode>(session.BuildView());
            var collectibles = Assert.Single(root.Tabs, tab => tab.Title == "Collectibles");
            var section = Assert.Single(collectibles.Children.OfType<SectionNode>(), node => node.Title == RiftGuideMaps.SectionTitle);
            Assert.Equal(RegionCount, section.Children.Count);
            var total = 0;
            foreach (var region in section.Children.Cast<SectionNode>())
            {
                var map = Assert.IsType<MapNode>(Assert.Single(session.Expand(region.LazyId!)));
                SpatialViewValidation.Validate(map);
                Assert.NotNull(map.Background);
                Assert.True(map.Background.EmbeddingPermitted);
                Assert.Contains("2026-10-29", map.Background.Attribution.License);
                Assert.NotNull(context.GetAsset(map.Background.AssetId));
                Assert.True(map.CoordinateEmbeddingPermitted);
                Assert.Equal(License, map.CoordinateAttribution!.License);
                Assert.Equal(new MapTransform(), map.Transform);
                Assert.Contains("unknown", map.TextAlternative);
                Assert.Equal(map.Markers.Select(marker => marker.Id),
                    Assert.IsType<MapNode>(Assert.Single(session.Expand(region.LazyId!))).Markers.Select(marker => marker.Id));
                foreach (var marker in map.Markers)
                {
                    var record = catalog.Find(marker.Id["guide/".Length..])!;
                    Assert.Equal(MapMarkerStatus.Unknown, marker.Status);
                    Assert.Equal(Confidence.Inferred, marker.Confidence);
                    Assert.Null(marker.Target);
                    Assert.Equal(Number(record, "x"), marker.X);
                    Assert.Equal(Number(record, "y"), marker.Y);
                    Assert.Equal(record.Name, marker.Label);
                    Assert.Equal(record.Attributes["guideCategory"], marker.Category);
                    Assert.Equal(record.Source, marker.ExternalLink!.Url);
                    Assert.Contains(record.AttributionText, marker.Description);
                    Assert.Contains("guide-to-save identity", marker.Description);
                }
                Assert.IsType<MapNode>(JsonSerializer.Deserialize<ViewNode>(JsonSerializer.Serialize<ViewNode>(map)));
                total += map.Markers.Count;
            }
            Assert.Equal(MarkerCount, total);
            Assert.Equal(file.ReadAllBytes(), Assert.Single(document.Serialize()).Content.ToArray());
        }
    }

    [Fact]
    public void MissingReferenceContextKeepsTheExistingStorageMapAndOmitsCommunityMaps()
    {
        using var document = new InsomniacPlugin().Open(PlayerCorpusTests.Find("i29slot0-s-autosave", 0));
        var tab = Assert.Single(Assert.IsType<TabsNode>(document.BuildView()).Tabs, item => item.Title == "Collectibles");
        Assert.DoesNotContain(tab.Children.OfType<SectionNode>(), section => section.Title == RiftGuideMaps.SectionTitle);
        Assert.Equal(64, Assert.Single(tab.Children.OfType<MapNode>()).Markers.Count);
    }

    private static double Number(ReferenceRecord record, string key) =>
        double.Parse(record.Attributes[key], CultureInfo.InvariantCulture);
}
