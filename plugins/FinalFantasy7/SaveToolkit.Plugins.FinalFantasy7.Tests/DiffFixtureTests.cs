using SaveToolkit.Testing;
using SaveToolkit.Testing.Diff;

namespace SaveToolkit.Plugins.FinalFantasy7.Tests;

public sealed class DiffFixtureTests
{
    [Theory]
    [InlineData("PPSA01911")]
    [InlineData("PPSA08664")]
    [InlineData("PPSA18936")]
    public void SharedF1ManifestVerifiesEveryArchivedGenerationWithoutInventingCaptureEvents(string title)
    {
        using var stream = typeof(DiffFixtureTests).Assembly.GetManifestResourceStream(
            "SaveToolkit.Plugins.FinalFantasy7.Tests.Fixtures.noop.json")!;
        using var reader = new StreamReader(stream);
        var manifest = FixtureManifest.Parse(reader.ReadToEnd());
        var fixture = manifest.Fixtures.Single(fixture => fixture.TitleId == title);
        var saves = SampleSaves.Load(title);
        var result = FixtureVerifier.Verify(fixture, saves, saves, FixtureVerifier.OptionsFor(fixture, manifest, decoder: null));
        Assert.True(result.Passed, string.Join("; ", result.Unexpected.Concat(result.Errors)));
        Assert.All(result.Expectations, expectation => Assert.True(expectation.Passed, expectation.Message));
    }

    [Fact]
    public void ExistingDecoderHookReadsCharacterIdentityWithExplicitF8CoverageLimitation()
    {
        var file = SampleSaves.Load("PPSA08664").AllDiskFiles.First(file => file.FileName == "ff7rebirth000");
        var decoder = new PluginViewDecoder(new FinalFantasy7Plugin(), lazyExpansionBudget: 0);
        Assert.True(decoder.CanDecode(file));
        var tree = decoder.Decode(file, new());
        Assert.Contains(tree.Values, value => value.Path == "character/0/experience" && value.Value == "148409");
        Assert.True(tree.Truncated);
        Assert.NotEmpty(tree.Warnings);
        // F12 (#32) owns full F8 traversal; do not build a private substitute or claim complete semantics.
    }
}
