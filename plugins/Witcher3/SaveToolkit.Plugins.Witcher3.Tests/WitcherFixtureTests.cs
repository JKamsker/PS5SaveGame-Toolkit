using SaveToolkit.Sdk.Values;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.Witcher3.Tests;

public sealed class WitcherFixtureTests
{
    [Fact]
    public void ImportsUseIndependentProcessScratchFilesAndRetainTheirInputBytes()
    {
        var input = SyntheticPlayerSave.Create();
        var original = input.ToArray();
        var first = WitcherTestFiles.Import(input);
        input[^1] ^= 1;
        var second = WitcherTestFiles.Import(input);
        Assert.Equal(original, first.ReadAllBytes());
        Assert.Equal(input, second.ReadAllBytes());
        Assert.Equal("Manual.sav", first.PathInDirectory);
        Assert.Equal("PPSA04018", first.Directory.TitleId);
        Assert.Equal(first.RelativePath, second.RelativePath);
        using var firstStream = Assert.IsType<FileStream>(first.OpenRead());
        using var secondStream = Assert.IsType<FileStream>(second.OpenRead());
        Assert.NotEqual(firstStream.Name, secondStream.Name);
        var scratch = Path.GetFullPath(TestScratch.ProcessFolder) + Path.DirectorySeparatorChar;
        Assert.StartsWith(scratch, firstStream.Name, StringComparison.OrdinalIgnoreCase);
        Assert.StartsWith(scratch, secondStream.Name, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PinnedContextsKeepDatasetUsageIndependent()
    {
        var first = ReferencePackFixture.Pin();
        var second = ReferencePackFixture.Pin();
        Assert.NotSame(first, second);
        Assert.Equal(3, first.Packs.Count);
        Assert.All(first.Datasets(), dataset => Assert.False(dataset.Used));
        Assert.NotNull(first.Catalog("items"));
        Assert.Contains(first.Datasets(), dataset => dataset.Used);
        Assert.All(second.Datasets(), dataset => Assert.False(dataset.Used));
    }

    [Fact]
    public void ParsedModelsRetainTheSuppliedContainerAndDoNotShareMutableProgression()
    {
        var input = SyntheticPlayerSave.Create();
        var container = WitcherContainer.Parse(input);
        var first = PlayerSaveFixture.Read(container);
        var second = PlayerSaveFixture.Read(input);
        Assert.Same(container, first.Container);
        first.Player.Progress.Experience = 123;
        Assert.Equal(0, second.Player.Progress.Experience);
        Assert.Equal(input, first.Container.Original);
        Assert.Equal(first.Container.Payload, second.Container.Payload);
        Assert.Equal(first.Directory.Names, second.Directory.Names);
    }

    [Fact]
    public void OpenedDocumentsHaveIndependentEditsAndPreserveNoOpBytes()
    {
        var input = SyntheticPlayerSave.Create();
        using var first = WitcherTestFiles.Open(input);
        using var second = WitcherTestFiles.Open(input);
        _ = first.BuildView();
        _ = second.BuildView();
        Assert.True(first.ApplyEdits([new("inventory/1/quantity", FieldValue.Of(500))]).Succeeded);
        Assert.Equal(250, second.GetValue("inventory/1/quantity").AsInt32());
        Assert.Equal(input, Assert.Single(second.Serialize()).Content.ToArray());
    }
}
