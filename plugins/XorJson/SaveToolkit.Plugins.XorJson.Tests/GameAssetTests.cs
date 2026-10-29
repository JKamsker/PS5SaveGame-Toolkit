using SaveToolkit.Core.References;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.XorJson.Tests;

public sealed class GameAssetTests
{
    internal const string OwnerDecision = "Rights reserved by Mojang Studios/Microsoft; used with attribution per owner decision 2026-10-29";
    private const string MapAsset = "xorjson.community.en:maps/overworld.webp";
    private const int EmbeddedImageBytes = 974007;

    [Fact]
    public void PackValidatesEveryHashImageTypeAndRecordAssetWithinTheBudget()
    {
        var plugin = new XorJsonPlugin();
        var store = new ReferencePackStore([plugin]);
        Assert.Empty(store.LoadErrors); // Loader validates hashes, image headers, dimensions and extensions.
        var references = store.Pin(plugin, "PPSA16064");
        var assets = new HashSet<string>(StringComparer.Ordinal);
        var descriptionCount = 0;
        foreach (var (catalogId, count, descriptions) in new[]
        {
            ("xorjson-items", 328, 309), ("xorjson-effects", 60, 60), ("xorjson-enchantments", 30, 30),
        })
        {
            var catalog = references.Catalog(catalogId)!;
            var official = 0;
            Assert.Equal(count, catalog.Count);
            for (var offset = 0; offset < count; offset += ReferenceQuery.MaxLimit)
            foreach (var record in catalog.Search(new ReferenceQuery { Offset = offset, Limit = ReferenceQuery.MaxLimit }).Items)
            {
                var assetId = Assert.IsType<string>(record.AssetId);
                assets.Add(assetId);
                var image = references.GetAsset(assetId);
                Assert.NotNull(image);
                Assert.True(image.ContentType is "image/png" or "image/webp");
                Assert.Equal(OwnerDecision, references.AssetProvenance(assetId)!.Attribution.License);
                Assert.Contains("MIT identifiers", record.Dataset.Attribution.Author);
                Assert.Contains("PS5 applicability remains unverified", record.Dataset.Attribution.Modifications);
                Assert.Null(record.Limit);
                if (record.Description?.Contains("Game description (PC", StringComparison.Ordinal) != true) continue;
                official++;
                Assert.StartsWith("https://www.dungeons.tools/2/", record.Source!);
                Assert.Contains("Game description (PC", record.Description!);
                Assert.Contains("PS5 applicability unverified", record.Description!);
            }
            Assert.Equal(descriptions, official);
            descriptionCount += official;
        }
        Assert.Equal(399, descriptionCount);
        Assert.Equal(388, assets.Count);
        assets.Add(MapAsset);
        var bytes = assets.Sum(asset => references.GetAsset(asset)!.Content.Length);
        Assert.Equal(EmbeddedImageBytes, bytes);
        Assert.InRange(bytes, 1, 4_000_000);
        Assert.Equal(2, references.Packs.Single().Datasets.Count(dataset => dataset.Kind == ReferenceDatasetKind.Assets));
    }

    [Theory]
    [InlineData("xorjson-items", "SW.Item.Sword", "A classic sword used by heroes and survivors")]
    [InlineData("xorjson-effects", "SW.Effect.ArrowBurst", "Grants a 20% chance to gain an additional arrow")]
    [InlineData("xorjson-enchantments", "SW.Enchantment.Arcane", "Using an artifact grants the Artificing status")]
    public void NativePickerCarriesGameTextIconsAndSeparateArtworkProvenance(string catalog, string id, string text)
    {
        var plugin = new XorJsonPlugin();
        var references = new ReferencePackStore([plugin]).Pin(plugin, "PPSA16064");
        var candidate = new ReferenceFieldService(references).Find(new ReferenceSpec(catalog), id);
        Assert.Contains(text, candidate.Description!);
        Assert.NotNull(candidate.AssetId);
        Assert.Equal("xorjson-icons", candidate.AssetDataset!.Id);
        Assert.Equal(OwnerDecision, candidate.AssetDataset.Attribution.License);
        Assert.Contains("identifierSource", references.Catalog(catalog)!.Find(id)!.Attributes.Keys);
    }

