using SaveToolkit.Core.References;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.UnrealGvas.Tests;

public sealed class GvasCataloguePolicyTests
{
    [Theory]
    [InlineData("generic", "UNKNOWN", "unrealgvas_property_types")]
    [InlineData("sackboy", "PPSA01288", "unrealgvas_property_types,unrealgvas_sackboy_fields,unrealgvas_sackboy_costumes")]
    [InlineData("images", "PPSA01288", "unrealgvas_property_types,unrealgvas_sackboy_image_fields")]
    [InlineData("jedi", "PPSA02199", "unrealgvas_property_types,unrealgvas_jedi_actor_fields")]
    [InlineData("settings", "PPSA02199", "unrealgvas_property_types,unrealgvas_jedi_settings")]
    [InlineData("hogwarts", "PPSA01603", "unrealgvas_property_types")]
    public void InspectionCatalogueOrderAndNamedCaptionSourcesRemainDistinct(string title, string titleId, string expected)
    {
        byte[] bytes = title switch
        {
            "images" => GvasFixture.Build(writer => GvasFixture.Property(writer, "SurfaceWidth", "IntProperty",
                GvasFixture.Body(body => body.Write(64))), minor: 25, patch: 1, packageVersion: 518,
                saveClass: "/Script/GingerBread.GingerbreadSaveImageSet"),
            "settings" => TitleFixture.Settings(),
            "hogwarts" => HogwartsInventoryFixture.Create().ReadAllBytes(),
            _ => TitleFixture.Scalars(title),
        };
        var plugin = new UnrealGvasPlugin();
        var references = new ReferencePackStore([plugin]).Pin(plugin, titleId);
        using var document = plugin.Open(MemorySaveSet.SingleFile("save", bytes, titleId), references);
        var root = Assert.IsType<TabsNode>(document.BuildView());
        var raw = title == "hogwarts"
            ? Assert.IsType<TabsNode>(Assert.Single(root.Tabs.Single(tab => tab.Title == "GVAS properties").Children)) : root;
        var catalogues = raw.Tabs.Single(tab => tab.Title == "Catalogues").Children;
        Assert.Equal(expected.Split(','), catalogues.OfType<FieldNode>().Select(field => Assert.IsType<ReferenceSpec>(field.Spec).CatalogId));
        Assert.Equal(expected.Split(',').Select(id => "unrealgvas/catalogue/inspect/" + id), catalogues.OfType<ActionNode>().Select(action => action.ActionId));
        Assert.All(catalogues.OfType<FieldNode>(), field => Assert.True(field.Spec.ReadOnly));
        if (title != "hogwarts")
        {
            var page = Assert.IsAssignableFrom<IQueryableDocument>(document).Query("gvas/properties", new() { Limit = 200 });
            string name = title switch { "sackboy" => "CollectabellsStored", "images" => "SurfaceWidth", "settings" => "HDRBrightness", _ => "Count" };
            string? catalogId = title switch { "sackboy" => "unrealgvas_sackboy_fields", "settings" => "unrealgvas_jedi_settings", _ => null };
            var row = page.Rows.Single(row => ((TextNode)row.Cells["path"]).Text == "/" + name);
            Assert.Equal(catalogId is null ? name : references.Catalog(catalogId)!.Find(name)!.Name, ((TextNode)row.Cells["name"]).Text);
            Assert.Equal("property/catalogue/" + expected.Split(',')[0], catalogues.OfType<FieldNode>().First().ValueId);
        }
        Assert.Equal(bytes, Assert.Single(document.Serialize()).Content.ToArray());
    }

    [Theory]
    [InlineData("cRc")]
    [InlineData("someCHECKSUMvalue")]
    [InlineData("Signature")]
    public void GenericIntegrityNamesProtectTheEntireDocument(string name)
    {
        byte[] bytes = GvasFixture.Build(writer =>
        {
            GvasFixture.Property(writer, "Count", "IntProperty", GvasFixture.Body(body => body.Write(17)));
            GvasFixture.Property(writer, name, "UInt32Property", [1, 2, 3, 4]);
        });
        using var document = new UnrealGvasPlugin().Open(MemorySaveSet.SingleFile("save", bytes));
        Assert.False(document.CanEdit);
        Assert.All(GvasFormatTests.Expand(document), field =>
        {
            Assert.True(field.Spec.ReadOnly);
            Assert.Equal(Confidence.Verified, field.Confidence);
            Assert.Equal("This save contains a game-specific integrity field. Its coverage is unknown, so the entire document is read-only.", field.Warning);
        });
        Assert.Equal(bytes, Assert.Single(document.Serialize()).Content.ToArray());
    }
}
