using System.Text.Json;
using SaveToolkit.Core;
using SaveToolkit.Core.Editing;
using SaveToolkit.Sdk.Views;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.FinalFantasy7.Tests;

public sealed class BulkSelectionDeclarationTests
{
    [Theory]
    [InlineData(BulkSelectionFixture.Remake)]
    [InlineData(BulkSelectionFixture.Rebirth)]
    [InlineData(BulkSelectionFixture.Demo)]
    public void OnlyInventoryAndMateriaSiblingsBindToTheirPublishedTables(string title)
    {
        using var fixture = new BulkSelectionFixture(title, InventoryPlanning.ActionId);
        var root = fixture.Document.BuildView();
        var index = new ViewIndex();
        index.Add(root);
        Assert.Empty(index.SelectionDiagnostics);
        AssertBindings(Assert.IsType<TabsNode>(root));
        var json = JsonSerializer.Serialize(root, ToolkitJson.Options);
        AssertBindings(Assert.IsType<TabsNode>(JsonSerializer.Deserialize<ViewNode>(json, ToolkitJson.Options)));
        Assert.Equal(fixture.Original, fixture.Bytes());

        static void AssertBindings(TabsNode tabs)
        {
            var expected = new Dictionary<string, string>
            {
                [InventoryPlanning.ActionId] = "inventory",
                [MateriaPlanning.ActionId] = "materia",
                [MateriaPlanning.StarActionId] = "materia",
            };
            var actions = tabs.Tabs.SelectMany(tab => tab.Children).OfType<ActionNode>().ToArray();
            Assert.Equal(expected.Count, actions.Count(action => action.SelectionTableId is not null));
            foreach (var (id, collection) in expected)
            {
                var tab = Assert.Single(tabs.Tabs, tab => tab.Children.OfType<ActionNode>().Any(action => action.ActionId == id));
                var action = Assert.Single(tab.Children.OfType<ActionNode>(), action => action.ActionId == id);
                Assert.True(action.SupportsPlanning);
                Assert.True(action.Mutates);
                Assert.Equal(collection, action.SelectionTableId);
                Assert.Equal(collection, Assert.Single(tab.Children.OfType<QueryableTableNode>()).CollectionId);
            }
            var characters = Assert.Single(tabs.Tabs, tab => tab.Title == "Characters");
            Assert.All(characters.Children.OfType<ActionNode>(), action => Assert.Null(action.SelectionTableId));
            var characterTable = Assert.Single(characters.Children.OfType<TableNode>());
            Assert.All(characterTable.Rows.SelectMany(row => row.Cells.Values).OfType<ActionNode>(),
                action => Assert.Null(action.SelectionTableId));
        }
    }

    [Theory]
    [InlineData(BulkSelectionFixture.Remake)]
    [InlineData(BulkSelectionFixture.Rebirth)]
    [InlineData(BulkSelectionFixture.Demo)]
    public void CommonFilesDoNotPublishBulkBindings(string title)
    {
        var schema = Ff7Schema.ForTitle(title)!;
        using var document = new FinalFantasy7Plugin().Open(MemorySaveSet.SingleFile(schema.CommonName, ContainerTests.Common(title), title));
        var tabs = Assert.IsType<TabsNode>(document.BuildView());
        Assert.False(document.CanEdit);
        Assert.Empty(tabs.Tabs.SelectMany(tab => tab.Children).OfType<ActionNode>());
    }
}
