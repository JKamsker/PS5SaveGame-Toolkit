using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using SaveToolkit.Core.References;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Views;
using static SaveToolkit.Plugins.SuckerPunch.Tests.SuckerPunchTestData;

namespace SaveToolkit.Plugins.SuckerPunch.Tests;

public sealed class TsushimaAssetTests
{
    private const string ResourcePrefix = "refpacks/suckerpunch.tsushima/";
    private const string MapAsset = "suckerpunch.tsushima:maps/tsushima.webp";
    private const int ExpectedAssetBytes = 238737;

    [Fact]
    public void EveryIconPassesTheSharedLoaderWithASixtyFourPixelLimit()
    {
        var assembly = typeof(SuckerPunchPlugin).Assembly;
        var resources = assembly.GetManifestResourceNames()
            .ToDictionary(name => name.Replace('\\', '/'), StringComparer.Ordinal);
        using var stream = assembly.GetManifestResourceStream(resources[ResourcePrefix + "pack.json"])!;
        var manifest = JsonNode.Parse(stream)!.AsObject();
        manifest["id"] = "suckerpunch.icon-test";
        var datasets = manifest["datasets"]!.AsArray();
        foreach (var dataset in datasets.ToArray())
        {
            if (dataset!["kind"]!.GetValue<string>() != "assets" ||
                dataset["id"]!.GetValue<string>() == "suckerpunch_map_art")
            {
                datasets.Remove(dataset);
            }
        }

        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            using (var entry = archive.CreateEntry("pack.json").Open())
            using (var writer = new Utf8JsonWriter(entry)) manifest.WriteTo(writer);
            foreach (var file in datasets.SelectMany(dataset => dataset!["files"]!.AsArray()))
            {
                var path = file!["path"]!.GetValue<string>();
                using var source = assembly.GetManifestResourceStream(resources[ResourcePrefix + path])!;
                using var destination = archive.CreateEntry(path).Open();
                source.CopyTo(destination);
            }
        }

