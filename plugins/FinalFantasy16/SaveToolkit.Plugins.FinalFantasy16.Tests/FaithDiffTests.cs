using SaveToolkit.Testing;
using SaveToolkit.Testing.Diff;

namespace SaveToolkit.Plugins.FinalFantasy16.Tests;

public sealed class FaithDiffTests
{
    [Fact]
    public void SharedF1ToolReproducesUncontrolledGenerationEvidence()
    {
        var set = SampleSaves.Load("PPSA10665");
        var manifest = FixtureManifest.Load(Path.Combine(AppContext.BaseDirectory, "generations.fixture.json"));
        var fixture = Assert.Single(manifest.Fixtures);
        var archive = SampleSaves.FindArchive("PPSA10665")!;
        Assert.Null(FixtureVerifier.CheckArchive(fixture.Before!, archive));
        var options = FixtureVerifier.OptionsFor(fixture, manifest, new PluginViewDecoder(new FinalFantasy16Plugin()));
        var result = FixtureVerifier.Verify(fixture, set, set, options);
        Assert.True(result.Passed, string.Join("\n", result.Errors.Concat(result.Expectations.Where(e => !e.Passed).Select(e => e.Message))));
        var file = Assert.Single(result.Report.Files);
        Assert.Equal(FileDiffStatus.Changed, file.Status);
        Assert.Equal(0, file.BeforeGeneration);
        Assert.Equal(1, file.AfterGeneration);
        Assert.Equal("564b39c6d5dc2404905cebb402d122fd06c9b8174b70cf2564e5c56d52497650", file.BeforeSha256);
        Assert.Null(file.Semantic?.Error);
        Assert.False(file.Semantic?.InputTruncated);
        Assert.True(file.Bytes?.HasChanges);
    }
}
