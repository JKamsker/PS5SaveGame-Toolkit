using System.Security.Cryptography;
using System.Text.Json;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.BaldursGate3.Tests;

public sealed class GameAssetTests
{
    private const int ExpectedIconCount = 51;
    private const int ExpectedAssetBytes = 161_455;
    private const int AssetBudgetBytes = 4 * 1024 * 1024;
    private const string PackId = "bg3.starter.en";
    private const string ResourcePrefix = "refpacks/" + PackId + "/";

    [Fact]
    public void AllEmbeddedAssetsHaveValidHashesDetectedTypesProvenanceAndSharedCachedContent()
    {
        var (plugin, store, context) = ReferenceViewTests.References();
        Assert.Empty(store.LoadErrors); // Host validates every declared image header and dimensions too.
        var assembly = typeof(BaldursGate3Plugin).Assembly;
        Stream Resource(string path) => assembly.GetManifestResourceStream(assembly.GetManifestResourceNames()
            .Single(name => name.Replace('\\', '/') == ResourcePrefix + path))!;
        using var manifest = Resource("pack.json");
        using var json = JsonDocument.Parse(manifest);
        var files = json.RootElement.GetProperty("datasets").EnumerateArray()
            .Single(d => d.GetProperty("kind").GetString() == "assets").GetProperty("files").EnumerateArray().ToArray();
        Assert.Equal(ExpectedIconCount, files.Length);
        var hashes = new HashSet<string>(StringComparer.Ordinal);
        var declaredAssets = new HashSet<string>(StringComparer.Ordinal);
        var secondContext = store.Pin(plugin, "PPSA18463");
        var total = 0;
        foreach (var file in files)
        {
            var path = file.GetProperty("path").GetString()!;
            using var resource = Resource(path);
            using var buffer = new MemoryStream();
            resource.CopyTo(buffer);
            var bytes = buffer.ToArray();
            var hash = Convert.ToHexStringLower(SHA256.HashData(bytes));
            Assert.Equal(file.GetProperty("sha256").GetString(), hash);
            Assert.True(hashes.Add(hash), $"Duplicate image bytes: {path}");
            var id = PackId + ":" + path;
            declaredAssets.Add(id);
            var asset = context.GetAsset(id);
            Assert.NotNull(asset);
            Assert.Same(asset, secondContext.GetAsset(id)); // No per-document image loading/copying.
            Assert.Equal(bytes, asset.Content.ToArray());
            ReadOnlySpan<byte> pngSignature = [137, 80, 78, 71, 13, 10, 26, 10];
            var png = bytes.AsSpan().StartsWith(pngSignature);
            Assert.Equal(png ? "image/png" : "image/webp", asset.ContentType);
            Assert.EndsWith(png ? ".png" : ".webp", path, StringComparison.Ordinal);
            if (!png)
            {
                Assert.True(bytes.AsSpan().StartsWith("RIFF"u8));
                Assert.True(bytes.AsSpan(8, 4).SequenceEqual("WEBP"u8));
            }
            var provenance = context.AssetProvenance(id)!;
            Assert.Equal("bg3_game_icons", provenance.Id);
            Assert.Contains("Rights reserved by Larian Studios", provenance.Attribution.License, StringComparison.Ordinal);
            total += bytes.Length;
        }
        Assert.Equal(ExpectedAssetBytes, total);
        Assert.InRange(total, 1, AssetBudgetBytes);
        var referencedAssets = Bg3References.CatalogIds.SelectMany(id => context.Catalog(id)!
            .Search(new ReferenceQuery { Limit = ReferenceQuery.MaxLimit }).Items)
            .Select(record => record.AssetId).OfType<string>().ToHashSet(StringComparer.Ordinal);
        Assert.True(declaredAssets.SetEquals(referencedAssets)); // Every asset is used; every assetId resolves.
    }

