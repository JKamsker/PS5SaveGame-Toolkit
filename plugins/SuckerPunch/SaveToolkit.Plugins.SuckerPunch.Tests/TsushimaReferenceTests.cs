using SaveToolkit.Core.References;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;
using SaveToolkit.Testing;
using static SaveToolkit.Plugins.SuckerPunch.Tests.SuckerPunchTestData;

namespace SaveToolkit.Plugins.SuckerPunch.Tests;

public sealed class TsushimaReferenceTests
{
    [Theory]
    [InlineData("suckerpunch_items", 48)]
    [InlineData("suckerpunch_equipment", 122)]
    [InlineData("suckerpunch_techniques", 6)]
    [InlineData("suckerpunch_resource_guide", 12)]
    [InlineData("suckerpunch_charm_guide", 79)]
    [InlineData("suckerpunch_armor_guide", 15)]
    [InlineData("suckerpunch_sword_guide", 41)]
    [InlineData("suckerpunch_technique_guide", 18)]
    [InlineData("suckerpunch_world_guide", 17)]
    public void EveryDatasetHasPinnedAttributionAndExplicitMappingConfidence(string id, int count)
    {
        var plugin = new SuckerPunchPlugin();
        var context = new ReferencePackStore([plugin]).Pin(plugin, TsushimaTitle);
        var catalog = Assert.IsAssignableFrom<IReferenceCatalog>(context.Catalog(id));
        Assert.Equal(count, catalog.Count);
        Assert.Equal("CC-BY-SA-3.0", catalog.Dataset.Attribution.License);
        Assert.Equal("https://creativecommons.org/licenses/by-sa/3.0/", catalog.Dataset.Attribution.LicenseUrl);
        Assert.Contains("oldid=", catalog.Dataset.Attribution.SourceUrl);
        Assert.Contains(catalog.Dataset.Attribution.SourceUrl, plugin.Info.References);
        foreach (var record in catalog.Search(new ReferenceQuery { Limit = 200 }).Items)
        {
            Assert.False(string.IsNullOrWhiteSpace(record.Description));
            Assert.False(string.IsNullOrWhiteSpace(record.Category));
            Assert.True(record.Attributes["mappingConfidence"] is "Unknown" or "Inferred");
            Assert.Null(record.Limit); // Wiki capacities are not a save/build-specific carrying-capacity contract.
            if (record.Key.StartsWith("wiki:", StringComparison.Ordinal) || record.Name != record.Key)
            {
                Assert.Contains("oldid=", record.Source);
                Assert.Contains(record.Source!, plugin.Info.References);
            }
        }
    }

    [Theory]
    [InlineData("suckerpunch_items", "IngredCoreOre")]
    [InlineData("suckerpunch_items", "Hide")]
    [InlineData("suckerpunch_items", "IngredUpgradeWaxTree")]
    [InlineData("suckerpunch_equipment", "AccMinorCharm01")]
    [InlineData("suckerpunch_equipment", "ArmorAcc1")]
    [InlineData("suckerpunch_equipment", "ArmorChest")]
    [InlineData("suckerpunch_equipment", "SwordKatanaYarikawa")]
    [InlineData("suckerpunch_techniques", "UltimateBrutalStance")]
    public void AmbiguousIdsAreNeverAssignedWikiNames(string catalogId, string rawId)
    {
        var plugin = new SuckerPunchPlugin();
        var context = new ReferencePackStore([plugin]).Pin(plugin, TsushimaTitle);
        var record = Assert.IsType<ReferenceRecord>(context.Catalog(catalogId)!.Find(rawId));
        Assert.Equal(rawId, record.Name);
        Assert.Equal("Unknown", record.Attributes["mappingConfidence"]);
    }

    [Fact]
    public void FriendlyLabelsPreserveRawBindingsAndKeepOwnershipUnknownOnEverySample()
    {
        var plugin = new SuckerPunchPlugin();
        var store = new ReferencePackStore([plugin]);
        foreach (var file in SampleSaves.Load(TsushimaTitle).AllDiskFiles.Where(file => plugin.Probe(file).IsMatch))
        {
            using var document = plugin.Open(file, store.Pin(plugin, TsushimaTitle));
            var root = Assert.IsType<TabsNode>(document.BuildView());
            var inventory = Inventory(document);
            var bamboo = Assert.IsType<FieldNode>(inventory.Rows.Single(row => row.Key == "IngredCoreBamboo").Cells["item"]);
            Assert.Equal("Bamboo", bamboo.Label);
            Assert.Equal("IngredCoreBamboo", bamboo.Value.AsString());
            Assert.Equal(Confidence.Inferred, bamboo.Confidence);
            Assert.True(bamboo.Spec.ReadOnly);
            Assert.Contains("oldid=29221", bamboo.Description);
            Assert.Contains("CC-BY-SA-3.0", bamboo.Description);
            var equipment = Assert.IsType<TableNode>(root.Tabs.Single(tab => tab.Title == "Equipment").Children[1]);
            var storm = Assert.IsType<FieldNode>(equipment.Rows.Single(row => row.Key == "SwordKatanaSakaiStorm").Cells["reference"]);
            Assert.Equal("The Storm of Clan Sakai", storm.Label);
            Assert.Equal("SwordKatanaSakaiStorm", document.GetValue(storm.ValueId).AsString());
            Assert.Equal(Confidence.Inferred, storm.Confidence);
            Assert.All(equipment.Rows, row => Assert.Equal("Unknown", Assert.IsType<TextNode>(row.Cells["state"]).Text));
            Assert.Equal(file.ReadAllBytes(), Assert.Single(document.Serialize()).Content.ToArray());
        }
    }

