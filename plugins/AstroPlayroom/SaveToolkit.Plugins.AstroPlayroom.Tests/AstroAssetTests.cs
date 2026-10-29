using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.AstroPlayroom.Tests;

public sealed class AstroAssetTests
{
    private const string PackId = "astro.catalogue.en";
    private const string MapAsset = PackId + ":maps/world-selection.webp";
    private const int AssetBytes = 155296;

    [Fact]
    public void EveryEmbeddedAssetHasMatchingHashWebpHeaderBoundedDimensionsAndAttribution()
    {
        var (plugin, _, context) = AstroCatalogueTestData.References();
        var assembly = typeof(AstroPlayroomPlugin).Assembly;
        var resource = Assert.Single(assembly.GetManifestResourceNames(), name =>
            name.Replace('\\', '/') == "refpacks/astro.catalogue.en/pack.json");
        using var stream = assembly.GetManifestResourceStream(resource);
        using var manifest = JsonDocument.Parse(Assert.IsAssignableFrom<Stream>(stream));
        var total = 0;
        var count = 0;
        var hashes = new HashSet<string>(StringComparer.Ordinal);
        foreach (var dataset in manifest.RootElement.GetProperty("datasets").EnumerateArray())
        {
            if (dataset.GetProperty("kind").GetString() != "assets") continue;
            foreach (var file in dataset.GetProperty("files").EnumerateArray())
            {
                var path = file.GetProperty("path").GetString()!;
                var id = PackId + ":" + path;
                var asset = Assert.IsType<AssetContent>(context.GetAsset(id));
                Assert.Equal("image/webp", asset.ContentType);
                var bytes = asset.Content.ToArray();
                Assert.Equal("RIFF", Encoding.ASCII.GetString(bytes, 0, 4));
                Assert.Equal("WEBPVP8 ", Encoding.ASCII.GetString(bytes, 8, 8));
                var width = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(26)) & 0x3fff;
                var height = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(28)) & 0x3fff;
                Assert.InRange(width, 1, path.StartsWith("maps/", StringComparison.Ordinal) ? 2048 : 96);
                Assert.InRange(height, 1, path.StartsWith("maps/", StringComparison.Ordinal) ? 2048 : 96);
                Assert.InRange(bytes.Length, 1, path.StartsWith("maps/", StringComparison.Ordinal) ? 600_000 : 20_000);
                var hash = Convert.ToHexStringLower(SHA256.HashData(bytes));
                Assert.Equal(file.GetProperty("sha256").GetString(), hash);
                Assert.True(hashes.Add(hash), "Identical images should share one asset.");
                var attribution = context.AssetProvenance(id)!.Attribution;
                Assert.Contains("Rights reserved by Sony (SIE)", attribution.License, StringComparison.Ordinal);
                Assert.Contains("2026-10-29", attribution.License, StringComparison.Ordinal);
                Assert.Contains(attribution.SourceUrl, plugin.Info.References);
                total += bytes.Length;
                count++;
            }
        }
        Assert.Equal(52, count);
        Assert.Equal(AssetBytes, total);
        Assert.InRange(total, 1, 4_000_000);
    }

    [Fact]
    public void CatalogImagesResolveAndOfficialCaptionsRetainSeparatePublisherProvenance()
    {
        var (_, _, context) = AstroCatalogueTestData.References();
        var catalog = context.Catalog("astro_collectibles")!;
        var records = catalog.Search(new ReferenceQuery { Limit = ReferenceQuery.MaxLimit }).Items;
        Assert.Equal(146, records.Count(record => record.AssetId is not null));
        Assert.All(records.Where(record => record.AssetId is not null), record => Assert.NotNull(context.GetAsset(record.AssetId!)));
        Assert.All(records.Where(record => record.Key.StartsWith("Prop/", StringComparison.Ordinal)), record => Assert.Null(record.AssetId));
        var puzzleImages = records.Where(record => record.Key.StartsWith("Puzzle/", StringComparison.Ordinal)).Select(record => record.AssetId).Distinct();
        Assert.Equal(PackId + ":icons/puzzle.webp", Assert.Single(puzzleImages));
        var captions = context.Catalog("astro_captions")!;
        Assert.Equal(49, captions.Count);
        Assert.Null(captions.Find("Artifact/ps1_mouse"));
        Assert.Equal("Awww, chibi console!", captions.Find("Artifact/psone")!.Description);
        Assert.All(captions.Search(new ReferenceQuery { Limit = ReferenceQuery.MaxLimit }).Items, caption =>
        {
            Assert.NotNull(catalog.Find(caption.Key));
            Assert.NotEmpty(caption.Description!);
            Assert.Equal("www.powerpyx.com", new Uri(caption.Source!).Host);
            Assert.Contains("Sony Interactive Entertainment", caption.AttributionText, StringComparison.Ordinal);
        });
        Assert.Equal("CC-BY-SA-3.0", catalog.Dataset.Attribution.License);
    }

    [Fact]
    public void EagerAndPagedTablesDisplayImagesCaptionsAndCacheAssetsWithoutChangingSaveBytes()
    {
        var (plugin, _, context) = AstroCatalogueTestData.References();
        foreach (var file in AstroTestData.Samples())
        {
            using var document = plugin.Open(file, context);
            Assert.All(AstroCatalogueTestData.Table(document, "Artifacts").Rows, row => CheckImage(row, document));
            Assert.All(AstroCatalogueTestData.Table(document, "Puzzle pieces").Rows, row => CheckImage(row, document));
            var page = Assert.IsAssignableFrom<IQueryableDocument>(document).Query("astro.collectibles", new() { Limit = 200 });
            Assert.Equal(146, page.Rows.Count(row => row.Cells["image"] is ImageNode));
            var psone = AstroCatalogueTestData.Row(document, "Artifacts", "Artifact/psone");
            Assert.Contains("Official in-game caption: Awww, chibi console!", Assert.IsType<FieldNode>(psone.Cells["name"]).Description, StringComparison.Ordinal);
            Assert.Equal(file.ReadAllBytes(), AstroTestData.Output(document));
        }

        void CheckImage(TableRow row, ISaveDocument document)
        {
            var image = Assert.IsType<ImageNode>(row.Cells["image"]);
            Assert.Contains("Sony Interactive Entertainment", image.Alt, StringComparison.Ordinal);
            Assert.Same(context.GetAsset(image.AssetId), document.GetAsset(image.AssetId));
        }
    }

    [Fact]
    public void ArtworkUsesExplicitCropTransformAndSaveFlagsWithSchematicAndRawFallbacks()
    {
        var (plugin, _, context) = AstroCatalogueTestData.References();
        foreach (var file in AstroTestData.Samples())
        {
            using var document = plugin.Open(file, context);
            var map = Assert.Single(document.ExpandNode("astro-map:Artwork:0").OfType<MapNode>());
            SpatialViewValidation.Validate(map);
            Assert.Equal(MapAsset, map.Background!.AssetId);
            Assert.True(map.Background.EmbeddingPermitted);
            Assert.Equal(new MapBounds(0, 0, 1140, 930), map.Bounds);
            Assert.Equal(new MapTransform(1, 1, -40, -50), map.Transform);
            Assert.Contains("Inferred", map.CoordinateAttribution!.Modifications, StringComparison.Ordinal);
            Assert.Contains("static", map.TextAlternative, StringComparison.Ordinal);
            Assert.Contains(map.Markers, marker => marker.X is null);
            Assert.Contains(map.Markers, marker => marker.X is not null);
            var beach = Assert.Single(map.Markers, marker => marker.Label == "Bot Beach: IsVisited");
            Assert.Equal(608, beach.X);
            Assert.Equal(350, beach.Y);
            Assert.All(map.Markers, marker => Assert.Equal(document.GetValue(marker.Target!.Id).AsBool(), marker.Status == MapMarkerStatus.Found));
            var schematic = Assert.Single(document.ExpandNode("astro-map:World:0").OfType<MapNode>());
            Assert.Null(schematic.Background);
            Assert.Equal(schematic.Markers.Select(marker => marker.Id), map.Markers.Select(marker => marker.Id));
            Assert.Equal(file.ReadAllBytes(), AstroTestData.Output(document));
            using var missing = plugin.Open(file);
            Assert.Null(Assert.Single(missing.ExpandNode("astro-map:Artwork:0").OfType<MapNode>()).Background);
            Assert.Null(missing.GetAsset(MapAsset));
        }
    }

    [Fact]
    public void UnknownSchemaOmitsArtworkEvenWhenThePackIsAvailable()
    {
        var (plugin, _, context) = AstroCatalogueTestData.References();
        Assert.NotNull(context.GetAsset(MapAsset));
        var bytes = AstroTestData.Bytes("\r\n<PreInstall><World><CPU_PLAZA><IsVisited>false</IsVisited>" +
            "<IsCleared>true</IsCleared></CPU_PLAZA></World></PreInstall>opaque");
        using var unknown = plugin.Open(AstroTestData.File(bytes), context);
        var map = Assert.Single(unknown.ExpandNode("astro-map:Artwork:0").OfType<MapNode>());
        SpatialViewValidation.Validate(map);
        Assert.Null(map.Background);
        Assert.Equal("Levels — original schematic", map.Title);
        Assert.Equal(new[] { MapMarkerStatus.Missing, MapMarkerStatus.Found }, map.Markers.Select(marker => marker.Status));
        Assert.All(map.Markers, marker =>
        {
            Assert.Equal(Confidence.Unknown, marker.Confidence);
            Assert.Equal(ViewTargetKind.Field, marker.Target!.Kind);
            Assert.Equal(unknown.GetValue(marker.Target.Id).AsBool(), marker.Status == MapMarkerStatus.Found);
        });
        Assert.Equal(bytes, AstroTestData.Output(unknown));
    }
}
