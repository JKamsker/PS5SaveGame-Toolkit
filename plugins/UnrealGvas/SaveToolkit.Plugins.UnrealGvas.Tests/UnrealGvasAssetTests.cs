using System.Security.Cryptography;
using System.Text.Json;
using SaveToolkit.Core.References;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.UnrealGvas.Tests;

public sealed class UnrealGvasAssetTests
{
    private const int AssetBudget = 4_000_000;
    private const string HogwartsNotice = "Rights reserved by Warner Bros./Portkey Games; used with attribution per owner decision 2026-10-29";
    private const string SackboyNotice = "Rights reserved by SIE; used with attribution per owner decision 2026-10-29";

    [Theory]
    [InlineData("hogwarts.legilimens", "unrealgvas_hogwarts_gear", HogwartsNotice, "Warner Bros. Games / Portkey Games")]
    [InlineData("hogwarts.legilimens", "unrealgvas_hogwarts_visuals", HogwartsNotice, "Warner Bros. Games / Portkey Games")]
    [InlineData("unrealgvas.ps5", "unrealgvas_sackboy_costumes", SackboyNotice, "Sony Interactive Entertainment / Sumo Digital")]
    [InlineData("unrealgvas.ps5", "unrealgvas_sackboy_costume_art", SackboyNotice, "Sony Interactive Entertainment / Sumo Digital")]
    public void PublisherNoticesNameHoldersWithinLicenseLimit(string packId, string datasetId, string notice, string fullHolder)
    {
        using var manifest = ReadManifest(new UnrealGvasPlugin(), packId);
        var dataset = Assert.Single(manifest.RootElement.GetProperty("datasets").EnumerateArray(),
            dataset => dataset.GetProperty("id").GetString() == datasetId);
        var attribution = dataset.GetProperty("attribution");
        var license = attribution.GetProperty("license").GetString()!;

        Assert.Equal(notice, license);
        Assert.InRange(license.Length, 1, 100);
        Assert.Contains(fullHolder, attribution.GetProperty("author").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public void EmbeddedVisualAssetsResolveWithPinnedHashesAndAttribution()
    {
        var plugin = new UnrealGvasPlugin();
        var store = new ReferencePackStore([plugin]);
        Assert.Empty(store.LoadErrors);
        var context = store.Pin(plugin, "PPSA01603");
        var manifest = ReadManifest(plugin, "hogwarts.legilimens");
        var total = 0;
        var hashes = new HashSet<string>(StringComparer.Ordinal);
        foreach (var dataset in manifest.RootElement.GetProperty("datasets").EnumerateArray()
                     .Where(dataset => dataset.GetProperty("kind").GetString() == "assets"))
        foreach (var file in dataset.GetProperty("files").EnumerateArray())
        {
            var path = file.GetProperty("path").GetString()!;
            var id = "hogwarts.legilimens:" + path;
            var asset = Assert.IsType<AssetContent>(context.GetAsset(id));
            Assert.Equal(file.GetProperty("sha256").GetString(), Convert.ToHexStringLower(SHA256.HashData(asset.Content.Span)));
            Assert.True(hashes.Add(Convert.ToHexStringLower(SHA256.HashData(asset.Content.Span))));
            Assert.True(asset.Content.Span.StartsWith("RIFF"u8) && asset.Content.Span.Slice(8, 4).SequenceEqual("WEBP"u8));
            Assert.Equal("image/webp", asset.ContentType);
            var license = context.AssetProvenance(id)!.Attribution.License;
            if (path.StartsWith("icons/", StringComparison.Ordinal))
                Assert.Contains("owner decision 2026-10-29", license, StringComparison.Ordinal);
            total += asset.Content.Length;
        }
        Assert.Equal(9, hashes.Count);
        Assert.Equal(40_848, total);
        Assert.InRange(total, 1, AssetBudget);

        var gear = context.Catalog("unrealgvas_hogwarts_gear")!;
        Assert.Equal(8, gear.Count);
        Assert.All(gear.Search(new ReferenceQuery { Limit = 20 }).Items, record =>
        {
            Assert.NotNull(record.Description);
            Assert.NotNull(record.AssetId);
            Assert.NotNull(context.GetAsset(record.AssetId!));
        });
    }

    [Fact]
    public void SackboyCostumeAssetsResolveAndSchemaFieldsCarryThumbnails()
    {
        var plugin = new UnrealGvasPlugin();
        var store = new ReferencePackStore([plugin]);
        var context = store.Pin(plugin, "PPSA01288");
        var catalogue = context.Catalog("unrealgvas_sackboy_costumes")!;
        Assert.Equal(4, catalogue.Count);
        Assert.All(catalogue.Search(new ReferenceQuery { Limit = 10 }).Items, record =>
            Assert.IsType<AssetContent>(context.GetAsset(record.AssetId!)));
        var fieldRecords = context.Catalog("unrealgvas_sackboy_fields")!.Search(new ReferenceQuery { Category = "costumes", Limit = 20 }).Items;
        Assert.Equal(4, fieldRecords.Count(record => record.AssetId is not null));
    }

    private static JsonDocument ReadManifest(UnrealGvasPlugin plugin, string packId)
    {
        var assembly = typeof(UnrealGvasPlugin).Assembly;
        var name = Assert.Single(assembly.GetManifestResourceNames(), resource =>
            resource.Replace('\\', '/') == $"refpacks/{packId}/pack.json");
        return JsonDocument.Parse(assembly.GetManifestResourceStream(name)!);
    }
}
