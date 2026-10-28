using SaveToolkit.Core.References;
using SaveToolkit.Plugins.UnrealGvas.Gvas;
using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Views;
using SaveToolkit.Testing;
using SaveToolkit.Testing.Diff;

namespace SaveToolkit.Plugins.UnrealGvas.Tests;

public sealed class UnrealGvasOverlayTests
{
    [Fact]
    public void EmbeddedEvidencePack_IsVersionedAndAttributed()
    {
        var plugin = new UnrealGvasPlugin();
        var context = new ReferencePackStore([plugin]).Pin(plugin, "PPSA01288");
        var catalog = Assert.IsAssignableFrom<IReferenceCatalog>(context.Catalog("unrealgvas_sackboy_fields"));

        Assert.Equal("1.1.0", catalog.Pack.Version);
        Assert.Equal(ReferencePackOrigin.Embedded, catalog.Pack.Origin);
        Assert.Equal(24, catalog.Count);
        Assert.Null(catalog.Find("CollectabellsStored")!.Limit);
        Assert.Contains("PS5 Save Game Toolkit", catalog.Find("Checksum")!.AttributionText);
    }

    [Fact]
    public void JediGameplay_ExposesBoundedDecodedActorIndexAndKeepsDocumentReadOnly()
    {
        var plugin = new UnrealGvasPlugin();
        var context = new ReferencePackStore([plugin]).Pin(plugin, "PPSA02199");
        var file = SampleSaves.Load("PPSA02199").AllDiskFiles.First(f =>
            f.Directory.Name == "SaveGame00" && f.FileName == "ue4savegame.dpx.sav");

        using var document = plugin.Open(file, context);
        Assert.False(document.CanEdit);
        var tabs = Assert.IsType<TabsNode>(document.BuildView()).Tabs;
        Assert.Contains(tabs, tab => tab.Title == "Schema");
        var actorPage = document.ExpandNode("jedi/actors");
        Assert.True(actorPage.OfType<TableNode>().Any(), string.Join(" | ", actorPage.OfType<TextNode>().Select(text => text.Text)));
        var table = Assert.Single(actorPage.OfType<TableNode>());
        Assert.NotEmpty(table.Rows);
        Assert.Contains(table.Rows, row => row.Cells["path"] is TextNode path && path.Text.Contains("ActorRecords", StringComparison.Ordinal));
    }

    [Fact]
    public void SackboyGenerations_DiffAsEvidenceAndExposeReadOnlySchemaFields()
    {
        var files = SampleSaves.Load("PPSA01288").AllDiskFiles
            .Where(f => f.FileName == "General").DistinctBy(f => f.Sha256).ToArray();
        Assert.Equal(2, files.Length);
        var diff = ByteDiffer.Compare(files[0].ReadAllBytes(), files[1].ReadAllBytes());
        Assert.True(diff.HasChanges);
        Assert.NotEmpty(diff.Ranges);

        using var document = new UnrealGvasPlugin().Open(files[0]);
        Assert.False(document.CanEdit);
        var schema = Assert.Single(Assert.IsType<TabsNode>(document.BuildView()).Tabs, tab => tab.Title == "Schema");
        var table = Assert.Single(schema.Children.OfType<TableNode>());
        Assert.Contains(table.Rows, row => row.Cells["property"] is FieldNode field && field.Value.AsString() == "CollectabellsStored");
        Assert.Contains(table.Rows, row => row.Cells["property"] is FieldNode field && field.Value.AsString() == "Checksum");
    }
}