    [Theory]
    [InlineData(Bg3References.Templates, "Potion of Healing")]
    [InlineData(Bg3References.Items, "Gold")]
    [InlineData(Bg3References.Equipment, "Longsword")]
    [InlineData(Bg3References.Spells, "Guidance")]
    [InlineData(Bg3References.Passives, "Civil Militia")]
    [InlineData(Bg3References.CampSupplies, "Banana")]
    public void CatalogTablesExposeGameIconsDescriptionsAndAttribution(string catalog, string name)
    {
        var (plugin, _, context) = ReferenceViewTests.References();
        var file = MemorySaveSet.SingleFile("test.lsv", PackageFixture.Create(payloads: [("SaveInfo.json", "{}"u8.ToArray())]));
        using var document = Assert.IsType<BaldursGate3Document>(plugin.Open(file, context));
        var row = Assert.Single(document.Query(catalog, new QueryableCollectionQuery { Text = name }).Rows);
        var icon = Assert.IsType<ImageNode>(row.Cells["icon"]);
        Assert.Contains("Larian Studios", icon.Alt, StringComparison.Ordinal);
        Assert.Contains("bg3.wiki", icon.Alt, StringComparison.Ordinal);
        Assert.NotNull(document.GetAsset(icon.AssetId));
        var details = Assert.IsType<SectionNode>(row.Cells["name"]);
        var description = string.Join(' ', details.Children.OfType<TextNode>().Select(node => node.Text));
        Assert.Contains("Game ", description, StringComparison.Ordinal);
        Assert.Contains("oldid=", description, StringComparison.Ordinal);
        Assert.Contains("Candidate label only", description, StringComparison.Ordinal);
        Assert.Equal(file.ReadAllBytes(), Assert.Single(document.Serialize()).Content.ToArray());
    }

    [Fact]
    public void SampleClassAndSpellViewsResolveExactKeysAndKeepUnknownStateAndOriginalBytes()
    {
        var (plugin, _, context) = ReferenceViewTests.References();
        var file = SampleLiveNewAgeTests.Save();
        using var document = Assert.IsType<BaldursGate3Document>(plugin.Open(file, context));
        var classes = document.Collections.Where(c => c.Id.EndsWith("/classes", StringComparison.Ordinal))
            .OrderByDescending(c => c.TotalCount).First();
        var rows = document.Query(classes.Id, new QueryableCollectionQuery { Limit = 200 }).Rows;
        Assert.Contains(rows, row => row.Cells["icon"] is ImageNode);
        foreach (var row in rows.Where(row => row.Cells["icon"] is ImageNode))
        {
            var field = Assert.IsType<FieldNode>(row.Cells["class"]);
            Assert.True(Assert.IsType<ReferenceSpec>(field.Spec).ReadOnly);
            Assert.Equal(Confidence.Unknown, field.Confidence);
            Assert.Equal(context.Catalog(Bg3References.Classes)!.Find(field.Value.AsString())!.AssetId,
                Assert.IsType<ImageNode>(row.Cells["icon"]).AssetId);
        }
        var books = document.Collections.Where(c => c.Id.EndsWith("/books", StringComparison.Ordinal))
            .OrderByDescending(c => c.TotalCount).First();
        var spellIcons = new List<ImageNode>();
        foreach (var row in document.Query(books.Id, new QueryableCollectionQuery { Limit = 200 }).Rows)
        {
            var lazy = Assert.IsType<SectionNode>(row.Cells["details"]).LazyId!;
            var view = document.ExpandNode(lazy);
            var graph = Assert.Single(view.OfType<GraphNode>());
            SpatialViewValidation.Validate(graph);
            Assert.All(graph.Items, item => Assert.Equal(GraphState.Unknown, item.State));
            spellIcons.AddRange(view.OfType<ImageNode>());
        }
        Assert.NotEmpty(spellIcons);
        Assert.All(spellIcons, icon => Assert.NotNull(document.GetAsset(icon.AssetId)));
        Assert.Equal(file.ReadAllBytes(), Assert.Single(document.Serialize()).Content.ToArray());
        Assert.Null(context.Catalog(Bg3References.Spells)!.Find("Shout_Bless"));
        Assert.NotNull(context.Catalog(Bg3References.Spells)!.Find("Target_Bless")!.AssetId);
    }
}
