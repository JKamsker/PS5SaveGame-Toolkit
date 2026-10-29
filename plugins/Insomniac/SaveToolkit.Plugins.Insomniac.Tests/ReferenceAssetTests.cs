using System.Security.Cryptography;
using System.Text.Json;
using SaveToolkit.Core.References;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Core.Storage;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.Insomniac.Tests;

public sealed class ReferenceAssetTests
{
    private const long AssetBytes = 1_240_234;
    private const int AssetCount = 64;

    [Fact]
    public void AllEmbeddedImagesHaveValidHashesTypesAttributionAndFitTheBudget()
    {
        var plugin = new InsomniacPlugin();
        var store = new ReferencePackStore([plugin]);
        Assert.Empty(store.LoadErrors);
        var context = store.Pin(plugin, "PPSA01474");
        var assembly = typeof(InsomniacPlugin).Assembly;
        var hashes = new HashSet<string>(StringComparer.Ordinal);
        long bytes = 0;
        var count = 0;
        foreach (var resource in assembly.GetManifestResourceNames().Where(name =>
            name.Replace('\\', '/').EndsWith("/pack.json", StringComparison.Ordinal)))
        {
            using var stream = assembly.GetManifestResourceStream(resource)!;
            using var manifest = JsonDocument.Parse(stream);
            var packId = manifest.RootElement.GetProperty("id").GetString();
            foreach (var dataset in manifest.RootElement.GetProperty("datasets").EnumerateArray())
            {
                if (dataset.GetProperty("kind").GetString() != "assets") continue;
                foreach (var file in dataset.GetProperty("files").EnumerateArray())
                {
                    var id = packId + ":" + file.GetProperty("path").GetString();
                    var asset = context.GetAsset(id)!;
                    Assert.NotNull(asset);
                    Assert.Equal("image/webp", asset.ContentType);
                    Assert.Equal("RIFF"u8.ToArray(), asset.Content.Span[..4].ToArray());
                    Assert.Equal("WEBP"u8.ToArray(), asset.Content.Span.Slice(8, 4).ToArray());
                    var hash = Convert.ToHexStringLower(SHA256.HashData(asset.Content.Span));
                    Assert.Equal(file.GetProperty("sha256").GetString(), hash);
                    Assert.True(hashes.Add(hash), "Duplicate image bytes should share one asset.");
                    Assert.InRange(asset.Content.Length, 1, id.Contains(":maps/", StringComparison.Ordinal) ? 600_000 : 20_000);
                    var attribution = context.AssetProvenance(id)!.Attribution;
                    Assert.Contains("Rights reserved by", attribution.License);
                    Assert.Contains("2026-10-29", attribution.License);
                    Assert.Contains(attribution.SourceUrl, plugin.Info.References);
                    bytes += asset.Content.Length;
                    count++;
                }
            }
        }
        Assert.Equal(AssetCount, count);
        Assert.Equal(AssetBytes, bytes);
        Assert.True(bytes <= 4_000_000);
    }

    [Fact]
    public void CatalogueImageIdsResolveAndUnconfirmedInternalArmourKeepsItsFallback()
    {
        var plugin = new InsomniacPlugin();
        var context = new ReferencePackStore([plugin]).Pin(plugin, "PPSA01474");
        foreach (var (id, expectedImages, expectedSynopses) in new[]
        {
            ("rift-weapons", 20, 20), ("rift-armor", 51, 51), ("rift-weapon-upgrades", 19, 20),
        })
        {
            var records = Records(context.Catalog(id)!);
            Assert.Equal(expectedImages, records.Count(record => record.AssetId is not null));
            Assert.Equal(expectedSynopses, records.Count(record => record.Description!.StartsWith("Menu synopsis:", StringComparison.Ordinal)));
            foreach (var record in records.Where(record => record.AssetId is not null))
                Assert.NotNull(context.GetAsset(record.AssetId!));
        }
        var armour = context.Catalog("rift-armor")!;
        Assert.Null(armour.Find("30")!.AssetId); // Emperor is an unconfirmed internal alias.
        Assert.Null(armour.Find("39")!.AssetId); // Reaver is an unconfirmed internal alias.
        Assert.Equal(armour.Find("6")!.AssetId, armour.Find("9")!.AssetId); // Carbonox previews share art.
        Assert.All(Records(context.Catalog("rift-weapon-upgrades")!), record =>
            Assert.Contains("not this save", record.Description));
    }

