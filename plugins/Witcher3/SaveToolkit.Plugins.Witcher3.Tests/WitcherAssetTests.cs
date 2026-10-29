using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using SaveToolkit.Core.References;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.Witcher3.Tests;

public sealed class WitcherAssetTests
{
    private const long ExpectedAssetBytes = 1_398_570;
    private const int MaximumIconDimension = 64;
    private const int MaximumMapDimension = 2048;
    private const int MaximumMapBytes = 600_000;

    [Fact]
    public void EveryEmbeddedAssetMatchesItsAuditHashHeaderDimensionsAndBudget()
    {
        var context = Context();
        using var audit = Audit();
        var assets = audit.RootElement.GetProperty("assets").EnumerateArray().ToArray();
        Assert.Equal(376, assets.Length);
        long total = 0;
        var hashes = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in assets)
        {
            var id = entry.GetProperty("pack").GetString() + ":" + entry.GetProperty("path").GetString();
            var asset = context.GetAsset(id)!;
            Assert.NotNull(asset);
            Assert.Same(asset, context.GetAsset(id)); // Validated pack assets are shared, never loaded per row/document.
            Assert.Equal("image/webp", asset.ContentType);
            var bytes = asset.Content.ToArray();
            Assert.Equal(entry.GetProperty("bytes").GetInt32(), bytes.Length);
            var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            Assert.Equal(entry.GetProperty("sha256").GetString(), hash);
            Assert.True(hashes.Add(hash));
            Assert.Equal("RIFF"u8.ToArray(), bytes[..4]);
            Assert.Equal("WEBP"u8.ToArray(), bytes[8..12]);
            Assert.Equal(bytes.Length - 8, (long)BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(4)));
            var dimensions = WebpDimensions(bytes);
            Assert.Equal(entry.GetProperty("width").GetInt32(), dimensions.Width);
            Assert.Equal(entry.GetProperty("height").GetInt32(), dimensions.Height);
            var map = id.StartsWith(RegionalMapViews.PackId + ":", StringComparison.Ordinal);
            Assert.InRange(dimensions.Width, 1, map ? MaximumMapDimension : MaximumIconDimension);
            Assert.InRange(dimensions.Height, 1, map ? MaximumMapDimension : MaximumIconDimension);
            if (map) { Assert.InRange(bytes.Length, 1, MaximumMapBytes); }
            var provenance = context.AssetProvenance(id)!;
            Assert.Contains("Rights reserved by CD PROJEKT RED", provenance.Attribution.License, StringComparison.Ordinal);
            Assert.Contains("owner decision 2026-10-29", provenance.Attribution.License, StringComparison.Ordinal);
            Assert.StartsWith("https://", entry.GetProperty("sourceUrl").GetString()!, StringComparison.Ordinal);
            total += bytes.Length;
        }
        Assert.Equal(ExpectedAssetBytes, total);
        Assert.InRange(total, 1, 4_000_000);
    }

    [Theory]
    [InlineData("items", 55, 43)]
    [InlineData("abilities", 107, 107)]
    [InlineData("gwent", 225, 0)]
    public void CatalogImagesResolveAndExpandedTextKeepsItsOwnProvenance(string id, int imageCount, int descriptionCount)
    {
        var context = Context();
        var catalog = context.Catalog(id)!;
        var records = Enumerable.Range(0, (catalog.Count + ReferenceQuery.MaxLimit - 1) / ReferenceQuery.MaxLimit)
            .SelectMany(page => catalog.Search(new() { Offset = page * ReferenceQuery.MaxLimit, Limit = ReferenceQuery.MaxLimit }).Items).ToArray();
        Assert.Equal(imageCount, records.Count(r => r.AssetId is not null));
        Assert.Equal(descriptionCount, records.Count(r => r.Attributes.ContainsKey("descriptionSource")));
        foreach (var record in records.Where(r => r.AssetId is not null))
        {
            Assert.NotNull(context.GetAsset(record.AssetId!));
            Assert.NotNull(context.AssetProvenance(record.AssetId!));
        }
        foreach (var record in records.Where(r => r.Attributes.ContainsKey("descriptionSource")))
        {
            Assert.Equal("CC-BY-SA-3.0", record.Dataset.Attribution.License);
            Assert.Equal(record.Source, record.Attributes["descriptionSource"]);
            Assert.Contains("PS5", record.Description!, StringComparison.Ordinal);
            Assert.DoesNotContain("{{", record.Description!, StringComparison.Ordinal);
            Assert.DoesNotContain("[[", record.Description!, StringComparison.Ordinal);
        }
        var view = new CatalogViews(context, "sav3-64.23.163", () => null);
        var imageKey = records.First(r => r.AssetId is not null).Key;
        var row = Assert.Single(view.Query("catalog/" + id, new() { Filters = new Dictionary<string, string> { ["raw"] = imageKey } }).Rows);
        Assert.IsType<ImageNode>(row.Cells["image"]);
        Assert.Contains(Assert.IsType<SectionNode>(row.Cells["details"]).Children.OfType<TextNode>(),
            text => text.Text.Contains("Rights reserved by CD PROJEKT RED", StringComparison.Ordinal));
    }

    [Fact]
    public void SampleSavedItemAbilityAndCardRowsShowLocalImagesWithoutChangingBytes()
    {
        var context = Context();
        var plugin = new Witcher3Plugin();
        foreach (var file in EnabledWriteFixture.Files())
        {
            using var document = plugin.Open(file, context);
            using var session = new DocumentSession("asset-round", file, plugin, document, references: context);
            _ = session.BuildView();
            var inventory = session.QueryCollection(InventoryViews.CollectionId, new() { Limit = 200 });
            var bread = Assert.Single(inventory.Rows, row => Assert.IsType<TextNode>(row.Cells["raw"]).Text == "Bread");
            Assert.IsType<ImageNode>(bread.Cells["image"]);
            var abilities = session.QueryCollection(AbilityCollections.Prefix + "skills", new() { Limit = 200 });
            Assert.Equal(95, abilities.Rows.Count(row => row.Cells["image"] is ImageNode));
            var cards = session.QueryCollection(GwentCollection.CollectionId, new() { Limit = 200 });
            Assert.All(cards.Rows, row => Assert.IsType<ImageNode>(row.Cells["image"]));
            Assert.Equal(file.ReadAllBytes(), Assert.Single(document.Serialize()).Content.ToArray());
            Assert.Contains(context.Datasets(), d => d.Used && d.Dataset.Id == "icons");
        }
    }

    [Fact]
    public void RegionalArtIsLazyAttributedAndNeverPlotsUnmappedSavedTags()
    {
        var context = Context();
        var plugin = new Witcher3Plugin();
        var file = EnabledWriteFixture.Files()[0];
        using var document = plugin.Open(file, context);
        _ = document.BuildView();
        Assert.DoesNotContain(context.Datasets(), d => d.Used && d.Pack.Id == RegionalMapViews.PackId);
        var maps = new RegionalMapViews(context);
        foreach (var section in maps.Build().Cast<SectionNode>())
        {
            Assert.Empty(section.Children);
            var map = Assert.IsType<MapNode>(Assert.Single(document.ExpandNode(section.LazyId!)));
            SpatialViewValidation.Validate(map);
            Assert.NotNull(map.Background);
            Assert.True(map.Background.EmbeddingPermitted);
            Assert.NotNull(context.GetAsset(map.Background.AssetId));
            Assert.Equal(context.AssetProvenance(map.Background.AssetId)!.Attribution, map.Background.Attribution);
            Assert.Empty(map.Markers);
            Assert.False(map.CoordinateEmbeddingPermitted);
            Assert.Contains("joins are unknown", map.TextAlternative, StringComparison.Ordinal);
            var missing = Assert.IsType<MapNode>(Assert.Single(new RegionalMapViews(IReferenceContext.Empty).Expand(section.LazyId!)));
            SpatialViewValidation.Validate(missing);
            Assert.Null(missing.Background);
            Assert.NotNull(missing.ExternalSource);
            Assert.Contains("unavailable", missing.TextAlternative, StringComparison.Ordinal);
        }
        var savedTags = Assert.IsType<MapNode>(Assert.Single(document.ExpandNode("map-view/0")));
        var parsed = EnabledWriteFixture.Read(file.ReadAllBytes());
        Assert.Equal(MapReader.Read(parsed.Tree, parsed.Container.Payload, parsed.Directory.Names)!.Count, savedTags.Markers.Count);
        Assert.All(savedTags.Markers, marker => { Assert.Null(marker.X); Assert.Null(marker.Y); });
        Assert.Contains(context.Datasets(), d => d.Used && d.Pack.Id == RegionalMapViews.PackId);
        Assert.Throws<KeyNotFoundException>(() => maps.Expand("map-art/unknown"));
    }

    [Fact]
    public void MissingImageProvenanceRetainsRawCatalogAndSavedIdentities()
    {
        var context = Context();
        var bread = context.Catalog("items")!.Find("Bread")!;
        Assert.IsType<TextNode>(ReferenceImages.Cell(bread, _ => null));
        Assert.IsType<TextNode>(ReferenceImages.Cell(null, context.AssetProvenance));
        Assert.Empty(ReferenceImages.Sources(bread, _ => null));
    }

    private static ReferenceContext Context()
    {
        var plugin = new Witcher3Plugin();
        var store = new ReferencePackStore([plugin]);
        Assert.Empty(store.LoadErrors); // Includes every manifest SHA-256 and framework image-type validation.
        return store.Pin(plugin, "PPSA04018");
    }

    private static JsonDocument Audit()
    {
        using var stream = typeof(Witcher3Plugin).Assembly.GetManifestResourceStream("refpacks/assets.source.json")!;
        Assert.NotNull(stream);
        return JsonDocument.Parse(stream);
    }

    private static (int Width, int Height) WebpDimensions(byte[] bytes)
    {
        // Pillow emits VP8X for transparent icons and VP8 for opaque images; verify actual header dimensions.
        if (bytes.AsSpan(12, 4).SequenceEqual("VP8X"u8))
        {
            return (1 + bytes[24] + (bytes[25] << 8) + (bytes[26] << 16),
                1 + bytes[27] + (bytes[28] << 8) + (bytes[29] << 16));
        }
        Assert.Equal("VP8 "u8.ToArray(), bytes[12..16]);
        Assert.Equal(new byte[] { 0x9d, 0x01, 0x2a }, bytes[23..26]);
        return (BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(26)) & 0x3fff,
            BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(28)) & 0x3fff);
    }
}
