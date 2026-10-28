using SaveToolkit.Core.References;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.AstroPlayroom.Tests;

internal static class AstroCatalogueTestData
{
    public const string Fingerprint = "e412d790cf868baf5dd17d332fd3e025032588a34eb569e1a85487f98e55eb40";

    public static (AstroPlayroomPlugin Plugin, ReferencePackStore Store, ReferenceContext Context) References()
    {
        var plugin = new AstroPlayroomPlugin();
        var store = new ReferencePackStore([plugin]);
        Assert.Empty(store.LoadErrors);
        return (plugin, store, store.Pin(plugin, "PPSA01325"));
    }

    public static Tab Catalogue(ISaveDocument document) =>
        Assert.Single(Assert.IsType<TabsNode>(document.BuildView()).Tabs, tab => tab.Title == "Collectibles");

    public static SectionNode Group(ISaveDocument document, string title) =>
        Assert.Single(Catalogue(document).Children.OfType<SectionNode>(), node => node.Title == title);

    public static TableNode Table(ISaveDocument document, string title) =>
        Assert.Single(Group(document, title).Children.OfType<TableNode>());

    public static TableRow Row(ISaveDocument document, string title, string key) =>
        Assert.Single(Table(document, title).Rows, row => Assert.IsType<TextNode>(row.Cells["key"]).Text == key);

    public static string SchemaHash(ISaveDocument document)
    {
        var tab = Assert.Single(Assert.IsType<TabsNode>(document.BuildView()).Tabs, tab => tab.Title == "Schema");
        var report = Assert.Single(tab.Children.OfType<SectionNode>());
        return Assert.IsType<TextNode>(report.Children[0]).Text.Split(": ")[1];
    }
}
