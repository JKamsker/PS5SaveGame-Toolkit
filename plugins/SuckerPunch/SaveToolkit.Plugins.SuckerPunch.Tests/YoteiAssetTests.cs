using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.IO.Compression;
using SaveToolkit.Core.References;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Views;
using static SaveToolkit.Plugins.SuckerPunch.Tests.YoteiTestData;

namespace SaveToolkit.Plugins.SuckerPunch.Tests;

public sealed class YoteiAssetTests
{
    [Fact]
    public void EveryAssetHashTypeBindingAttributionAndCacheReuseIsValid()
    {
        var plugin = new SuckerPunchPlugin();
        var store = new ReferencePackStore([plugin]);
        Assert.Empty(store.LoadErrors);
        var context = store.Pin(plugin, Title);
        var assembly = typeof(SuckerPunchPlugin).Assembly;
        using var stream = assembly.GetManifestResourceStream(assembly.GetManifestResourceNames()
            .Single(name => name.Replace('\\', '/') == "refpacks/suckerpunch.yotei/pack.json"));
        Assert.NotNull(stream);
        using var manifest = JsonDocument.Parse(stream);
        var files = manifest.RootElement.GetProperty("datasets").EnumerateArray().Where(dataset => dataset.GetProperty("kind").GetString() == "assets")
            .SelectMany(dataset => dataset.GetProperty("files").EnumerateArray()).ToArray();
        var total = 0;
        var hashes = new HashSet<string>();
        foreach (var file in files)
        {
            var id = "suckerpunch.yotei:" + file.GetProperty("path").GetString();
            var asset = Assert.IsType<AssetContent>(context.GetAsset(id));
            var hash = Convert.ToHexStringLower(SHA256.HashData(asset.Content.Span));
            Assert.Equal(file.GetProperty("sha256").GetString(), hash);
            Assert.True(hashes.Add(hash));
            Assert.Equal("image/webp", asset.ContentType);
            Assert.Equal("RIFF"u8.ToArray(), asset.Content.Span[..4].ToArray());
            Assert.Equal("WEBP"u8.ToArray(), asset.Content.Span[8..12].ToArray());
            Assert.Same(asset, store.Pin(plugin, Title).GetAsset(id));
            var provenance = Assert.IsType<ReferenceDatasetInfo>(context.AssetProvenance(id));
            Assert.Contains("Sony Interactive Entertainment / Sucker Punch Productions", provenance.Attribution.Author);
            Assert.Contains("owner decision 2026-10-29", provenance.Attribution.License);
            Assert.Contains(provenance.Attribution.SourceUrl, plugin.Info.References);
            Assert.Null(store.Pin(plugin, "PPSA02225").GetAsset(id));
            total += asset.Content.Length;
        }
        Assert.Equal(4, files.Length);
        Assert.Equal(89702, total);
        foreach (var catalogId in new[] { "suckerpunch_yotei_items", "suckerpunch_yotei_techniques", "suckerpunch_yotei_world" })
        {
            var catalog = context.Catalog(catalogId)!;
            Assert.Contains(catalog.Dataset.Attribution.SourceUrl, plugin.Info.References);
            Assert.All(catalog.Search(new ReferenceQuery { Limit = 200 }).Items.Where(record => record.AssetId is not null),
                record => Assert.NotNull(context.GetAsset(record.AssetId!)));
        }
        using var doc = plugin.Open(Slot("suckerpunchUser4"), context);
        var root = (TabsNode)doc.BuildView();
        var inventory = Inventory(doc);
        Assert.Equal(2, inventory.Rows.Count(row => row.Cells["icon"] is ImageNode));
        var world = root.Tabs.Single(tab => tab.Title == "World").Children.OfType<TableNode>().Single();
        Assert.All(world.Rows, row => Assert.IsType<ImageNode>(row.Cells["icon"]));
        Assert.Equal(4, context.Datasets().Count(dataset => dataset.Used));
    }

    [Fact]
    public void IconPackPassesSharedLoaderWithSixtyFourPixelDimensionCap()
    {
        var assembly = typeof(SuckerPunchPlugin).Assembly;
        const string prefix = "refpacks/suckerpunch.yotei/";
        var names = assembly.GetManifestResourceNames().ToDictionary(name => name.Replace('\\', '/'), StringComparer.Ordinal);
        using var stream = assembly.GetManifestResourceStream(names[prefix + "pack.json"])!;
        var manifest = JsonNode.Parse(stream)!;
        var datasets = manifest["datasets"]!.AsArray();
        var art = datasets.Single(dataset => dataset!["kind"]!.GetValue<string>() == "assets")!;
        var artFiles = art["files"]!.AsArray();
        artFiles.Remove(artFiles.Single(file => file!["path"]!.GetValue<string>().StartsWith("maps/", StringComparison.Ordinal)));
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            using (var entry = archive.CreateEntry("pack.json").Open())
            using (var writer = new Utf8JsonWriter(entry)) manifest.WriteTo(writer);
            foreach (var file in datasets.SelectMany(dataset => dataset!["files"]!.AsArray()))
            {
                var path = file!["path"]!.GetValue<string>();
                using var source = assembly.GetManifestResourceStream(names[prefix + path])!;
                using var destination = archive.CreateEntry(path).Open();
                source.CopyTo(destination);
            }
        }
        var store = new ReferencePackStore([], new ReferencePackOptions
        { Limits = new ReferencePackLimits { MaxImageDimension = 64 } });
        var pack = store.Import(buffer.ToArray(), "yotei-icons.zip");
        Assert.Equal(3, pack.Info.Datasets.Single(dataset => dataset.Kind == ReferenceDatasetKind.Assets).ItemCount);
    }

    [Theory]
    [InlineData("suckerpunch_yotei_items", 48)]
    [InlineData("suckerpunch_yotei_techniques", 10)]
    [InlineData("suckerpunch_yotei_world", 16)]
    public void GuideCatalogsOfferAttributedSearchCategorySortAndBrowseOnlyRecords(string id, int count)
    {
        var plugin = new SuckerPunchPlugin();
        var catalog = new ReferencePackStore([plugin]).Pin(plugin, Title).Catalog(id)!;
        Assert.Equal(count, catalog.Count);
        foreach (var key in catalog.SortKeys)
            foreach (var descending in new[] { false, true })
            {
                var page = catalog.Search(new ReferenceQuery { SortBy = key, Descending = descending, Limit = 200 });
                Assert.Equal(count, page.Items.Count);
                Assert.All(page.Items, record => { Assert.NotEmpty(record.AttributionText); Assert.NotEmpty(record.Description!); });
            }
        var first = catalog.Search(new ReferenceQuery { Limit = 1 }).Items[0];
        Assert.Contains(catalog.Search(new ReferenceQuery { Text = first.Name, Category = first.Category }).Items, row => row.Key == first.Key);
    }
}
