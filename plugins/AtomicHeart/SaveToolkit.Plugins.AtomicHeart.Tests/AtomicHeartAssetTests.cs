using System.Security.Cryptography;
using System.Text.Json;
using SaveToolkit.Core.References;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.AtomicHeart.Tests;

public sealed class AtomicHeartAssetTests
{
    private const int IconCount = 23;
    private const int AssetBytes = 12624;
    private const string PackId = "atomic-heart.items.en";
    private static readonly string[] CatalogIds = ["items", "guide", "skills"];

    [Fact]
    public void EveryIconResolvesWithValidHashDetectedTypeAndRightsWithinBudget()
    {
        var plugin = new AtomicHeartPlugin();
        var store = new ReferencePackStore([plugin]);
        Assert.Empty(store.LoadErrors);
        var context = store.Pin(plugin, "PPSA10695");
        var assembly = typeof(AtomicHeartPlugin).Assembly;
        var manifestName = Assert.Single(assembly.GetManifestResourceNames(),
            name => name.Replace('\\', '/') == $"refpacks/{PackId}/pack.json");
        using var stream = assembly.GetManifestResourceStream(manifestName);
        Assert.NotNull(stream);
        using var manifest = JsonDocument.Parse(stream);
        var hashes = new HashSet<string>(StringComparer.Ordinal);
        var totalBytes = 0;
        foreach (var dataset in manifest.RootElement.GetProperty("datasets").EnumerateArray()
                     .Where(d => d.GetProperty("kind").GetString() == "assets"))
        foreach (var file in dataset.GetProperty("files").EnumerateArray())
        {
            var id = PackId + ":" + file.GetProperty("path").GetString();
            var asset = context.GetAsset(id);
            Assert.NotNull(asset);
            Assert.Same(asset, context.GetAsset(id)); // Host caches immutable assets; no per-view reload.
            var hash = Convert.ToHexStringLower(SHA256.HashData(asset.Content.Span));
            Assert.Equal(file.GetProperty("sha256").GetString(), hash);
            Assert.True(hashes.Add(hash), "Duplicate image bytes should be deduplicated.");
            var png = asset.Content.Span.StartsWith(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
            if (!png)
            {
                Assert.True(asset.Content.Span[..4].SequenceEqual("RIFF"u8));
                Assert.True(asset.Content.Span.Slice(8, 4).SequenceEqual("WEBP"u8));
            }
            Assert.Equal(png ? "image/png" : "image/webp", asset.ContentType);
            var provenance = context.AssetProvenance(id);
            Assert.NotNull(provenance);
            Assert.Contains("owner decision 2026-10-29", provenance.Attribution.License, StringComparison.Ordinal);
            totalBytes += asset.Content.Length;
        }
        Assert.Equal(IconCount, hashes.Count);
        Assert.Equal(AssetBytes, totalBytes);
        Assert.InRange(totalBytes, 1, 4_000_000);
        var records = CatalogIds.SelectMany(id =>
            context.Catalog(id)!.Search(new ReferenceQuery { Limit = 200 }).Items).ToArray();
        var illustrated = records.Where(record => record.AssetId is not null).ToArray();
        Assert.Equal(IconCount, illustrated.Length);
        Assert.All(illustrated, record => Assert.NotNull(context.GetAsset(record.AssetId!)));
        Assert.All(records, record => Assert.Null(record.Limit));
    }

    [Fact]
    public void SampleIconsFollowLiveWeaponReplacementAndReferenceOnlyJoinsKeepOriginalBounds()
    {
        var file = WriteTestData.Sample();
        using var named = WriteTestData.Plugin.Open(file, WriteTestData.References());
        using var raw = WriteTestData.Plugin.Open(file);
        var rows = WriteTestData.Table(named).Rows;
        var rawRows = WriteTestData.Table(raw).Rows.ToDictionary(row => row.Key);
        foreach (var id in new[] { "DA_Item_Resources_Metal_Parts", "DA_Item_Synthetic_Material_Resource", "DA_Consumable_aid_small" })
        {
            var matches = rows.Where(row => WriteTestData.Field(row, "asset").Value.AsString() == id).ToArray();
            Assert.NotEmpty(matches);
            foreach (var row in matches)
            {
                var asset = WriteTestData.Field(row, "asset");
                Assert.True(asset.Spec.ReadOnly);
                Assert.IsType<ImageNode>(row.Cells["icon"]);
                Assert.False(named.ApplyEdits([new(asset.ValueId, asset.Value)]).Succeeded);
                Assert.Equal(WriteTestData.Field(rawRows[row.Key], "amount").Spec, WriteTestData.Field(row, "amount").Spec);
            }
        }
        var swede = Assert.Single(rows, row => WriteTestData.Field(row, "asset").Value.AsString() == "DA_Item_Shved");
        var originalIcon = Assert.IsType<ImageNode>(swede.Cells["icon"]);
        Assert.NotNull(named.GetAsset(originalIcon.AssetId));
        var reference = WriteTestData.Field(swede, "asset");
        Assert.True(named.ApplyEdits([new(reference.ValueId, FieldValue.Of("DA_Item_ShotgunKS23"))]).Succeeded);
        var changed = ((IQueryableDocument)named).Query(InventoryCollection.Id, new() { Text = "KS-23", Limit = 200 });
        var replacementIcon = Assert.IsType<ImageNode>(changed.Rows.Single(row => row.Key == swede.Key).Cells["icon"]);
        Assert.NotEqual(originalIcon.AssetId, replacementIcon.AssetId);
        Assert.Contains("Slimao", replacementIcon.Alt, StringComparison.Ordinal);
        Assert.True(named.ApplyEdits([new(reference.ValueId, reference.Value)]).Succeeded);
        Assert.Equal(file.ReadAllBytes(), WriteTestData.Output(named));
    }

    [Fact]
    public void ReferenceGuideShowsSourceSpecificCostsWithoutPretendingToKnowSavedState()
    {
        using var document = WriteTestData.Plugin.Open(WriteTestData.Sample(), WriteTestData.References());
        var modules = document.ExpandNode("references/guide/0").OfType<TableNode>().Single();
        var gasVent = modules.Rows.Single(row => row.Key == "reference/module/ks23/additional-gas-vent");
        Assert.Contains("19 Metal Parts", Assert.IsType<TextNode>(gasVent.Cells["facts"]).Text, StringComparison.Ordinal);
        Assert.Contains("level 1", Assert.IsType<TextNode>(gasVent.Cells["description"]).Text, StringComparison.Ordinal);
        var cartridge = modules.Rows.Single(row => row.Key == "reference/module/ks23/cartridge-module");
        Assert.Contains("25 Metal Parts; 12 Synthetic Material; 3 Chemistry",
            Assert.IsType<TextNode>(cartridge.Cells["facts"]).Text, StringComparison.Ordinal);
        var skills = document.ExpandNode("references/skills/0").OfType<TableNode>().Single();
        Assert.Equal(9, skills.Rows.Count);
        Assert.All(skills.Rows, row => {
            Assert.StartsWith("reference/shok/", row.Key, StringComparison.Ordinal);
            Assert.Contains("no native save join", Assert.IsType<TextNode>(row.Cells["key"]).Text, StringComparison.Ordinal);
            Assert.Contains("GameRiv", Assert.IsType<TextNode>(row.Cells["source"]).Text, StringComparison.Ordinal);
        });
        var electrization = skills.Rows.Single(row => row.Key == "reference/shok/electrization");
        Assert.Contains("32 Neuropolymer", Assert.IsType<TextNode>(electrization.Cells["facts"]).Text, StringComparison.Ordinal);
        var saved = Assert.IsType<GraphNode>(Assert.Single(document.ExpandNode("spatial/skills/0")));
        Assert.All(saved.Items, item => { Assert.Equal(GraphState.Unknown, item.State); Assert.Null(item.Cost); });
        Assert.Empty(saved.Edges);
        var map = document.ExpandNode("spatial/map/0").OfType<MapNode>().Single();
        Assert.Null(map.Background);
        SpatialViewValidation.Validate(map);
        Assert.Equal(WriteTestData.Sample().ReadAllBytes(), WriteTestData.Output(document));
    }

    [Fact]
    public void MissingPacksRetainFallbacksAndMalformedReferencePagesAreRejected()
    {
        using var document = WriteTestData.Plugin.Open(WriteTestData.Sample());
        foreach (var id in CatalogIds)
            Assert.Contains("No pinned reference pack", Assert.IsType<TextNode>(Assert.Single(document.ExpandNode($"references/{id}/0"))).Text, StringComparison.Ordinal);
        Assert.Null(document.GetAsset(PackId + ":icons/swede.webp"));
        Assert.All(WriteTestData.Table(document).Rows, row => Assert.IsType<TextNode>(row.Cells["icon"]));
        foreach (var id in new[] { "references/missing/0", "references/items/-1", "references/items/no", "references/items/2147483648" })
            Assert.Throws<KeyNotFoundException>(() => document.ExpandNode(id));
        using var named = WriteTestData.Plugin.Open(WriteTestData.Sample(), WriteTestData.References());
        Assert.Throws<KeyNotFoundException>(() => named.ExpandNode("references/items/48"));
    }
}