    [Fact]
    public void CoreQueriesRenderIconsAndReferenceUpgradePreviewsWithoutChangingTheSave()
    {
        var plugin = new InsomniacPlugin();
        var context = new ReferencePackStore([plugin]).Pin(plugin, "PPSA01474");
        var file = Assert.IsType<DiskSaveFile>(PlayerCorpusTests.Find("i29slot0-s-autosave", 0));
        using var document = plugin.Open(file, context);
        using var session = new DocumentSession("asset-view", file, plugin, document, references: context);
        var tabs = Assert.IsType<TabsNode>(session.BuildView());
        Assert.Contains(Assert.Single(tabs.Tabs, tab => tab.Title == "Upgrades").Children.OfType<QueryableTableNode>(),
            table => table.CollectionId == "arsenal/reference-upgrades" && table.Title.Contains("not saved level", StringComparison.Ordinal));
        var weapons = session.QueryCollection("arsenal/weapons", new() { Text = "Burst Pistol", Limit = 200 });
        Assert.NotEmpty(weapons.Rows);
        Assert.All(weapons.Rows, row => Assert.IsType<ImageNode>(row.Cells["icon"]));
        var armour = session.QueryCollection("armour/collection", new() { Limit = 200 });
        Assert.Equal(51, armour.Rows.Count(row => row.Cells["icon"] is ImageNode));
        var upgrades = session.QueryCollection("arsenal/reference-upgrades", new() { Limit = 200 });
        Assert.Equal(20, upgrades.Rows.Count);
        Assert.Equal(19, upgrades.Rows.Count(row => row.Cells["icon"] is ImageNode));
        Assert.All(upgrades.Rows, row => Assert.Contains("not this save", Assert.IsType<TextNode>(row.Cells["description"]).Text));
        Assert.Equal(file.ReadAllBytes(), Assert.Single(document.Serialize()).Content.ToArray());
    }

    [Fact]
    public void MissingImagesRetainAllGuideMarkersAndTheCoordinateOnlyFallback()
    {
        var plugin = new InsomniacPlugin();
        var context = new ReferencePackStore([plugin]).Pin(plugin, "PPSA01474");
        var lazy = new Dictionary<string, Func<IReadOnlyList<ViewNode>>>();
        var section = RiftGuideMaps.Build(context.Catalog(RiftGuideMaps.CatalogId), IReferenceContext.Empty, lazy)!;
        Assert.Equal(9, section.Children.Count);
        var count = 0;
        foreach (var region in section.Children.Cast<SectionNode>())
        {
            var map = Assert.IsType<MapNode>(Assert.Single(lazy[region.LazyId!]()));
            SpatialViewValidation.Validate(map);
            Assert.Null(map.Background);
            count += map.Markers.Count;
        }
        Assert.Equal(403, count);
    }

    [Fact]
    public void ReplacementMarkerCatalogueDoesNotReuseBackgroundsWithUnprovenAlignment()
    {
        var plugin = new InsomniacPlugin();
        var context = new ReferencePackStore([plugin]).Pin(plugin, "PPSA01474");
        var replacement = new ReplacementCatalog(context.Catalog(RiftGuideMaps.CatalogId)!);
        var lazy = new Dictionary<string, Func<IReadOnlyList<ViewNode>>>();
        var section = RiftGuideMaps.Build(replacement, context, lazy)!;
        Assert.Equal(9, section.Children.Count);
        foreach (var region in section.Children.Cast<SectionNode>())
            Assert.Null(Assert.IsType<MapNode>(Assert.Single(lazy[region.LazyId!]())).Background);
    }

    private static ReferenceRecord[] Records(IReferenceCatalog catalog) =>
        Enumerable.Range(0, (catalog.Count + ReferenceQuery.MaxLimit - 1) / ReferenceQuery.MaxLimit)
            .SelectMany(page => catalog.Search(new() { Offset = page * ReferenceQuery.MaxLimit, Limit = ReferenceQuery.MaxLimit }).Items)
            .ToArray();

    private sealed class ReplacementCatalog(IReferenceCatalog original) : IReferenceCatalog
    {
        public string Id => original.Id;
        public ReferencePackInfo Pack => original.Pack;
        public ReferenceDatasetInfo Dataset => original.Dataset with { PackId = "replacement.markers" };
        public int Count => original.Count;
        public IReadOnlyList<string> Categories => original.Categories;
        public ReferenceRecord? Find(string key) => original.Find(key) is { } record ? Replace(record) : null;
        public ReferencePage Search(ReferenceQuery query)
        {
            var page = original.Search(query);
            return page with { Items = page.Items.Select(Replace).ToArray() };
        }
        private ReferenceRecord Replace(ReferenceRecord record) => record with { Dataset = Dataset };
    }
}