    [Theory]
    [InlineData("suckerpunch_resource_guide", 12)]
    [InlineData("suckerpunch_charm_guide", 79)]
    [InlineData("suckerpunch_armor_guide", 15)]
    [InlineData("suckerpunch_sword_guide", 41)]
    [InlineData("suckerpunch_technique_guide", 18)]
    [InlineData("suckerpunch_world_guide", 17)]
    public void LazyGuidesShowAttributionAndUnknownSaveStateWithoutCreatingBindings(string catalogId, int count)
    {
        var plugin = new SuckerPunchPlugin();
        var file = Slot("suckerpunchUser1");
        using var document = plugin.Open(file, new ReferencePackStore([plugin]).Pin(plugin, TsushimaTitle));
        _ = document.BuildView();
        var nodes = document.ExpandNode("guide/" + catalogId);
        var table = Assert.IsType<TableNode>(nodes[1]);
        Assert.Equal(count, table.Rows.Count);
        Assert.All(table.Rows, row =>
        {
            Assert.StartsWith("wiki:", row.Key);
            Assert.Contains("Unknown", Assert.IsType<TextNode>(row.Cells["state"]).Text);
            var attribution = Assert.IsType<TextNode>(row.Cells["source"]).Text;
            Assert.Contains("oldid=", attribution);
            Assert.Contains("CC-BY-SA-3.0", attribution);
            Assert.All(row.Cells.Values, cell => Assert.IsType<TextNode>(cell));
        });
        Assert.Equal(file.ReadAllBytes(), Assert.Single(document.Serialize()).Content.ToArray());
    }

    [Fact]
    public void WorldReferenceScopesIkiSeparatelyAndNeverReportsCompletion()
    {
        var plugin = new SuckerPunchPlugin();
        var catalog = new ReferencePackStore([plugin]).Pin(plugin, TsushimaTitle).Catalog("suckerpunch_world_guide")!;
        var rows = catalog.Search(new ReferenceQuery { Limit = 200 }).Items;
        Assert.Equal(7, rows.Count(row => row.Category == "Izuhara"));
        Assert.Equal(6, rows.Count(row => row.Category == "Toyotama"));
        Assert.Equal(3, rows.Count(row => row.Category == "Kamiagata"));
        var iki = Assert.Single(rows, row => row.Category == "Iki");
        Assert.Equal("Nakajima Shrine", iki.Name);
        Assert.Equal("Iki expansion", iki.Attributes["contentScope"]);
        Assert.All(rows, row => Assert.Equal("Unknown", row.Attributes["mappingConfidence"]));
    }

    [Fact]
    public void CuratedGuidesRetainSourceCategoriesAndExcludeYoteiEntries()
    {
        var plugin = new SuckerPunchPlugin();
        var context = new ReferencePackStore([plugin]).Pin(plugin, TsushimaTitle);
        var kits = context.Catalog("suckerpunch_sword_guide")!;
        var stonecutter = kits.Find("wiki:sword-kit:the-stonecutter")!;
        Assert.Equal("Iki", stonecutter.Attributes["region"]);
        Assert.Equal("pillars of honor", stonecutter.Category);
        Assert.Equal("default", kits.Find("wiki:sword-kit:the-storm-of-clan-sakai")!.Category);
        Assert.Null(kits.Find("wiki:sword-kit:lone-wolf"));
        var charms = context.Catalog("suckerpunch_charm_guide")!;
        Assert.Equal("major", charms.Find("wiki:charm:charm-of-inari")!.Attributes["charmSize"]);
        Assert.Equal("minor", charms.Find("wiki:charm:charm-of-inari-s-might")!.Attributes["charmSize"]);
        Assert.Contains("Honor Buried Deep", charms.Find("wiki:charm:charm-of-broken-barriers")!.Description);
        Assert.Null(charms.Find("wiki:charm:charm-of-futsunushi"));
        Assert.Null(new ReferencePackStore([plugin]).Pin(plugin, "PPSA26344").Catalog("suckerpunch_charm_guide"));
    }

    [Fact]
    public void MissingPacksRetainRawIdsAndRejectUnknownGuideIds()
    {
        using var document = Open(Slot("suckerpunchUser1"));
        var field = Assert.IsType<FieldNode>(Inventory(document).Rows.Single(row => row.Key == "IngredCoreBamboo").Cells["item"]);
        Assert.Equal("IngredCoreBamboo", field.Label);
        Assert.Equal(Confidence.Unknown, field.Confidence);
        Assert.IsType<TextNode>(Assert.Single(document.ExpandNode("guide/suckerpunch_charm_guide")));
        Assert.Throws<KeyNotFoundException>(() => document.ExpandNode("guide/unknown"));
        Assert.False(document.ApplyEdits([new ValueEdit(field.ValueId, FieldValue.Of("wiki:resource:bamboo"))]).Succeeded);
    }
}
