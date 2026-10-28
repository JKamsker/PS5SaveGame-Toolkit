using System.Globalization;
using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.Cyberpunk2077.Tests;

public sealed class WikiLabelTests
{
    [Fact]
    public void WikiLabelsHaveExactTechnicalJoinsPinnedRevisionsAndShareAlikeAttribution()
    {
        var (plugin, _, context) = ReferencePackFixture.Embedded();
        var labels = Assert.IsAssignableFrom<IReferenceCatalog>(context.Catalog("item-labels", "2210"));
        Assert.Equal(15, labels.Count);
        Assert.Equal("en", labels.Pack.Locale);
        Assert.Equal("CC-BY-SA-3.0", labels.Dataset.Attribution.License);
        foreach (var record in ReferencePackFixture.All(labels))
        {
            Assert.Equal(ItemNames.TweakHash(record.Attributes["internalName"]).ToString(CultureInfo.InvariantCulture), record.Key);
            Assert.Contains("?oldid=", record.Source);
            Assert.Contains(record.Source!, plugin.Info.References);
            Assert.Contains("Cyberpunk Wiki contributors", record.AttributionText);
            Assert.Null(record.Limit);
            Assert.Null(record.AssetId);
            Assert.NotNull(context.Catalog("items", "2210")!.Find(record.Key));
        }
        var references = new CyberpunkReferences(context, 2210);
        Assert.Equal("MaxDoc", references.ItemDisplayName(ItemNames.TweakHash("Items.FirstAidWhiffV0")));
        Assert.Equal("Items.Preset_V_Unity_Cutscene", references.ItemDisplayName(ItemNames.TweakHash("Items.Preset_V_Unity_Cutscene")));
        Assert.Null(references.ItemLabel(ItemNames.TweakHash("Items.Preset_V_Unity_Cutscene"))); // No speculative variant joins.
    }

    [Fact]
    public void ExactBuildLocalItemNamesOverrideEmbeddedWikiLabels()
    {
        var (plugin, store, _) = ReferencePackFixture.Embedded();
        var id = ItemNames.TweakHash("Items.FirstAidWhiffV0");
        store.Import(ReferencePackFixture.UserPack("items", [new { id = CyberpunkReferences.ItemKey(id), name = "Synthetic same-build localized name" }]), "local.zip");
        var references = new CyberpunkReferences(store.Pin(plugin, "PPSA04027"), 2210);
        Assert.Equal("Synthetic same-build localized name", references.ItemDisplayName(id));
        Assert.Null(references.ItemLabel(id));
    }

    [Fact]
    public void SampleFriendlyNamesCarryBothSourcesAndNeverEnableExtendedWrites()
    {
        var (plugin, _, context) = ReferencePackFixture.Embedded();
        using var document = plugin.Open(ProgressionSampleTests.Files()[0], context);
        _ = document.BuildView();
        var page = Assert.IsAssignableFrom<SaveToolkit.Sdk.Collections.IQueryableDocument>(document).Query("inventory", new() { Text = "MaxDoc" });
        var field = Assert.IsType<FieldNode>(Assert.Single(page.Rows).Cells["id"]);
        Assert.Equal("MaxDoc", field.Value.AsString());
        Assert.True(field.Spec.ReadOnly);
        Assert.Contains("MIT", field.Description);
        Assert.Contains("CC-BY-SA-3.0", field.Description);
        var all = CyberpunkSampleTests.ExpandAll(document);
        Assert.DoesNotContain(all, f => f.ValueId.Contains("/record", StringComparison.Ordinal) && !f.Spec.ReadOnly);
        Assert.DoesNotContain(all, f => f.Spec is ReferenceSpec && !f.Spec.ReadOnly);
    }
}
