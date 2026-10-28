using System.Text;
using System.Text.Json.Nodes;
using SaveToolkit.Core.References;
using SaveToolkit.Core.Tests.Fakes;
using SaveToolkit.Sdk.References;

namespace SaveToolkit.Core.Tests.References;

public sealed class ReferencePackLoaderTests
{
    private static ReferencePackStore Store(ReferencePackLimits? limits = null) =>
        new([new ItemsPlugin()], new ReferencePackOptions { Limits = limits ?? new ReferencePackLimits() });

    private static string ImportError(PackBuilder pack, ReferencePackLimits? limits = null, string root = "") =>
        Assert.Throws<ReferencePackException>(() => Store(limits).Import(pack.Zip(root), "pack.zip")).Message;

    [Fact]
    public void EmbeddedPack_InPluginAssembly_LoadsWithAttribution()
    {
        var store = Store();

        Assert.Empty(store.LoadErrors);
        var pack = Assert.Single(store.Packs, p => p.Id == "test.items.en");
        Assert.Equal(ReferencePackOrigin.Embedded, pack.Info.Origin);
        Assert.Equal("en", pack.Info.Locale);
        var items = Assert.Single(pack.Info.Datasets, d => d.Id == "items");
        Assert.Equal(3, items.ItemCount);
        Assert.Equal("PS5 Save Game Toolkit contributors", items.Attribution.Author);
        Assert.Equal(new DateOnly(2026, 10, 28), items.Attribution.Retrieved);
        Assert.Equal(2, Assert.Single(pack.Info.Datasets, d => d.Id == "icons").ItemCount);
    }

    [Fact]
    public void ValidZip_Imports_WithQualifiedAssetIdsAndRecordProvenance()
    {
        var store = Store();

        var pack = store.Import(PackBuilder.WithItems().Zip(), "pack.zip");

        var catalog = pack.Catalog("items")!;
        var potion = catalog.Find("1001")!;
        Assert.Equal("User Potion", potion.Name);
        Assert.Equal(10, potion.Limit);
        Assert.Equal("user.items:icons/a.png", potion.AssetId);
        Assert.Equal("user.items", potion.Dataset.PackId);
        Assert.Equal("CC-BY-SA-4.0", potion.Dataset.Attribution.License);
        Assert.Contains("Example wiki contributors", potion.AttributionText, StringComparison.Ordinal);
        Assert.Null(catalog.Find("1001 "));
        Assert.Equal(["consumable", "armour"], catalog.Categories);
    }

    [Fact]
    public void ZipWithSingleTopLevelFolder_IsAccepted()
    {
        var pack = Store().Import(PackBuilder.WithItems().Zip("my-pack/"), "pack.zip");

        Assert.Equal(2, pack.Catalog("items")!.Count);
    }

    [Fact]
    public void RecordsHash_IgnoresCrLfLineEndings()
    {
        var lf = """{ "records": [ { "id": "1", "name": "A" } ] }""" + "\n";
        var crlf = lf.Replace("\n", "\r\n", StringComparison.Ordinal);
        var builder = new PackBuilder
        {
            Mutate = m => m["datasets"]![0]!["files"]![0]!["sha256"] = PackBuilder.Sha256(Encoding.UTF8.GetBytes(lf)),
        }.Records("items.json", crlf);

        Assert.Equal("A", Store().Import(builder.Zip(), "pack.zip").Catalog("items")!.Find("1")!.Name);
    }