        var store = new ReferencePackStore([], new ReferencePackOptions
        {
            Limits = new ReferencePackLimits { MaxImageDimension = 64 },
        });
        var pack = store.Import(buffer.ToArray(), "icons.zip");
        Assert.Equal(106, pack.Info.Datasets.Sum(dataset => dataset.ItemCount));
    }

    [Fact]
    public void EmbeddedArtworkHasValidHashesTypesProvenanceAndNoDuplicatePayloads()
    {
        var plugin = new SuckerPunchPlugin();
        var store = new ReferencePackStore([plugin]);
        Assert.Empty(store.LoadErrors);
        var context = store.Pin(plugin, TsushimaTitle);
        var assembly = typeof(SuckerPunchPlugin).Assembly;
        var resourceName = assembly.GetManifestResourceNames()
            .Single(name => name.Replace('\\', '/') == ResourcePrefix + "pack.json");
        using var manifestStream = assembly.GetManifestResourceStream(resourceName)!;
        using var manifest = JsonDocument.Parse(manifestStream);
        var files = manifest.RootElement.GetProperty("datasets").EnumerateArray()
            .Where(dataset => dataset.GetProperty("kind").GetString() == "assets")
            .SelectMany(dataset => dataset.GetProperty("files").EnumerateArray()).ToArray();
        var hashes = new HashSet<string>(StringComparer.Ordinal);
        var totalBytes = 0;
        Assert.Equal(107, files.Length);
        foreach (var file in files)
        {
            var path = file.GetProperty("path").GetString()!;
            var id = "suckerpunch.tsushima:" + path;
            var asset = Assert.IsType<AssetContent>(context.GetAsset(id));
            var bytes = asset.Content.ToArray();
            var hash = Convert.ToHexStringLower(SHA256.HashData(bytes));
            Assert.Equal(file.GetProperty("sha256").GetString(), hash);
            Assert.True(hashes.Add(hash), "Identical imagery must share an asset file.");
            if (path.EndsWith(".webp", StringComparison.Ordinal))
            {
                Assert.Equal("image/webp", asset.ContentType);
                Assert.Equal("RIFF"u8.ToArray(), bytes[..4]);
                Assert.Equal("WEBP"u8.ToArray(), bytes[8..12]);
            }
            else
            {
                Assert.EndsWith(".png", path, StringComparison.Ordinal);
                Assert.Equal("image/png", asset.ContentType);
                Assert.Equal(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }, bytes[..8]);
            }

            var provenance = Assert.IsType<ReferenceDatasetInfo>(context.AssetProvenance(id));
            Assert.Contains("Sony Interactive Entertainment / Sucker Punch Productions", provenance.Attribution.Author);
            Assert.Contains("owner decision 2026-10-29", provenance.Attribution.License);
            Assert.Contains(provenance.Attribution.SourceUrl, plugin.Info.References);
            if (id == MapAsset) Assert.InRange(bytes.Length, 1, 600000);
            totalBytes += bytes.Length;
        }

        Assert.Equal(ExpectedAssetBytes, totalBytes);
        Assert.InRange(totalBytes, 1, 4000000);
        Assert.Null(context.GetAsset("suckerpunch.tsushima:missing.webp"));
        Assert.Null(store.Pin(plugin, "PPSA26344").GetAsset(MapAsset));
    }

    [Theory]
    [InlineData("suckerpunch_techniques", 4)]
    [InlineData("suckerpunch_technique_guide", 18)]
    [InlineData("suckerpunch_charm_guide", 79)]
    [InlineData("suckerpunch_armor_guide", 15)]
    [InlineData("suckerpunch_sword_guide", 41)]
    [InlineData("suckerpunch_equipment", 1)]
    public void EveryCatalogueIconResolvesAndSharesTheCachedAsset(string catalogId, int iconCount)
    {
        var plugin = new SuckerPunchPlugin();
        var store = new ReferencePackStore([plugin]);
        var first = store.Pin(plugin, TsushimaTitle);
        var second = store.Pin(plugin, TsushimaTitle);
        var records = first.Catalog(catalogId)!.Search(new ReferenceQuery { Limit = 200 }).Items;
        var illustrated = records.Where(record => record.AssetId is not null).ToArray();
        Assert.Equal(iconCount, illustrated.Length);
        foreach (var record in illustrated)
        {
            var asset = first.GetAsset(record.AssetId!);
            Assert.NotNull(asset);
            Assert.Same(asset, second.GetAsset(record.AssetId!));
            Assert.NotNull(first.AssetProvenance(record.AssetId!));
        }

        Assert.All(records.Where(record => record.Attributes["mappingConfidence"] == "Unknown" &&
            !record.Key.StartsWith("wiki:", StringComparison.Ordinal)), record => Assert.Null(record.AssetId));
    }

    [Theory]
    [InlineData("suckerpunch_charm_guide", "wiki:charm:charm-of-inari",
        "Increases supplies, predator hides, bamboo, and yew wood gained from collecting.")]
    [InlineData("suckerpunch_technique_guide", "wiki:technique:deflect-arrows", "Parry incoming projectiles while blocking.")]
    [InlineData("suckerpunch_sword_guide", "wiki:sword-kit:the-storm-of-clan-sakai",
        "The regalia of the Sakai clan, right hand of the jito.")]
    [InlineData("suckerpunch_armor_guide", "wiki:armor:broken-armor", "Samurai armor that has been damaged beyond repair.")]
    public void GameDescriptionsRetainAttributionAndUnknownGuideState(string catalogId, string key, string description)
    {
        var plugin = new SuckerPunchPlugin();
        var context = new ReferencePackStore([plugin]).Pin(plugin, TsushimaTitle);
        var record = context.Catalog(catalogId)!.Find(key)!;
        Assert.StartsWith(description, record.Description, StringComparison.Ordinal);
        Assert.Equal("Unknown", record.Attributes["mappingConfidence"]);
        Assert.Equal("CC-BY-SA-3.0", record.Dataset.Attribution.License);
        Assert.Contains("owner decision 2026-10-29", record.Dataset.Attribution.Modifications);
    }

    [Fact]
    public void GuideTechniqueAndEquipmentCellsPublishLocalImagesAndVisibleProvenance()
    {
        var plugin = new SuckerPunchPlugin();
        var context = new ReferencePackStore([plugin]).Pin(plugin, TsushimaTitle);
        using var document = plugin.Open(Slot("suckerpunchUser1"), context);
        var root = Assert.IsType<TabsNode>(document.BuildView());
        foreach (var catalog in new[] { "suckerpunch_charm_guide", "suckerpunch_armor_guide",
            "suckerpunch_sword_guide", "suckerpunch_technique_guide" })
        {
            var table = Assert.IsType<TableNode>(document.ExpandNode("guide/" + catalog)[1]);
            Assert.All(table.Rows, row =>
            {
                var image = Assert.IsType<ImageNode>(row.Cells["icon"]);
                Assert.NotNull(document.GetAsset(image.AssetId));
                Assert.Contains("owner decision 2026-10-29", Assert.IsType<TextNode>(row.Cells["source"]).Text);
                Assert.Contains("owner decision 2026-10-29", Assert.IsType<FieldNode>(row.Cells["name"]).Description);
            });
        }

        var techniques = root.Tabs.Single(tab => tab.Title == "Techniques").Children.OfType<TableNode>().Single();
        Assert.Equal(4, techniques.Rows.Count(row => row.Cells["icon"] is ImageNode));
        var equipment = Assert.IsAssignableFrom<IQueryableDocument>(document).Query("equipment/identifiers",
            new QueryableCollectionQuery { Text = "SwordKatanaSakaiStorm", Limit = 1 });
        Assert.IsType<ImageNode>(Assert.Single(equipment.Rows).Cells["icon"]);
        Assert.Equal(4, context.Datasets().Count(dataset => dataset.Used && dataset.Dataset.Kind == ReferenceDatasetKind.Assets));
        Assert.Equal(Slot("suckerpunchUser1").ReadAllBytes(), Assert.Single(document.Serialize()).Content.ToArray());
    }

    [Fact]
    public void MissingAssetsRetainCataloguesAndUnpositionedMapWithoutBrokenImages()
    {
        var plugin = new SuckerPunchPlugin();
        var pinned = new ReferencePackStore([plugin]).Pin(plugin, TsushimaTitle);
        using var document = plugin.Open(Slot("suckerpunchUser1"), new WithoutAssets(pinned));
        var root = Assert.IsType<TabsNode>(document.BuildView());
        var map = root.Tabs.Single(tab => tab.Title == "World").Children.OfType<MapNode>().Single();
        SpatialViewValidation.Validate(map);
        Assert.Null(map.Background);
        Assert.Equal(17, map.Markers.Count);
        Assert.All(map.Markers, marker => { Assert.Null(marker.X); Assert.Null(marker.Y); });
        Assert.Contains("schematic list", map.TextAlternative);
        var guide = Assert.IsType<TableNode>(document.ExpandNode("guide/suckerpunch_charm_guide")[1]);
        Assert.Equal(79, guide.Rows.Count);
        Assert.All(guide.Rows, row => Assert.IsType<TextNode>(row.Cells["icon"]));
    }

    private sealed class WithoutAssets(IReferenceContext inner) : IReferenceContext
    {
        public IReadOnlyList<ReferencePackInfo> Packs => inner.Packs;
        public IReferenceCatalog? Catalog(string catalogId, string? gameBuild = null) => inner.Catalog(catalogId, gameBuild);
        public AssetContent? GetAsset(string assetId) => null;
        public ReferenceDatasetInfo? AssetProvenance(string assetId) => null;
    }
}
