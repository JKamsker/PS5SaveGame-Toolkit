using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SaveToolkit.Core.References;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.EldenRing.Tests;

public sealed class VisualAssetTests
{
    [Fact]
    public void EveryEmbeddedAssetHasAHashDetectedWebPTypeAndBoundedDimensionsWithinTotalBudget()
    {
        var plugin = new EldenRingPlugin();
        var store = new ReferencePackStore([plugin]);
        Assert.Empty(store.LoadErrors);
        var context = store.Pin(plugin, "PPSA03169");
        using var manifest = JsonDocument.Parse(Resource("pack.json"));
        long total = 0;
        var iconHashes = new HashSet<string>(StringComparer.Ordinal);
        foreach (var dataset in manifest.RootElement.GetProperty("datasets").EnumerateArray()
            .Where(dataset => dataset.GetProperty("kind").GetString() == "assets"))
        {
            Assert.Equal(ProgressMapAssets.Rights, dataset.GetProperty("attribution").GetProperty("license").GetString());
            foreach (var file in dataset.GetProperty("files").EnumerateArray())
            {
                var path = file.GetProperty("path").GetString()!;
                var bytes = Resource(path);
                total += bytes.Length;
                var hash = Convert.ToHexStringLower(SHA256.HashData(bytes));
                Assert.Equal(file.GetProperty("sha256").GetString(), hash);
                Assert.Equal("RIFF", Encoding.ASCII.GetString(bytes, 0, 4));
                Assert.Equal("WEBP", Encoding.ASCII.GetString(bytes, 8, 4));
                Assert.Equal(bytes.Length - 8, BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(4)));
                var dimensions = Dimensions(bytes);
                var icon = path.StartsWith("icons/", StringComparison.Ordinal);
                Assert.InRange(dimensions.Width, 1, icon ? 64 : 2048);
                Assert.InRange(dimensions.Height, 1, icon ? 64 : 2048);
                Assert.InRange(bytes.Length, 1, icon ? 20_000 : 600_000);
                if (icon) Assert.True(iconHashes.Add(hash), "Duplicate icon was not deduplicated.");
                var id = ProgressMapAssets.PackId + ":" + path;
                var asset = Assert.IsType<AssetContent>(context.GetAsset(id));
                Assert.Equal("image/webp", asset.ContentType);
                Assert.Equal(bytes, asset.Content.ToArray());
                Assert.Equal(ProgressMapAssets.Rights, context.AssetProvenance(id)!.Attribution.License);
                Assert.Same(asset, context.GetAsset(id)); // No repeated image decoding/copying in the plugin.
            }
        }
        Assert.Equal(1096, iconHashes.Count);
        Assert.Equal(2_640_798, total);
        Assert.InRange(total, 1, 4_000_000);
    }

    [Fact]
    public void SelectedCatalogRecordsExposeOfficialTextAndResolvableIconsWithoutChangingIdentityOrLimits()
    {
        var plugin = new EldenRingPlugin();
        var references = new ReferencePackStore([plugin]).Pin(plugin, "PPSA03169");
        var catalog = references.Catalog(ReferenceBrowser.CatalogId)!;
        var records = CommunityReferenceTests.Read(catalog);
        var illustrated = records.Where(record => record.AssetId is not null).ToArray();
        Assert.Equal(1245, illustrated.Length);
        foreach (var record in illustrated)
        {
            Assert.NotNull(references.GetAsset(record.AssetId!));
            Assert.Contains("Inferred on PS5", record.Description);
            Assert.Contains("FromSoftware / Bandai Namco", record.AttributionText);
            Assert.Contains(ProgressMapAssets.Source, record.AttributionText);
            Assert.Null(record.Limit);
        }
        Assert.Equal(437, illustrated.Count(record => record.Category == "weapon"));
        Assert.Equal(593, illustrated.Count(record => record.Category == "goods"));
        Assert.Equal(115, illustrated.Count(record => record.Category == "talisman"));
        Assert.Equal(9, illustrated.Count(record => record.Category == "armor"));
        Assert.Equal(91, illustrated.Count(record => record.Category == "ash"));
        var rowa = catalog.Find("0x400050F0")!;
        Assert.Equal("Rowa Fruit", rowa.Name);
        Assert.Contains("Easily found everywhere in the Lands Between", rowa.Description);
        Assert.NotNull(catalog.Find("0x200003E8")!.AssetId); // Crimson Amber Medallion.
        Assert.Null(catalog.Find("goods:upstream:999999999")!.AssetId);
    }

    [Fact]
    public void SampleInventoryAndCatalogRichCellsShowBundledIconsAndKeepExactBytes()
    {
        var file = WriteTestSupport.Sample();
        var original = file.ReadAllBytes();
        var plugin = new EldenRingPlugin();
        var context = new ReferencePackStore([plugin]).Pin(plugin, file.Directory.TitleId);
        using var document = plugin.Open(file, context);
        var query = (IQueryableDocument)document;
        foreach (var name in new[] { "Rowa Fruit", "Scimitar", "Smithing Stone [1]", "Cracked Pot", "Map: Limgrave- West" })
        {
            var rows = query.Query("slots/0/inventory/held", new() { Text = name }).Rows;
            Assert.NotEmpty(rows);
            foreach (var row in rows)
            {
                var details = Assert.IsType<SectionNode>(row.Cells["details"]);
                var icon = Assert.Single(details.Children.OfType<ImageNode>());
                Assert.NotNull(document.GetAsset(icon.AssetId));
                Assert.Contains(details.Children.OfType<TextNode>(), node => node.Text.Contains("FromSoftware / Bandai Namco", StringComparison.Ordinal));
            }
        }
        var catalogRow = Assert.Single(query.Query(ReferenceBrowser.CollectionId, new()
            { Filters = new Dictionary<string, string> { ["name"] = "Rowa Fruit" } }).Rows);
        Assert.Single(Assert.IsType<SectionNode>(catalogRow.Cells["details"]).Children.OfType<ImageNode>());
        WriteTestSupport.EqualBytes(original, document);
    }

    [Theory]
    // Independently calculated from pinned marker/world-conversion rows (not generated map-positions.json).
    [InlineData("76100", "M00", 3666.6060, 7215.0180)]
    [InlineData("10000800", "M00", 3097.36, 6460.4)]
    public void NativeMasterCoordinatesRetainKnownSourceAnchors(string eventId, string layer, double x, double y)
    {
        var context = CommunityReferenceTests.Context();
        var row = context.Catalog(ProgressMapAssets.CatalogId)!.Find(eventId)!;
        Assert.Equal(layer, row.Attributes["layer"]);
        Assert.Equal(x, double.Parse(row.Attributes["x"], System.Globalization.CultureInfo.InvariantCulture), 3);
        Assert.Equal(y, double.Parse(row.Attributes["y"], System.Globalization.CultureInfo.InvariantCulture), 3);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MissingArtOrCoordinatesRetainAccessibleCandidatesWithoutInventingStatus(bool coordinatesAvailable)
    {
        var context = CommunityReferenceTests.Context();
        var source = context.Catalog(ProgressReferences.CatalogId)!;
        var entry = ProgressReferences.Parse(source.Find("grace:ChurchofElleh")!)!;
        var region = new ProgressRegion("limgrave", "Limgrave", [entry]);
        var fallback = new MapNode("Limgrave", new(0, 0, 1, 1),
            [new("grace", entry.Reference.Name, MapMarkerStatus.Unknown, "grace")], "Source candidate; status Unknown.");
        var maps = new ProgressMapAssets(new WithoutArt(context, coordinatesAvailable));
        var result = maps.Decorate(fallback, region);
        SpatialViewValidation.Validate(result);
        Assert.Null(result.Background);
        Assert.Equal(coordinatesAvailable, Assert.Single(result.Markers).X.HasValue);
        Assert.Equal(MapMarkerStatus.Unknown, result.Markers[0].Status);
        if (!coordinatesAvailable) Assert.Same(fallback, result);
    }

    internal static void ValidateMap(MapNode map, IReferenceContext references)
    {
        SpatialViewValidation.Validate(map);
        foreach (var marker in map.Markers)
        {
            Assert.Equal(marker.X.HasValue, marker.Y.HasValue);
            if (!marker.X.HasValue) continue;
            Assert.InRange(marker.X.Value, 0, ProgressMapAssets.NativeSize);
            Assert.InRange(marker.Y!.Value, 0, ProgressMapAssets.NativeSize);
            Assert.Contains("Placement: Inferred", marker.Description);
        }
        if (map.CoordinateEmbeddingPermitted)
        {
            Assert.Equal(ProgressMapAssets.Rights, map.CoordinateAttribution!.License);
            var background = Assert.IsType<MapBackground>(map.Background);
            Assert.True(background.EmbeddingPermitted);
            Assert.NotNull(references.GetAsset(background.AssetId));
            Assert.Equal(ProgressMapAssets.Rights, background.Attribution.License);
            Assert.Contains("Inferred", map.TextAlternative);
        }
        else
        {
            Assert.Null(map.Background);
            Assert.All(map.Markers, marker => Assert.Null(marker.X));
        }
    }

    [Theory]
    [InlineData("x", "NaN")]
    [InlineData("x", "Infinity")]
    [InlineData("x", "-1")]
    [InlineData("y", "10497")]
    [InlineData("layer", "M11")]
    [InlineData("projection", "guessed")]
    public void MalformedOrUnsupportedPlacementKeepsTheUnpositionedFallback(string key, string value)
    {
        var context = CommunityReferenceTests.Context();
        var source = context.Catalog(ProgressMapAssets.CatalogId)!;
        var record = source.Find("76100")!;
        var attributes = record.Attributes.ToDictionary();
        attributes[key] = value;
        var invalid = new ProgressTestCatalog(source, [record with { Attributes = attributes }]);
        var entry = ProgressReferences.Parse(context.Catalog(ProgressReferences.CatalogId)!.Find("grace:ChurchofElleh")!)!;
        var fallback = new MapNode("Region", new(0, 0, 1, 1),
            [new("grace", "Church of Elleh", MapMarkerStatus.Unknown, "grace")], "Unpositioned source candidate.");
        var map = new ProgressMapAssets(new WithoutArt(context, true, invalid)).Decorate(fallback, new("region", "Region", [entry]));
        Assert.Same(fallback, map);
        SpatialViewValidation.Validate(map);
    }

    private static byte[] Resource(string path)
    {
        var assembly = typeof(EldenRingPlugin).Assembly;
        var name = Assert.Single(assembly.GetManifestResourceNames(), name =>
            name.Replace('\\', '/') == "refpacks/" + ProgressMapAssets.PackId + "/" + path);
        using var stream = assembly.GetManifestResourceStream(name)!;
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return memory.ToArray();
    }

    private static (int Width, int Height) Dimensions(byte[] bytes)
    {
        var kind = Encoding.ASCII.GetString(bytes, 12, 4);
        if (kind == "VP8X") return (1 + UInt24(bytes, 24), 1 + UInt24(bytes, 27));
        Assert.Equal("VP8 ", kind);
        Assert.Equal(new byte[] { 0x9d, 0x01, 0x2a }, bytes[23..26]);
        return (BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(26)) & 0x3fff,
            BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(28)) & 0x3fff);
    }

    private static int UInt24(byte[] bytes, int offset) => bytes[offset] | bytes[offset + 1] << 8 | bytes[offset + 2] << 16;

    private sealed class WithoutArt(IReferenceContext context, bool coordinatesAvailable, IReferenceCatalog? positions = null) : IReferenceContext
    {
        public IReadOnlyList<ReferencePackInfo> Packs => context.Packs;
        public IReferenceCatalog? Catalog(string id, string? gameBuild = null) =>
            coordinatesAvailable ? positions ?? context.Catalog(id, gameBuild) : null;
        public AssetContent? GetAsset(string assetId) => null;
        public ReferenceDatasetInfo? AssetProvenance(string assetId) => context.AssetProvenance(assetId);
    }
}