    [Fact]
    public void HashMismatch_IsRejected()
    {
        var pack = PackBuilder.WithItems() with
        {
            Mutate = m => m["datasets"]![0]!["files"]![0]!["sha256"] = new string('0', 64),
        };

        Assert.Contains("hash mismatch", ImportError(pack), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("schema", "2", "schema must be 1")]
    [InlineData("id", "\"Bad Id\"", "id must be")]
    [InlineData("id", "\"trailing\\n\"", "id must be")]
    [InlineData("plugins", "[]", "plugins must list")]
    [InlineData("titleIds", "[\"nope\"]", "titleIds must look like")]
    [InlineData("locale", "\"english!\"", "locale must be")]
    [InlineData("licence", "\"typo\"", "licence")]
    public void InvalidManifest_IsRejectedWithReason(string property, string json, string expected)
    {
        var pack = PackBuilder.WithItems() with { Mutate = m => m[property] = JsonNode.Parse(json) };

        Assert.Contains(expected, ImportError(pack), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("sourceUrl", "javascript:alert(1)", "sourceUrl must be an http(s) URL")]
    [InlineData("licenseUrl", "", "licenseUrl must be an http(s) URL")]
    [InlineData("retrieved", "last week", "retrieved must be a date")]
    [InlineData("author", " ", "author is required")]
    public void IncompleteAttribution_IsRejected(string property, string value, string expected)
    {
        var pack = PackBuilder.WithItems() with { Mutate = m => m["datasets"]![0]!["attribution"]![property] = value };

        Assert.Contains(expected, ImportError(pack), StringComparison.Ordinal);
    }

    [Fact]
    public void NullEntries_AreRejectedNotCrashing()
    {
        var pack = PackBuilder.WithItems() with
        {
            Mutate = m =>
            {
                m["datasets"]!.AsArray().Add(null);
                m["datasets"]![0]!["files"]!.AsArray().Add(null);
            },
        };

        var error = ImportError(pack);

        Assert.Contains("datasets must not contain null entries", error, StringComparison.Ordinal);
        Assert.Contains("files must not contain null entries", error, StringComparison.Ordinal);
    }

    [Fact]
    public void MissingAttribution_IsRejected()
    {
        var pack = PackBuilder.WithItems() with { Mutate = m => m["datasets"]![1]!.AsObject().Remove("attribution") };

        Assert.Contains("attribution is required", ImportError(pack), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("../items.json")]
    [InlineData("/etc/items.json")]
    [InlineData("sub\\items.json")]
    [InlineData("C:/items.json")]
    [InlineData("a/./b.json")]
    public void UnsafePaths_AreRejected(string path)
    {
        var pack = PackBuilder.WithItems() with { Mutate = m => m["datasets"]![0]!["files"]![0]!["path"] = path };

        Assert.Contains("unsafe path", ImportError(pack), StringComparison.Ordinal);
    }

    [Fact]
    public void OversizedAsset_IsRejected()
    {
        var pack = PackBuilder.WithItems();

        Assert.Contains("larger than the limit", ImportError(pack, new ReferencePackLimits { MaxAssetBytes = 16 }), StringComparison.Ordinal);
    }

    [Fact]
    public void OversizedArchive_IsRejected()
    {
        var pack = PackBuilder.WithItems();

        Assert.Contains("larger than the limit", ImportError(pack, new ReferencePackLimits { MaxArchiveBytes = 100 }), StringComparison.Ordinal);
    }

    [Fact]
    public void ImageWithHugeDimensions_IsRejected()
    {
        var pack = new PackBuilder().Asset("icons/big.png", Images.Png(10_000, 10));

        Assert.Contains("10000x10", ImportError(pack), StringComparison.Ordinal);
    }

    [Fact]
    public void SvgDisguisedAsPng_IsRejected()
    {
        var pack = new PackBuilder().Asset("icons/evil.png", Images.Svg());

        Assert.Contains("not a PNG, JPEG or WebP", ImportError(pack), StringComparison.Ordinal);
    }

    [Fact]
    public void ImageWithWrongExtension_IsRejected()
    {
        var pack = new PackBuilder().Asset("icons/photo.png", Images.Jpeg(4, 4));

        Assert.Contains("extension", ImportError(pack), StringComparison.Ordinal);
    }

    [Fact]
    public void Jpeg_IsAccepted()
    {
        var pack = Store().Import(new PackBuilder().Asset("icons/photo.jpg", Images.Jpeg(4, 4)).Zip(), "pack.zip");

        Assert.Equal(1, pack.Info.Datasets.Single().ItemCount);
    }

    [Fact]
    public void RecordProblems_AreRejected()
    {
        var pack = new PackBuilder().Records("items.json", """
            { "records": [
              { "id": "1", "name": "A", "assetId": "icons/missing.png" },
              { "id": "5", "name": "E" },
              { "id": "5", "name": "F" },
              { "id": "2", "name": "" },
              { "id": "3", "name": "C", "source": "ftp://x" },
              { "id": "4", "name": "D", "limit": -1 }
            ] }
            """);

        var error = ImportError(pack);

        Assert.Contains("not an asset of this pack", error, StringComparison.Ordinal);
        Assert.Contains("duplicate id '5'", error, StringComparison.Ordinal);
        Assert.Contains("name is required", error, StringComparison.Ordinal);
        Assert.Contains("source must be", error, StringComparison.Ordinal);
        Assert.Contains("limit must not be negative", error, StringComparison.Ordinal);
    }

    [Fact]
    public void ListedButMissingFile_IsRejected()
    {
        var pack = PackBuilder.WithItems() with
        {
            Mutate = m => m["datasets"]![0]!["files"]!.AsArray().Add(new JsonObject { ["path"] = "more.json", ["sha256"] = new string('a', 64) }),
        };

        Assert.Contains("'more.json' is listed but missing", ImportError(pack), StringComparison.Ordinal);
    }

    [Fact]
    public void NotAZip_IsRejected()
    {
        var error = Assert.Throws<ReferencePackException>(() => Store().Import(Encoding.UTF8.GetBytes("hello"), "pack.zip")).Message;

        Assert.Contains("not a valid zip", error, StringComparison.Ordinal);
    }

    [Fact]
    public void ZipWithoutManifest_IsRejected()
    {
        var zip = TestZips.Build(("items.json", [1]));

        var error = Assert.Throws<ReferencePackException>(() => Store().Import(zip, "pack.zip")).Message;

        Assert.Contains("no pack.json", error, StringComparison.Ordinal);
    }
}
