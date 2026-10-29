namespace SaveToolkit.Plugins.FinalFantasy7.Tests;

public sealed class SemanticPresetCompatibilityTests
{
    [Theory]
    [InlineData("PPSA01911")]
    [InlineData("PPSA08664")]
    [InlineData("PPSA18936")]
    public void CommonRoleDoesNotPublishOrCaptureMutatingPresets(string title)
    {
        var schema = Ff7Schema.ForTitle(title)!;
        var bytes = ContainerTests.Common(title);
        using var common = (FinalFantasy7Document)new FinalFantasy7Plugin().Open(
            SaveToolkit.Testing.MemorySaveSet.SingleFile(schema.CommonName, bytes, title));
        Assert.Empty(common.Presets.Actions);
        Assert.Throws<ArgumentException>(() => common.Presets.Capture(InventoryPlanning.ActionId,
            SemanticPresetFixture.RestockParameters, PresetScopeKind.Document));
        SemanticPresetFixture.Unchanged(common, bytes);
    }

    [Theory]
    [InlineData("PPSA01911", "PPSA01911")]
    [InlineData("PPSA01911", "PPSA08664")]
    [InlineData("PPSA01911", "PPSA18936")]
    [InlineData("PPSA08664", "PPSA01911")]
    [InlineData("PPSA08664", "PPSA08664")]
    [InlineData("PPSA08664", "PPSA18936")]
    [InlineData("PPSA18936", "PPSA01911")]
    [InlineData("PPSA18936", "PPSA08664")]
    [InlineData("PPSA18936", "PPSA18936")]
    public void ExactTitleAndVariantAreRequiredEvenWhenContainerDimensionsMatch(string sourceTitle, string targetTitle)
    {
        var source = new SemanticPresetFixture(sourceTitle);
        using var captured = source.Open();
        var preset = captured.Presets.Capture(CharacterPlanning.RecoveryActionId,
            new Dictionary<string, SaveToolkit.Sdk.Values.FieldValue>(), PresetScopeKind.Selected, ["character/0"]);
        var destination = new SemanticPresetFixture(targetTitle);
        using var target = destination.Open();
        if (sourceTitle == targetTitle)
        {
            Assert.Single(target.Presets.Resolve(preset).Scope.RowKeys!);
            Assert.Throws<ArgumentException>(() => target.Presets.Resolve(preset with { TitleId = "PPSA00000" }));
            Assert.Throws<ArgumentException>(() => target.Presets.Resolve(preset with { Variant = (Ff7Variant)99 }));
        }
        else Assert.Throws<ArgumentException>(() => target.Presets.Preview(preset));
        SemanticPresetFixture.Unchanged(target, destination.Bytes);
    }
}
