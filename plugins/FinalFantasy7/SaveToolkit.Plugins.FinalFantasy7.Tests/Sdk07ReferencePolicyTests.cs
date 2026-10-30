using System.Globalization;
using System.Text.Json;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.FinalFantasy7.Tests;

public sealed class Sdk07ReferencePolicyTests
{
    [Theory]
    [InlineData(BulkSelectionFixture.Remake, MateriaPlanning.ActionId)]
    [InlineData(BulkSelectionFixture.Remake, MateriaPlanning.StarActionId)]
    [InlineData(BulkSelectionFixture.Rebirth, MateriaPlanning.ActionId)]
    [InlineData(BulkSelectionFixture.Rebirth, MateriaPlanning.StarActionId)]
    [InlineData(BulkSelectionFixture.Demo, MateriaPlanning.ActionId)]
    [InlineData(BulkSelectionFixture.Demo, MateriaPlanning.StarActionId)]
    public void MateriaPresetSpecsMatchRenderedControlsAndPinnedCapture(string title, string actionId)
    {
        using var fixture = new BulkSelectionFixture(title, actionId);
        var adapter = (IPresetDocument)fixture.Document;
        var capability = adapter.PresetCapability.Actions.Single(action => action.ActionId == actionId);
        var tabs = Assert.IsType<TabsNode>(fixture.Document.BuildView());
        var materiaTab = Assert.Single(tabs.Tabs, tab => tab.Title == "Materia");
        var rendered = Assert.Single(materiaTab.Children.OfType<ActionNode>(), action => action.ActionId == actionId);
        var publishedReference = Assert.IsType<ReferenceSpec>(capability.ParameterSpecs[0]);
        var renderedReference = Assert.IsType<ReferenceSpec>(rendered.Parameters[0].Spec);
        Assert.Equal("ff7r-materia", publishedReference.CatalogId);
        Assert.Equal(renderedReference.CatalogId, publishedReference.CatalogId);
        Assert.False(publishedReference.AllowCustom);
        Assert.False(renderedReference.AllowCustom);
        var publishedRange = Assert.IsType<IntegerSpec>(capability.ParameterSpecs[1]);
        var renderedRange = Assert.IsType<IntegerSpec>(rendered.Parameters[1].Spec);
        Assert.Equal(renderedRange.Min, publishedRange.Min);
        Assert.Equal(renderedRange.Max, publishedRange.Max);

        var captured = fixture.Session.CapturePreset(new(actionId, fixture.WireParameters, PresetScopeKinds.Document));
        Assert.Equal(actionId, captured.ActionId);
        Assert.Empty(captured.Instances);
        Assert.Equal(fixture.Original, fixture.Bytes());
    }

    [Theory]
    [InlineData(BulkSelectionFixture.Remake, MateriaPlanning.ActionId)]
    [InlineData(BulkSelectionFixture.Remake, MateriaPlanning.StarActionId)]
    [InlineData(BulkSelectionFixture.Rebirth, MateriaPlanning.ActionId)]
    [InlineData(BulkSelectionFixture.Rebirth, MateriaPlanning.StarActionId)]
    [InlineData(BulkSelectionFixture.Demo, MateriaPlanning.ActionId)]
    [InlineData(BulkSelectionFixture.Demo, MateriaPlanning.StarActionId)]
    public void UnknownSentinelAndIneligibleMateriaIdsRejectBeforeMutation(string title, string actionId)
    {
        using var fixture = new BulkSelectionFixture(title, actionId);
        var schema = Ff7Schema.ForTitle(title)!;
        var summon = EmbeddedCatalogs.Records(schema, "ff7r-materia")
            .First(record => record.Category == "Summon Materia").Key;
        foreach (var type in new[] { "999999", uint.MaxValue.ToString(CultureInfo.InvariantCulture), summon })
        {
            var parameters = new Dictionary<string, JsonElement>(fixture.WireParameters)
            {
                ["type"] = JsonSerializer.SerializeToElement(type),
            };
            Assert.Throws<ArgumentException>(() => fixture.Session.CapturePreset(
                new(actionId, parameters, PresetScopeKinds.Document)));
            Assert.Equal(fixture.Original, fixture.Bytes());
            Assert.False(fixture.Session.State.CanUndo);
        }
    }
}