    [Fact]
    public void SampleInventoryShowsLocalIconsAndTextAndReusesPinnedAssetsAcrossDocuments()
    {
        var plugin = new XorJsonPlugin();
        var references = new ReferencePackStore([plugin]).Pin(plugin, "PPSA16064");
        var file = SampleSaves.Load("PPSA16064").AllDiskFiles.Where(file => !file.IsSystemFile).MaxBy(file => file.Length)!;
        using var document = plugin.Open(file, references);
        using var second = plugin.Open(file, references);
        document.BuildView();
        var page = Assert.IsAssignableFrom<IQueryableDocument>(document).Query("character/inventory", new());
        Assert.Equal(5, page.Rows.Count);
        Assert.All(page.Rows, row =>
        {
            var icon = Assert.IsType<ImageNode>(row.Cells["icon"]);
            var asset = document.GetAsset(icon.AssetId);
            Assert.NotNull(asset);
            Assert.Same(asset, second.GetAsset(icon.AssetId));
            var label = Assert.IsType<SectionNode>(row.Cells["name"]);
            Assert.Contains(label.Children.OfType<TextNode>(), node => node.Text.Contains("Game description (PC", StringComparison.Ordinal));
        });
        Assert.Contains(references.Datasets(), dataset => dataset.Dataset.Id == "xorjson-icons" && dataset.Used);
        Assert.Equal(file.ReadAllBytes(), document.Serialize()[0].Content.ToArray());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void MapBackgroundHasProvenanceAndAnHonestUnpositionedFallback(bool withReferences)
    {
        var plugin = new XorJsonPlugin();
        var references = withReferences ? new ReferencePackStore([plugin]).Pin(plugin, "PPSA16064") : IReferenceContext.Empty;
        var file = SampleSaves.Load("PPSA16064").AllDiskFiles.Where(file => !file.IsSystemFile).MaxBy(file => file.Length)!;
        using var document = plugin.Open(file, references);
        var map = Assert.Single(StructuralTests.Nodes(document).OfType<MapNode>());
        SpatialViewValidation.Validate(map);
        Assert.NotEmpty(map.Markers);
        Assert.Contains("text list", map.TextAlternative);
        Assert.All(map.Markers, marker =>
        {
            Assert.Null(marker.X);
            Assert.Null(marker.Y);
            Assert.Equal(MapMarkerStatus.Unknown, marker.Status);
        });
        if (withReferences)
        {
            Assert.NotNull(map.Background);
            Assert.True(map.Background.EmbeddingPermitted);
            Assert.Equal(OwnerDecision, map.Background.Attribution.License);
            Assert.Equal(new MapBounds(0, 0, 2022, 1340), map.Bounds);
            Assert.Equal("image/webp", document.GetAsset(map.Background.AssetId)!.ContentType);
        }
        else Assert.Null(map.Background);
        Assert.Equal(file.ReadAllBytes(), document.Serialize()[0].Content.ToArray());
    }

    [Fact]
    public void MissingPackAndUnknownItemHaveNoInventedImageOrDescription()
    {
        var plugin = new XorJsonPlugin();
        var bytes = XorJsonTests.Encode("{\"TypeTag\":\"SW.Item.FutureUnknown\"}");
        using var document = plugin.Open(MemorySaveSet.SingleFile("unknown.sav", bytes, "PPSA16064"));
        Assert.Null(document.GetAsset(MapAsset));
        var field = Assert.Single(XorJsonTests.Fields(document));
        Assert.DoesNotContain("Game description", field.Description!);
        Assert.Equal(bytes, document.Serialize()[0].Content.ToArray());
    }
}
