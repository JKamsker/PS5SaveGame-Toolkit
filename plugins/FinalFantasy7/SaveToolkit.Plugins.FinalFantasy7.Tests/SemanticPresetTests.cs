using SaveToolkit.Sdk.Values;

namespace SaveToolkit.Plugins.FinalFantasy7.Tests;

public sealed class SemanticPresetTests
{
    [Fact]
    public void InventoryTypeMovesAndMultipleMatchesRequireExplicitChoiceOrAllMatches()
    {
        var source = new SemanticPresetFixture();
        source.Item(3);
        using var captured = source.Open();
        var parameters = SemanticPresetFixture.RestockParameters;
        var rows = new List<string> { "inventory/3" };
        var preset = captured.Presets.Capture(InventoryPlanning.ActionId, parameters, PresetScopeKind.Selected, rows);
        parameters["quantity"] = FieldValue.Of(2L);
        rows.Clear();
        Assert.Equal(99, preset.Parameters["quantity"].AsInt32());
        var capturedTarget = Assert.Single(preset.Targets);
        Assert.Equal(PresetIdentityDomain.ItemType, capturedTarget.Domain);
        Assert.Equal(SemanticPresetFixture.ItemType, capturedTarget.Id);
        Assert.NotNull(capturedTarget.Source);

        var destination = new SemanticPresetFixture();
        destination.Item(19);
        using var moved = destination.Open();
        var preview = moved.Presets.Preview(preset);
        Assert.Equal("inventory/19", Assert.Single(preview.Resolution.Scope.RowKeys!));
        Assert.Equal("inventory/19/quantity", Assert.Single(preview.Plan.Changes).ValueId);
        SemanticPresetFixture.Unchanged(moved, destination.Bytes);

        destination.Item(42);
        using var duplicate = destination.Open();
        preview = duplicate.Presets.Preview(preset);
        Assert.True(preview.Resolution.NeedsChoice);
        Assert.Equal(2, Assert.Single(preview.Resolution.Targets).Candidates.Count);
        Assert.NotEmpty(preview.Plan.Errors);
        Assert.Equal("inventory/42/quantity", Assert.Single(duplicate.Presets.Preview(preset,
            new Dictionary<int, string> { [0] = "inventory/42" }).Plan.Changes).ValueId);
        Assert.Throws<ArgumentException>(() => duplicate.Presets.Preview(preset,
            new Dictionary<int, string> { [0] = "inventory/3" }));
        var allMatches = preset with { Targets = [preset.Targets[0] with { AllMatches = true }] };
        Assert.Equal(2, duplicate.Presets.Preview(allMatches).Plan.Changes.Count);
        Assert.Throws<ArgumentException>(() => duplicate.Presets.Resolve(allMatches,
            new Dictionary<int, string> { [0] = "inventory/19" }));
        SemanticPresetFixture.Unchanged(duplicate, destination.Bytes);
    }

    [Fact]
    public void MissingAndEmptySelectionsNeverBroadenToDocumentScope()
    {
        var fixture = new SemanticPresetFixture();
        fixture.Item(1);
        using var document = fixture.Open();
        var empty = document.Presets.Capture(InventoryPlanning.ActionId, SemanticPresetFixture.RestockParameters,
            PresetScopeKind.Selected, []);
        var preview = document.Presets.Preview(empty);
        Assert.Empty(preview.Resolution.Scope.RowKeys!);
        Assert.Empty(preview.Plan.Changes);
        var missing = empty with { Targets = [new(PresetIdentityDomain.ItemType, 102)] };
        preview = document.Presets.Preview(missing);
        Assert.Equal(PresetTargetStatus.Missing, Assert.Single(preview.Resolution.Targets).Status);
        Assert.Equal("missing-target", preview.Resolution.Targets[0].Reason);
        Assert.Empty(preview.Plan.Changes);
        var all = document.Presets.Capture(InventoryPlanning.ActionId, SemanticPresetFixture.RestockParameters, PresetScopeKind.AllEligible);
        Assert.Null(document.Presets.Preview(all).Resolution.Scope.RowKeys);
        Assert.Single(document.Presets.Preview(all).Plan.Changes);
        SemanticPresetFixture.Unchanged(document, fixture.Bytes);
    }

    [Theory]
    [InlineData("PPSA01911")]
    [InlineData("PPSA08664")]
    [InlineData("PPSA18936")]
    public void ExactlyFiveExistingActionDescriptorsAndTypedParameterCapture(string title)
    {
        var fixture = new SemanticPresetFixture(title);
        using var document = fixture.Open();
        Assert.Equal(new[] { InventoryPlanning.ActionId, MateriaPlanning.ActionId, MateriaPlanning.StarActionId,
            CharacterPlanning.RecoveryActionId, CharacterPlanning.LevelActionId }, document.Presets.Actions.Select(action => action.ActionId));
        Assert.All(document.Presets.Actions, action => Assert.True(action.SupportsPlanning));
        Assert.All(document.Presets.Actions, action =>
        {
            var parameters = action.Parameters.ToDictionary(parameter => parameter.ValueId, parameter => parameter.Value);
            var preset = document.Presets.Capture(action.ActionId, parameters, PresetScopeKind.Selected, []);
            Assert.Equal(parameters, preset.Parameters);
            Assert.Empty(document.Presets.Preview(preset).Plan.Changes);
        });
        SemanticPresetFixture.Unchanged(document, fixture.Bytes);
    }

    [Fact]
    public void ForeignDomainsMalformedScopesAndUnsupportedActionsRejectWithoutMutation()
    {
        var fixture = new SemanticPresetFixture();
        fixture.Item(1);
        using var document = fixture.Open();
        var preset = document.Presets.Capture(InventoryPlanning.ActionId, SemanticPresetFixture.RestockParameters,
            PresetScopeKind.Selected, ["inventory/1"]);
        foreach (var target in new[] { new PresetTarget(PresetIdentityDomain.MateriaInstance, 101, 101),
            new(PresetIdentityDomain.CharacterIndex, 0), new(PresetIdentityDomain.ItemType, uint.MaxValue),
            new(PresetIdentityDomain.ItemType, 101, 101), new((PresetIdentityDomain)99, 101) })
            Assert.Throws<ArgumentException>(() => document.Presets.Preview(preset with { Targets = [target] }));
        Assert.Throws<ArgumentException>(() => document.Presets.Preview(preset with { ScopeKind = PresetScopeKind.Document }));
        Assert.Throws<ArgumentException>(() => document.Presets.Preview(preset with { ScopeKind = (PresetScopeKind)99 }));
        Assert.Throws<ArgumentException>(() => document.Presets.Preview(preset with { Targets = [preset.Targets[0], preset.Targets[0]] }));
        Assert.Throws<ArgumentException>(() => document.Presets.Capture("character/0/weapon", new Dictionary<string, FieldValue>(), PresetScopeKind.Document));
        Assert.Throws<ArgumentException>(() => document.Presets.Capture(InventoryPlanning.ActionId, new Dictionary<string, FieldValue>(), PresetScopeKind.Document));
        foreach (var row in new[] { "materia/1", "inventory/01", "inventory/-1", "inventory/2048" })
            Assert.Throws<ArgumentException>(() => document.Presets.Capture(InventoryPlanning.ActionId,
                SemanticPresetFixture.RestockParameters, PresetScopeKind.Selected, [row]));
        SemanticPresetFixture.Unchanged(document, fixture.Bytes);
    }
}
