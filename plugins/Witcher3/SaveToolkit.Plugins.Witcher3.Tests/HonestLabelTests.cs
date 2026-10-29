using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.Witcher3.Tests;

public sealed class HonestLabelTests
{
    [Fact]
    public void AbilityPointControlAndScalarOutcomeRetainConsoleAndDependencyWarnings()
    {
        var bytes = SyntheticPlayerSave.Create();
        using var document = WitcherTestFiles.Open(bytes);
        var player = Assert.IsType<TabsNode>(document.BuildView()).Tabs.Single(tab => tab.Title == "Player");
        var field = player.Children.OfType<FieldNode>().Single(field => field.ValueId == "player/abilityPoints");
        Assert.Contains("Not validated on console", field.Warning!);
        Assert.Contains("Costs", field.Warning!);
        Assert.Equal(bytes, Assert.Single(document.Serialize()).Content.ToArray());

        var changed = document.ApplyEdits([new(field.ValueId, FieldValue.Of(3))]);

        Assert.True(changed.Succeeded);
        Assert.Contains(WriteFields.ConsoleWarning, changed.Warnings);
        Assert.True(document.ApplyEdits([new(field.ValueId, field.Value)]).Succeeded);
        Assert.Equal(bytes, Assert.Single(document.Serialize()).Content.ToArray());
    }
}
