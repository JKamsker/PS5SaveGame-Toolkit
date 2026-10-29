using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SaveToolkit.Core.References;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Core.Storage;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.Cyberpunk2077.Tests;

public sealed class GameAssetPackTests
{
    private const string PackId = CyberpunkReferences.GameAssetPackId;
    private const int ImageBytes = 584_946;

    [Fact]
    public void EveryFileMatchesItsHashAndEveryIconResolvesWithSeparateArtAttribution()
    {
        var (plugin, _, context) = ReferencePackFixture.Embedded();
        using var manifest = JsonDocument.Parse(Resource("pack.json"));
        var total = 0;
        foreach (var dataset in manifest.RootElement.GetProperty("datasets").EnumerateArray())
        {
            foreach (var file in dataset.GetProperty("files").EnumerateArray())
            {
                var path = file.GetProperty("path").GetString()!;
                var bytes = Resource(path);
                if (dataset.GetProperty("kind").GetString() == "records")
                    bytes = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(bytes).Replace("\r\n", "\n", StringComparison.Ordinal));
                Assert.Equal(file.GetProperty("sha256").GetString(), Convert.ToHexStringLower(SHA256.HashData(bytes)));
                if (!path.EndsWith(".webp", StringComparison.Ordinal)) continue;
                total += bytes.Length;
                var asset = context.GetAsset(PackId + ":" + path)!;
                Assert.Equal("image/webp", asset.ContentType); // Host validates type and dimensions on load.
                Assert.Equal("RIFF", Encoding.ASCII.GetString(bytes, 0, 4));
                Assert.Equal("WEBP", Encoding.ASCII.GetString(bytes, 8, 4));
            }
        }
        Assert.Equal(ImageBytes, total);
        Assert.True(total < 4_000_000);
        var catalog = context.Catalog("item-art", "2210")!;
        var records = ReferencePackFixture.All(catalog).ToArray();
        Assert.Equal(146, records.Length);
        Assert.Equal(16, records.Select(record => record.AssetId).Distinct().Count());
        Assert.Equal(92, records.Count(record => record.Category == "Cyberware"));
        Assert.Equal("CC-BY-SA-3.0", catalog.Dataset.Attribution.License);
        foreach (var record in records)
        {
            Assert.Equal(ItemNames.TweakHash(record.Attributes["internalName"]).ToString(CultureInfo.InvariantCulture), record.Key);
            Assert.False(string.IsNullOrWhiteSpace(record.Description));
            Assert.Contains(record.Source!, plugin.Info.References);
            Assert.NotNull(context.GetAsset(record.AssetId!));
            Assert.Contains("Rights reserved by CD PROJEKT RED", context.AssetProvenance(record.AssetId!)!.Attribution.License);
            Assert.Null(record.Limit);
        }
    }

    [Fact]
    public void CorruptedEmbeddedImageIsRejectedByHostHashValidation()
    {
        using var buffer = new MemoryStream();
        var assembly = typeof(Cyberpunk2077Plugin).Assembly;
        var prefix = "refpacks/" + PackId + "/";
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var name in assembly.GetManifestResourceNames().Select(name => name.Replace('\\', '/'))
                .Where(name => name.StartsWith(prefix, StringComparison.Ordinal)))
            {
                var path = name[prefix.Length..];
                var bytes = Resource(path);
                if (path == "maps/night-city.webp") bytes[^1] ^= 1;
                using var target = zip.CreateEntry(path).Open();
                target.Write(bytes);
            }
        }
        var store = new ReferencePackStore([]);
        var error = Assert.Throws<ReferencePackException>(() => store.Import(buffer.ToArray(), "corrupt-art.zip"));
        Assert.Contains("maps/night-city.webp' hash mismatch", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void OfficialPlannerTextUsesExactPerkEnumsWithoutClaimingBuildRules()
    {
        var (_, _, context) = ReferencePackFixture.Embedded();
        var catalog = context.Catalog("perks", "2210")!;
        Assert.Equal(193, catalog.Count);
        Assert.Empty(catalog.Pack.GameBuilds);
        var painkiller = catalog.Find("Body_Central_Milestone_1")!;
        Assert.Equal("Painkiller", painkiller.Name);
        Assert.Equal("Unlocks slow Health Regen in combat.", painkiller.Description);
        Assert.Contains("not PS5 2.21 rules", catalog.Dataset.Attribution.Modifications);
        Assert.All(ReferencePackFixture.All(catalog), record =>
        {
            Assert.False(string.IsNullOrWhiteSpace(record.Description));
            Assert.DoesNotContain("<0>", record.Description);
            Assert.Null(record.Limit);
        });
    }

    [Fact]
    public void AssetsAreSharedAcrossPinnedDocumentsAndLocalItemPacksOwnTheirPresentation()
    {
        var (plugin, store, context) = ReferencePackFixture.Embedded();
        Assert.Same(context.GetAsset(CyberpunkReferences.MapAssetId), store.Pin(plugin, "PPSA04027").GetAsset(CyberpunkReferences.MapAssetId));
        var id = ItemNames.TweakHash("Items.FirstAidWhiffV0");
        store.Import(ReferencePackFixture.UserPack("items", [new { id = CyberpunkReferences.ItemKey(id), name = "Local name" }]), "local.zip");
        var local = new CyberpunkReferences(store.Pin(plugin, "PPSA04027"), 2210);
        Assert.Null(local.ItemArt(id));
        Assert.Null(local.ItemIcon(id));
        Assert.Equal("Local name", local.ItemDisplayName(id));
    }

    [Fact]
    public void SampleInventoryAndCyberwarePublishIconsAndMapRetainsUnpositionedListAndNoOpBytes()
    {
        var file = ProgressionSampleTests.Files().OrderByDescending(file => file.Length).First();
        var (plugin, _, context) = ReferencePackFixture.Embedded();
        using var document = plugin.Open(file, context);
        using var session = new DocumentSession("assets", (DiskSaveFile)file, plugin, document, references: context);
        _ = session.BuildView();
        var inventory = session.QueryCollection("inventory", new QueryableCollectionQuery { Text = "MaxDoc" });
        var row = Assert.Single(inventory.Rows);
        var image = Assert.IsType<ImageNode>(row.Cells["icon"]);
        Assert.NotNull(document.GetAsset(image.AssetId));
        var field = Assert.IsType<FieldNode>(row.Cells["id"]);
        Assert.Contains("Rights reserved by CD PROJEKT RED", field.Description);
        Assert.Contains("CC-BY-SA-3.0", field.Description);
        var identity = Assert.IsType<SectionNode>(row.Cells["details"]).Children.OfType<FieldNode>().First();
        var candidates = session.SearchReferences(identity.ValueId, new ReferenceQuery { Text = "MaxDoc" });
        Assert.Equal(image.AssetId, Assert.Single(candidates.Items, candidate => candidate.RawId == identity.Value.AsString()).AssetId);
        var cyberware = session.QueryCollection("cyberware", new QueryableCollectionQuery { Text = "Second Heart" });
        Assert.NotEmpty(cyberware.Rows);
        Assert.All(cyberware.Rows, item => Assert.IsType<ImageNode>(item.Cells["icon"]));
        Assert.All(cyberware.Rows, item => Assert.Single(Assert.IsType<SectionNode>(item.Cells["name"]).Children.OfType<ImageNode>()));
        var map = Assert.IsType<MapNode>(session.Expand("travel-map/0")[0]);
        SpatialViewValidation.Validate(map);
        Assert.Equal(CyberpunkReferences.MapAssetId, map.Background!.AssetId);
        Assert.True(map.Background.EmbeddingPermitted);
        Assert.Contains("No verified saved travel-ID crosswalk", map.Background.Attribution.Modifications);
        Assert.All(map.Markers, marker => { Assert.Null(marker.X); Assert.Null(marker.Y); });
        Assert.False(session.State.IsDirty);
        Assert.Equal(file.ReadAllBytes(), Assert.Single(document.Serialize()).Content.ToArray());
    }

    [Fact]
    public void MissingPackKeepsRawIdentifiersAndOmitsArt()
    {
        var references = new CyberpunkReferences(IReferenceContext.Empty, 2210);
        Assert.Null(references.ItemIcon(ItemNames.TweakHash("Items.FirstAidWhiffV0")));
        Assert.Null(references.MapBackground());
        Assert.Null(references.Catalog("perks"));
    }

    [Fact]
    public void PerkTextPreservesSavedIdentityRankUnknownStateAndNavigation()
    {
        var (_, _, context) = ReferencePackFixture.Embedded();
        var references = new CyberpunkReferences(context, 2210);
        var stored = new InspectionRow("saved-perk", new Dictionary<string, string>
        {
            ["kind"] = "Modern perk", ["group"] = "BodyAttributeData", ["name"] = "Body_Central_Milestone_1",
            ["rank"] = "2", ["status"] = "Stored rank; effects unverified",
        });
        var enriched = references.EnrichPerk(stored);
        Assert.Equal(SpatialDetails.PerkId(stored), SpatialDetails.PerkId(enriched));
        Assert.Equal("2", enriched.Values["rank"]);
        var graph = new PerkGraphInspection([enriched], null);
        var nodes = graph.Expand("perk-graph/0", (id, label, spec, value) => new FieldNode(id, label, spec, SaveToolkit.Sdk.Values.FieldValue.Of(value)));
        var item = Assert.Single(Assert.IsType<GraphNode>(nodes[0]).Items);
        Assert.Equal("Painkiller", item.Label);
        Assert.Equal(GraphState.Unknown, item.State);
        Assert.Contains("Unlocks slow Health Regen", item.Description);
        Assert.Contains("CD PROJEKT RED", item.Description);
        Assert.Equal(SpatialDetails.PerkId(stored), item.Target!.Id);
        var legacy = stored with { Values = new Dictionary<string, string>(stored.Values) { ["kind"] = "Legacy perk" } };
        Assert.Same(legacy, references.EnrichPerk(legacy));
    }

    private static byte[] Resource(string path)
    {
        var assembly = typeof(Cyberpunk2077Plugin).Assembly;
        var name = assembly.GetManifestResourceNames().Single(name => name.Replace('\\', '/') == "refpacks/" + PackId + "/" + path);
        using var stream = assembly.GetManifestResourceStream(name)!;
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }
}
