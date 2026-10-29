using System.Security.Cryptography;
using System.Text.Json;
using SaveToolkit.Core.References;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.TalesOfArise.Tests;

public sealed class AriseAssetTests
{
    private const string ResourcePrefix = "refpacks/talesofarise.catalog/";
    private static readonly string[] CatalogIds = ["items", "equipment", "artes", "skills", "skill-nodes", "progress"];

    [Fact]
    public void EmbeddedArtworkMatchesReviewedHashesTypesDimensionsAndBudget()
    {
        var context = Context();
        using var ledger = Resource("asset-sources.json");
        long total = 0;
        var fingerprints = new HashSet<string>(StringComparer.Ordinal);
        var images = ledger.RootElement.GetProperty("assets").EnumerateArray().ToArray();
        Assert.Equal(111, images.Length);
        foreach (var image in images)
        {
            var path = image.GetProperty("path").GetString()!;
            var asset = context.GetAsset("talesofarise.catalog:" + path);
            Assert.NotNull(asset);
            var fingerprint = Convert.ToHexStringLower(SHA256.HashData(asset.Content.Span));
            Assert.Equal(image.GetProperty("sha256").GetString(), fingerprint);
            Assert.True(fingerprints.Add(fingerprint), "Identical images must share an asset.");
            Assert.Equal(image.GetProperty("bytes").GetInt32(), asset.Content.Length);
            Assert.Equal(path.EndsWith(".png", StringComparison.Ordinal) ? "image/png" : "image/webp", asset.ContentType);
            Assert.True(path.EndsWith(".png", StringComparison.Ordinal)
                ? asset.Content.Span.StartsWith((ReadOnlySpan<byte>)[0x89, 0x50, 0x4E, 0x47])
                : asset.Content.Span.StartsWith("RIFF"u8));
            var maximumEdge = image.GetProperty("role").GetString() == "map" ? 2048 : 64;
            Assert.InRange(image.GetProperty("width").GetInt32(), 1, maximumEdge);
            Assert.InRange(image.GetProperty("height").GetInt32(), 1, maximumEdge);
            var provenance = context.AssetProvenance("talesofarise.catalog:" + path);
            Assert.NotNull(provenance);
            Assert.Equal(AriseAssets.MediaLicense, provenance.Attribution.License);
            total += asset.Content.Length;
        }

        Assert.Equal(185302, total);
        Assert.InRange(total, 1, 4_000_000);
    }

    [Fact]
    public void EveryCatalogIconResolvesAndOfficialDescriptionsRetainSeparateProvenance()
    {
        var context = Context();
        var plugin = new TalesOfArisePlugin();
        var icons = 0;
        var descriptions = 0;
        foreach (var catalogId in CatalogIds)
        {
            var catalog = context.Catalog(catalogId)!;
            foreach (var record in All(catalog))
            {
                if (record.AssetId is { } asset)
                {
                    icons++;
                    Assert.NotNull(context.GetAsset(asset));
                    Assert.NotEmpty(record.Attributes["iconRole"]);
                    Assert.Contains("https://", record.Attributes["iconSource"]);
                }

                if (record.Attributes.TryGetValue("descriptionSource", out var source))
                {
                    descriptions++;
                    Assert.Contains("Official in-game description (Aselia transcription):", record.Description);
                    Assert.Contains(source, record.Description);
                    Assert.Contains("CC BY-SA 3.0", record.Description);
                    Assert.Contains(AriseAssets.MediaLicense, record.Description);
                    Assert.Contains(source, plugin.Info.References);
                }
            }

            for (var offset = 0; offset < catalog.Count; offset += ReferenceQuery.MaxLimit)
            {
                var page = AriseCatalogQuery.Query(catalogId, catalog, new() { Offset = offset, Limit = ReferenceQuery.MaxLimit });
                foreach (var row in page.Rows.Where(row => catalog.Find(row.Key)!.AssetId is not null))
                {
                    Assert.Equal(catalog.Find(row.Key)!.AssetId, Assert.IsType<ImageNode>(row.Cells["icon"]).AssetId);
                }
            }
        }

        Assert.Equal(1660, icons);
        Assert.Equal(25, descriptions);
        Assert.All(All(context.Catalog("skills")!), record => Assert.NotNull(record.AssetId));
        Assert.All(Enumerable.Range(1, 13), id => Assert.NotNull(context.Catalog("items")!.Find(id.ToString())!.AssetId));
        Assert.Contains("A fast-hitting two-hit slice attack.", context.Catalog("artes")!.Find("martial-artes:alphen:swallow-blade")!.Description);
    }

