using System.Text.Json;
using SaveToolkit.Core.Presets;
using SaveToolkit.Sdk;

namespace SaveToolkit.Plugins.FinalFantasy7.Tests;

public sealed class Sdk07ResolutionShapeTests
{
    [Fact]
    public void UniqueInventoryResolutionMatchesPublishedHostCardinality()
    {
        var fixture = new SemanticPresetFixture();
        fixture.Item(0, type: 101, quantity: 1);
        using var document = fixture.Open();
        var adapter = (IPresetDocument)document;
        var action = adapter.CapturePreset(new(InventoryPlanning.ActionId,
            new Dictionary<string, JsonElement> { ["quantity"] = JsonSerializer.SerializeToElement(99) },
            PresetScopeKinds.Selected, ["inventory/0"]));
        var resolution = adapter.ResolvePresetInstances(action.Instances);

        var result = Assert.Single(resolution.Instances);
        Assert.Equal(PresetInstanceResolutionStatus.Resolved, result.Status);
        var candidate = Assert.Single(result.Candidates);
        Assert.Equal("ff7:inventory-candidate/101/0", candidate.StableId);
        Assert.Null(PresetContractValidation.ValidateResolution(resolution, action.Instances, null, false, out _));
    }
}
