using System.Text.Json;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Views;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.FinalFantasy7.Tests;

public sealed class Sdk07R6BoundaryTests
{
    [Fact]
    public void CommonResolutionIsUnsupportedWithoutReadingGameplayOffsetsOrPlanning()
    {
        const string title = "PPSA08664";
        var source = new SemanticPresetFixture(title);
        source.Item(0);
        source.Materia(0, 0);
        using var gameplay = source.Open();
        var sourceAdapter = (IPresetDocument)gameplay;
        var actions = new[]
        {
            sourceAdapter.CapturePreset(new(InventoryPlanning.ActionId,
                new Dictionary<string, JsonElement> { ["quantity"] = JsonSerializer.SerializeToElement(99) },
                PresetScopeKinds.Selected, ["inventory/0"])),
            sourceAdapter.CapturePreset(new(MateriaPlanning.ActionId,
                new Dictionary<string, JsonElement> { ["type"] = JsonSerializer.SerializeToElement("10020"), ["ap"] = JsonSerializer.SerializeToElement(300) },
                PresetScopeKinds.Selected, ["materia/0"])),
            sourceAdapter.CapturePreset(new(MateriaPlanning.ActionId,
                new Dictionary<string, JsonElement> { ["type"] = JsonSerializer.SerializeToElement("10020"), ["ap"] = JsonSerializer.SerializeToElement(300) },
                PresetScopeKinds.AllCopies, null, true)),
            sourceAdapter.CapturePreset(new(CharacterPlanning.RecoveryActionId, new Dictionary<string, JsonElement>(),
                PresetScopeKinds.Selected, ["character/0"])),
        };

        var schema = Ff7Schema.ForTitle(title)!;
        var bytes = ContainerTests.Common(title);
        using var common = (FinalFantasy7Document)new FinalFantasy7Plugin().Open(
            MemorySaveSet.SingleFile(schema.CommonName, bytes, title));
        var before = common.Serialize().Single().Content.ToArray();
        var adapter = (IPresetDocument)common;

        foreach (var action in actions)
        {
            var resolution = adapter.ResolvePresetInstances(action.Instances);
            var result = Assert.Single(resolution.Instances);
            Assert.Equal(PresetInstanceResolutionStatus.Unsupported, result.Status);
            Assert.Empty(result.Candidates);
            Assert.Equal("common-file-read-only", result.Message);
            var plan = adapter.PlanPreset([action], resolution);
            Assert.NotEmpty(plan.Errors);
            Assert.False(adapter.ApplyPreset([action], resolution, plan).Succeeded);
        }

        Assert.Equal(before, common.Serialize().Single().Content.ToArray());
    }

    [Fact]
    public void UnpublishedInventoryAllMatchesIdentityIsRejectedBeforePlanOrHistory()
    {
        using var fixture = new BulkSelectionFixture("PPSA08664", InventoryPlanning.ActionId);
        var adapter = (IPresetDocument)fixture.Document;
        var captured = adapter.CapturePreset(new(InventoryPlanning.ActionId,
            new Dictionary<string, JsonElement> { ["quantity"] = JsonSerializer.SerializeToElement(99) },
            PresetScopeKinds.Selected, [fixture.Key(0)]));
        var action = captured with
        {
            Instances = [captured.Instances.Single() with { Kind = "ff7.item-type.all-matches" }],
        };

        var resolution = adapter.ResolvePresetInstances(action.Instances);
        var result = Assert.Single(resolution.Instances);
        Assert.Equal(PresetInstanceResolutionStatus.Unsupported, result.Status);
        Assert.Empty(result.Candidates);
        var plan = adapter.PlanPreset([action], resolution);
        Assert.NotEmpty(plan.Errors);
        Assert.False(adapter.ApplyPreset([action], resolution, plan).Succeeded);
        Assert.Equal(fixture.Original, fixture.Bytes());
        Assert.False(fixture.Session.State.CanUndo);
    }

    [Theory]
    [InlineData(BulkSelectionFixture.Remake)]
    [InlineData(BulkSelectionFixture.Rebirth)]
    [InlineData(BulkSelectionFixture.Demo)]
    public void CharacterDescriptionsMatchSelectionBinding(string title)
    {
        using var fixture = new BulkSelectionFixture(title, CharacterPlanning.RecoveryActionId);
        var tabs = Assert.IsType<TabsNode>(fixture.Document.BuildView());
        var characters = Assert.Single(tabs.Tabs, tab => tab.Title == "Characters");
        var actions = characters.Children.OfType<ActionNode>().ToArray();
        Assert.Equal(2, actions.Length);
        Assert.All(actions, action =>
        {
            Assert.Contains("Follow the characters table selection", action.Description, StringComparison.Ordinal);
            Assert.Contains("with no selection", action.Description, StringComparison.Ordinal);
        });
        var table = Assert.Single(characters.Children.OfType<TableNode>());
        Assert.Contains("controls follow that selection", table.Description, StringComparison.Ordinal);
        Assert.DoesNotContain("separate controls above the table target all seven", table.Description, StringComparison.Ordinal);
    }
}