    [Fact]
    public void SampleInventoryRendersIconsWithoutChangingBytesAndAssetsAreCached()
    {
        var plugin = new TalesOfArisePlugin();
        var context = Context();
        var file = AriseSampleTests.GameplayFiles().First(file => file.FileName == "SaveData_002.sav");
        using var document = plugin.Open(file, context);
        _ = document.BuildView();
        var inventory = document.LoadInventory();
        var apple = inventory.Rows.Single(row => row.Key == "inventory/24/quantity");
        var icon = Assert.IsType<ImageNode>(apple.Cells["icon"]);
        Assert.Same(context.GetAsset(icon.AssetId), document.GetAsset(icon.AssetId));
        Assert.Same(document.GetAsset(icon.AssetId), document.GetAsset(icon.AssetId));
        var page = Assert.IsAssignableFrom<IQueryableDocument>(document).Query("inventory", new() { Text = "Apple Gel" });
        Assert.Equal(icon.AssetId, Assert.IsType<ImageNode>(Assert.Single(page.Rows).Cells["icon"]).AssetId);
        Assert.Equal(file.ReadAllBytes(), Assert.Single(document.Serialize()).Content.ToArray());
    }

    [Fact]
    public void ArtworkIsOptionalAndSchematicFallbackRetainsEveryMarker()
    {
        var context = Context();
        var withArt = new AriseSpatialViews(new AriseReferences(context)).Maps.OfType<MapNode>().Single();
        var withoutArt = new AriseSpatialViews(new AriseReferences(new NoArtworkContext(context))).Maps.OfType<MapNode>().Single();
        SpatialViewValidation.Validate(withArt);
        SpatialViewValidation.Validate(withoutArt);
        Assert.NotNull(withArt.Background);
        Assert.True(withArt.Background.EmbeddingPermitted);
        Assert.Contains("decorative", withArt.Background.Attribution.Modifications);
        Assert.Null(withoutArt.Background);
        Assert.Equal(withArt.Markers.Select(marker => (marker.Id, marker.Label, marker.Status, marker.X, marker.Y)),
            withoutArt.Markers.Select(marker => (marker.Id, marker.Label, marker.Status, marker.X, marker.Y)));
        Assert.Equal(withArt.Bounds, withoutArt.Bounds);
        Assert.Equal(withArt.Transform, withoutArt.Transform);
        Assert.All(withArt.Markers, marker => Assert.Equal(MapMarkerStatus.Unknown, marker.Status));
        Assert.Null(AriseAssets.Background(IReferenceContext.Empty));
    }

    private static IReferenceContext Context()
    {
        var plugin = new TalesOfArisePlugin();
        var store = new ReferencePackStore([plugin]);
        Assert.Empty(store.LoadErrors);
        return store.Pin(plugin, "PPSA02745");
    }

    private static JsonDocument Resource(string path)
    {
        using var stream = typeof(TalesOfArisePlugin).Assembly.GetManifestResourceStream(ResourcePrefix + path)!;
        return JsonDocument.Parse(stream);
    }

    private static IEnumerable<ReferenceRecord> All(IReferenceCatalog catalog)
    {
        for (var offset = 0; offset < catalog.Count; offset += ReferenceQuery.MaxLimit)
        {
            foreach (var record in catalog.Search(new() { Offset = offset, Limit = ReferenceQuery.MaxLimit }).Items)
            {
                yield return record;
            }
        }
    }

    private sealed class NoArtworkContext(IReferenceContext inner) : IReferenceContext
    {
        public IReadOnlyList<ReferencePackInfo> Packs => inner.Packs;
        public IReferenceCatalog? Catalog(string catalogId, string? gameBuild = null) => inner.Catalog(catalogId, gameBuild);
        public SaveToolkit.Sdk.AssetContent? GetAsset(string assetId) => null;
        public ReferenceDatasetInfo? AssetProvenance(string assetId) => null;
    }
}
