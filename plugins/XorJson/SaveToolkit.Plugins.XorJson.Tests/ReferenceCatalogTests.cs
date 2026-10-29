using SaveToolkit.Core.References;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.XorJson.Tests;

public sealed class ReferenceCatalogTests
{
    [Fact]
    public void PackSearchAndPickerPreserveAttributionAndRejectUnknownReplacements()
    {
        var plugin = new XorJsonPlugin();
        var store = new ReferencePackStore([plugin]);
        Assert.Empty(store.LoadErrors);
        var references = store.Pin(plugin, "PPSA16064");
        Assert.Single(references.Packs);
        var catalog = references.Catalog("xorjson-items")!;
        Assert.Equal(328, catalog.Count);
        Assert.Equal(60, references.Catalog("xorjson-effects")!.Count);
        Assert.Equal(1346, references.Catalog("xorjson-world")!.Count);
        Assert.Equal(30, references.Catalog("xorjson-enchantments")!.Count);
        var sword = catalog.Find("SW.Item.Sword")!;
        Assert.NotNull(sword);
        Assert.Equal(GameAssetTests.OwnerDecision, sword.Dataset.Attribution.License);
        Assert.Contains("84ffee67b73e49d5b888e31257ce9767aa8ab826", sword.Attributes["identifierSource"]);
        Assert.NotNull(sword.AssetId);
        Assert.Null(sword.Limit); // No unproven PC limits become PS5 validators.
        var service = new ReferenceFieldService(references);
        var spec = new ReferenceSpec("xorjson-items");
        var page = service.Search(spec, new ReferenceQuery { Text = "sword", Limit = 2 }, revision: 7);
        Assert.Equal(7, page.Revision);
        Assert.InRange(page.Items.Count, 1, 2);
        Assert.All(page.Items, candidate => Assert.Equal(GameAssetTests.OwnerDecision, candidate.Dataset!.Attribution.License));
        Assert.True(service.Validate(FieldValue.Of("SW.Item.Sword"), spec).Ok);
        Assert.False(service.Validate(FieldValue.Of("SW.Item.NotInCatalog"), spec).Ok);
        Assert.False(service.Find(spec, "SW.Item.NotInCatalog").Eligibility.Selectable);
        Assert.NotEmpty(catalog.Categories);
        var category = catalog.Search(new ReferenceQuery { Category = sword.Category });
        Assert.All(category.Items, item => Assert.Equal(sword.Category, item.Category));
    }

    [Fact]
    public void ExpandedQuestTaskAndEnchantmentCatalogsCarryPinnedProvenanceWithoutGameplayLimits()
    {
        var plugin = new XorJsonPlugin();
        var references = new ReferencePackStore([plugin]).Pin(plugin, "PPSA16064");
        var world = references.Catalog("xorjson-world")!;
        var quest = world.Find("CA00")!;
        Assert.Equal("Escape from Squid Coast", quest.Name);
        var task = world.Find("CA00_E01")!;
        Assert.Equal("Get back to camp", task.Name);
        Assert.Equal("Task", task.Category);
        Assert.Equal("CA00", task.Attributes["parentQuest"]);
        var enchantment = references.Catalog("xorjson-enchantments")!.Find("SW.Enchantment.Arcane")!;
        Assert.Equal("Artifact Amplifier", enchantment.Name);
        foreach (var record in new[] { quest, task, enchantment })
        {
            Assert.Equal(record == enchantment ? GameAssetTests.OwnerDecision : "MIT", record.Dataset.Attribution.License);
            Assert.Contains("84ffee67b73e49d5b888e31257ce9767aa8ab826",
                record == enchantment ? record.Attributes["identifierSource"] : record.Source!);
            Assert.Contains("PS5 applicability unverified", record.Attributes["confidence"]);
            Assert.Null(record.Limit);
            Assert.Equal(record == enchantment, record.AssetId is not null);
            Assert.DoesNotContain("strength", record.Attributes.Keys);
        }
    }

    [Fact]
    public void ObservedInventoryUsesAttributedExperimentalReferencesAndUnknownIdsStayVisible()
    {
        var plugin = new XorJsonPlugin();
        var store = new ReferencePackStore([plugin]);
        var references = store.Pin(plugin, "PPSA16064");
        var file = SampleSaves.Load("PPSA16064").AllDiskFiles.Single(f => !f.IsSystemFile && f.Directory.Name.StartsWith("Character", StringComparison.Ordinal));
        using var document = plugin.Open(file, references);
        var items = XorJsonTests.Fields(document).Where(f => f.Label == "TypeTag" && f.Description!.Contains("member(\"Inventory\",0)", StringComparison.Ordinal)).ToArray();
        Assert.Equal(5, items.Length);
        Assert.All(items, item =>
        {
            Assert.Equal("xorjson-items", Assert.IsType<ReferenceSpec>(item.Spec).CatalogId);
            Assert.False(item.Spec.ReadOnly);
            Assert.Equal(Confidence.Inferred, item.Confidence);
            Assert.Contains("MIT", item.Description!);
            Assert.Contains("PC catalog; PS5 semantics unverified", item.Description!);
        });
        var sections = StructuralTests.Nodes(document).OfType<SectionNode>().Where(s => s.Description?.Contains("Candidate identity", StringComparison.Ordinal) == true).ToArray();
        Assert.Equal(5, sections.Length);
        Assert.Equal(5, sections.Select(s => s.LazyId).Distinct().Count());

        var unknownFile = MemorySaveSet.SingleFile("unknown.sav", XorJsonTests.Encode("{\"TypeTag\":\"SW.Item.FutureUnknown\"}"), "PPSA16064");
        using var unknown = plugin.Open(unknownFile, references);
        var field = Assert.Single(XorJsonTests.Fields(unknown));
        Assert.IsType<ReferenceSpec>(field.Spec);
        Assert.Equal("SW.Item.FutureUnknown", field.Value.AsString());
        Assert.Equal(unknownFile.ReadAllBytes(), unknown.Serialize()[0].Content.ToArray());
    }

    [Fact]
    public void GenericObfuscatedItemFieldsUseTheNativeSearchablePicker()
    {
        var plugin = new XorJsonPlugin();
        var store = new ReferencePackStore([plugin]);
        var references = store.Pin(plugin, "PPSA16064");
        var file = MemorySaveSet.SingleFile("synthetic.sav", XorJsonTests.Encode("{\"TypeTag\":\"SW.Item.Sword\",\"unknown\":-0.00E+0}"), "PPSA16064");
        using var document = plugin.Open(file, references);
        var field = XorJsonTests.Fields(document).Single(f => f.Label == "TypeTag");
        var spec = Assert.IsType<ReferenceSpec>(field.Spec);
        Assert.False(spec.ReadOnly);
        Assert.False(spec.AllowCustom);
        var next = FieldValue.Of("SW.Item.Bow");
        Assert.True(new ReferenceFieldService(references).Validate(next, spec).Ok);
        Assert.True(document.ApplyEdits([new(field.ValueId, next)]).Succeeded);
        Assert.Equal(XorJsonTests.Encode("{\"TypeTag\":\"SW.Item.Bow\",\"unknown\":-0.00E+0}"), document.Serialize()[0].Content.ToArray());
    }
}
