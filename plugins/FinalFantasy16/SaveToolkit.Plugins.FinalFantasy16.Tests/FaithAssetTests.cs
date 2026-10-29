using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using SaveToolkit.Core.References;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.FinalFantasy16.Tests;

public sealed class FaithAssetTests
{
    private const int NewAssetBytes = 398116;
    private const int AssetBudget = 4_000_000;
    private readonly FinalFantasy16Plugin _plugin = new();

    [Fact]
    public void EveryEmbeddedHashAndImageTypeResolvesWithinThePluginBudget()
    {
        var store = new ReferencePackStore([_plugin]);
        Assert.Empty(store.LoadErrors);
        var references = store.Pin(_plugin, "PPSA10665");
        var assembly = typeof(FinalFantasy16Plugin).Assembly;
        var hashes = new HashSet<string>(StringComparer.Ordinal);
        var total = 0;
        foreach (var resource in assembly.GetManifestResourceNames().Where(name => name.EndsWith("/pack.json", StringComparison.Ordinal)))
        {
            using var stream = assembly.GetManifestResourceStream(resource)!;
            using var manifest = JsonDocument.Parse(stream);
            var pack = manifest.RootElement.GetProperty("id").GetString()!;
            var prefix = resource[..^"pack.json".Length];
            foreach (var dataset in manifest.RootElement.GetProperty("datasets").EnumerateArray())
            foreach (var file in dataset.GetProperty("files").EnumerateArray())
            {
                var path = file.GetProperty("path").GetString()!;
                using var content = assembly.GetManifestResourceStream(prefix + path)!;
                using var buffer = new MemoryStream();
                content.CopyTo(buffer);
                var bytes = buffer.ToArray();
                var image = dataset.GetProperty("kind").GetString() == "assets";
                var hashed = image ? bytes : Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(bytes).Replace("\r\n", "\n", StringComparison.Ordinal));
                Assert.Equal(file.GetProperty("sha256").GetString(), Convert.ToHexStringLower(SHA256.HashData(hashed)));
                if (!image) continue;
                var asset = Assert.IsType<AssetContent>(references.GetAsset(pack + ":" + path));
                Assert.Equal("image/webp", asset.ContentType);
                Assert.Equal(bytes, asset.Content.ToArray());
                Assert.Equal("RIFF", Encoding.ASCII.GetString(bytes.AsSpan(0, 4)));
                Assert.Equal("WEBP", Encoding.ASCII.GetString(bytes.AsSpan(8, 4)));
                Assert.True(hashes.Add(Convert.ToHexStringLower(SHA256.HashData(bytes))), "Duplicate embedded image.");
                Assert.Same(asset, references.GetAsset(pack + ":" + path)); // Resolved bytes are cached, not reloaded.
                total += bytes.Length;
                var attribution = references.AssetProvenance(pack + ":" + path)!.Attribution;
                Assert.Contains("Square Enix", attribution.Author);
                Assert.Contains("owner decision 2026-10-29", attribution.License);
            }
        }
        Assert.Equal(145, hashes.Count);
        Assert.Equal(NewAssetBytes, total);
        Assert.InRange(total, 1, AssetBudget);
    }

    [Theory]
    [InlineData(FaithCatalog.ItemsId, 92)]
    [InlineData(FaithCatalog.AbilitiesId, 48)]
    public void PickerRecordsCarryIconsAndAttributedOfficialExcerptsWithoutNewGameplayClaims(string catalogId, int expected)
    {
        var references = new ReferencePackStore([_plugin]).Pin(_plugin, "PPSA10665");
        var catalog = references.Catalog(catalogId)!;
        var records = Enumerable.Range(0, (catalog.Count + 199) / 200)
            .SelectMany(page => catalog.Search(new ReferenceQuery { Offset = page * 200, Limit = 200 }).Items).ToArray();
        Assert.Equal(expected, records.Count(record => record.AssetId is not null));
        foreach (var record in records)
        {
            var fallback = FaithEmbeddedCatalogs.Find(catalogId, record.Key)!;
            Assert.Equal(record.Description, fallback.Description);
            Assert.Equal(record.AssetId, fallback.AssetId);
            if (record.AssetId is null) continue;
            Assert.NotNull(references.GetAsset(record.AssetId));
            Assert.Contains("Official game description (excerpt)", record.Description);
            Assert.Contains("https://game8.co/", record.Description);
            Assert.Contains("Square Enix game text", record.Description);
            Assert.Contains("owner decision 2026-10-29", record.Description);
            Assert.Null(record.Limit);
            Assert.Equal("Inferred", record.Attributes["confidence"]);
        }
        Assert.Null(references.Catalog(FaithCatalog.ItemsId)!.Find("900000"));
    }

    [Fact]
    public void EagerAndPagedInventoryCellsDisplayTheSameResolvedIconAndFallbackRemainsUsable()
    {
        var items = FaithTestData.Items();
        BinaryPrimitives.WriteInt32LittleEndian(items, 5);
        BinaryPrimitives.WriteInt32LittleEndian(items.AsSpan(16), 900000);
        var bytes = FaithTestData.Container((FaithSchema.ItemType, FaithSchema.ItemVersion, items));
        var file = FaithTestData.File(bytes);
        var references = new ReferencePackStore([_plugin]).Pin(_plugin, "PPSA10665");
        using var document = _plugin.Open(file, references);
        var view = Assert.IsType<TabsNode>(document.BuildView());
        var table = Assert.Single(view.Tabs.Single(tab => tab.Title == "Inventory").Children.OfType<TableNode>());
        var image = Assert.IsType<ImageNode>(table.Rows[0].Cells["icon"]);
        Assert.Equal("Potion", image.Alt);
        Assert.NotNull(document.GetAsset(image.AssetId));
        Assert.IsType<TextNode>(table.Rows[1].Cells["icon"]);
        var page = ((IQueryableDocument)document).Query(FaithInventoryView.CollectionId, new() { Limit = 1 });
        Assert.Equal(image, Assert.IsType<ImageNode>(Assert.Single(page.Rows).Cells["icon"]));
        Assert.Contains(references.Datasets(), dataset => dataset.Used && dataset.Dataset.Id == "game_icons");
        using var fallback = _plugin.Open(file);
        var fallbackView = Assert.IsType<TabsNode>(fallback.BuildView());
        var fallbackTable = Assert.Single(fallbackView.Tabs.Single(tab => tab.Title == "Inventory").Children.OfType<TableNode>());
        Assert.IsType<TextNode>(fallbackTable.Rows[0].Cells["icon"]);
        Assert.Null(fallback.GetAsset(image.AssetId));
        Assert.Equal(bytes, Assert.Single(document.Serialize()).Content.ToArray());
        Assert.Equal(bytes, Assert.Single(fallback.Serialize()).Content.ToArray());
    }

    [Fact]
    public void AbilityIconsRemainBehindTheExistingSpoilerGate()
    {
        var file = FaithTestData.File(FaithTestData.Container((99, 1, new byte[8])));
        var references = new ReferencePackStore([_plugin]).Pin(_plugin, "PPSA10665");
        using var document = _plugin.Open(file, references);
        var root = Assert.IsType<TabsNode>(document.BuildView());
        var lazy = Assert.Single(root.Tabs.Single(tab => tab.Title == FaithAbilityView.TabTitle).Children.OfType<SectionNode>());
        Assert.Empty(lazy.Children);
        var table = Assert.Single(document.ExpandNode(FaithAbilityView.LazyId).OfType<TableNode>());
        Assert.Equal(48, table.Rows.Count);
        Assert.All(table.Rows, row => Assert.NotNull(document.GetAsset(Assert.IsType<ImageNode>(row.Cells["icon"]).AssetId)));
        Assert.Equal(file.ReadAllBytes(), Assert.Single(document.Serialize()).Content.ToArray());
    }

    [Theory]
    [InlineData("PPSA10665")]
    [InlineData("PPSA13803")]
    public void WorldAndLazyRegionalBackgroundsValidateWithoutInventingSaveCoordinates(string title)
    {
        var file = FaithSamples.Snapshot(title);
        var references = new ReferencePackStore([_plugin]).Pin(_plugin, title);
        using var document = _plugin.Open(file, references);
        var root = Assert.IsType<TabsNode>(document.BuildView());
        var context = root.Tabs.Single(tab => tab.Title == "Character context");
        var maps = context.Children.OfType<SectionNode>().SelectMany(section => section.Children).OfType<MapNode>().ToArray();
        var world = Assert.Single(maps);
        var marker = Assert.Single(world.Markers);
        Assert.Null(marker.X);
        Assert.Null(marker.Y);
        Assert.Equal(MapMarkerStatus.Unknown, marker.Status);
        Assert.Equal("party/map", marker.Target!.Id);
        var regions = document.ExpandNode(FaithMapArt.RegionalLazyId).OfType<MapNode>().ToArray();
        Assert.Equal(4, regions.Length);
        Assert.All(regions, region => Assert.Empty(region.Markers));
        foreach (var map in regions.Prepend(world))
        {
            SpatialViewValidation.Validate(map);
            var background = Assert.IsType<MapBackground>(map.Background);
            Assert.True(background.EmbeddingPermitted);
            Assert.NotNull(document.GetAsset(background.AssetId));
            Assert.Equal(references.AssetProvenance(background.AssetId)!.Attribution, background.Attribution);
            Assert.False(map.CoordinateEmbeddingPermitted);
            Assert.InRange(map.Bounds.MaxX, 1, 2048);
            Assert.InRange(map.Bounds.MaxY, 1, 2048);
            Assert.Contains("reference", map.TextAlternative, StringComparison.OrdinalIgnoreCase);
        }
        Assert.Contains(references.Datasets(), dataset => dataset.Used && dataset.Dataset.Id == "world_art");
        Assert.Contains(references.Datasets(), dataset => dataset.Used && dataset.Dataset.Id == "regional_art");
        Assert.Equal(file.ReadAllBytes(), Assert.Single(document.Serialize()).Content.ToArray());
        using var fallback = _plugin.Open(file);
        _ = fallback.BuildView();
        Assert.Single(fallback.ExpandNode(FaithMapArt.RegionalLazyId).OfType<TextNode>());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ImportRejectsAnAlteredImageEvenWhenItsHashIsRecomputed(bool recomputeHash)
    {
        const string Prefix = "refpacks/ffxvi.maps.en/";
        var assembly = typeof(FinalFantasy16Plugin).Assembly;
        using var stream = assembly.GetManifestResourceStream(Prefix + "pack.json")!;
        var manifest = JsonNode.Parse(stream)!;
        manifest["id"] = "ffxvi.corrupt-media";
        var assetFile = manifest["datasets"]!.AsArray().First(dataset => dataset!["kind"]!.GetValue<string>() == "assets")!["files"]![0]!;
        var brokenPath = assetFile["path"]!.GetValue<string>();
        byte[] invalid = Encoding.UTF8.GetBytes("<html>This is not a raster image.</html>");
        if (recomputeHash) assetFile["sha256"] = Convert.ToHexStringLower(SHA256.HashData(invalid));
        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            using (var writer = new StreamWriter(zip.CreateEntry("pack.json").Open())) writer.Write(manifest.ToJsonString());
            foreach (var file in manifest["datasets"]!.AsArray().SelectMany(dataset => dataset!["files"]!.AsArray()))
            {
                var path = file!["path"]!.GetValue<string>();
                using var target = zip.CreateEntry(path).Open();
                if (path == brokenPath) target.Write(invalid);
                else
                {
                    using var source = assembly.GetManifestResourceStream(Prefix + path)!;
                    source.CopyTo(target);
                }
            }
        }
        var error = Assert.Throws<ReferencePackException>(() => new ReferencePackStore([_plugin]).Import(buffer.ToArray(), "corrupt-media.zip"));
        Assert.Contains(recomputeHash ? "not a PNG, JPEG or WebP" : "hash mismatch", error.Message);
    }
}
