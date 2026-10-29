using SaveToolkit.Sdk.Views;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.TalesOfArise.Tests;

public sealed class HonestLabelTests
{
    [Fact]
    public void InventoryActionControlsExplainConsoleRiskBeforeOpeningPreview()
    {
        var bytes = new AriseTestContainer().Encrypt();
        using var document = new TalesOfArisePlugin().Open(MemorySaveSet.SingleFile(
            AriseTestContainer.FileName, bytes, AriseTestContainer.TitleId));
        var inventory = Assert.IsType<TabsNode>(document.BuildView()).Tabs.Single(tab => tab.Title == "Inventory");
        var actions = inventory.Children.OfType<ActionNode>().Where(action => action.Mutates).ToArray();

        Assert.Equal(2, actions.Length);
        Assert.All(actions, action =>
        {
            Assert.Contains("Experimental", action.Description!);
            Assert.Contains("Not validated on console", action.Description!);
            Assert.Contains("stack bound are inferred", action.Description!);
            Assert.Contains("game may still reject", action.Description!);
        });
        Assert.Equal(bytes, Assert.Single(document.Serialize()).Content.ToArray());
    }
}
