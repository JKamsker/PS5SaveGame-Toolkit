using SaveToolkit.Testing;
using SaveToolkit.Testing.Diff;
using SaveToolkit.Sdk.Diff;

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
    public void FrameworkViewDecoderReadsCharacterIdentityWithExplicitLazyCoverageLimit()
    {
        var file = SampleSaves.Load("PPSA08664").AllDiskFiles.First(file => file.FileName == "ff7rebirth000");
        var decoder = new PluginViewDecoder(new FinalFantasy7Plugin(), lazyExpansionBudget: 0);
        Assert.True(decoder.CanDecode(file));
        var tree = decoder.Decode(file, new());
        Assert.Contains(tree.Values, value => value.Path == "character/0/experience" && value.Value == "148409");
        Assert.True(tree.Truncated);
        Assert.NotEmpty(tree.Warnings);
        // The SDK-native hook below is the production F1 path; this test keeps the framework view decoder's bounded behavior covered too.
    }

    [Fact]
    public void ProviderPublishesBoundedSdkEvidenceHook()
    {
        var provider = Assert.IsAssignableFrom<ISaveDecoderProvider>(new FinalFantasy7Plugin());
        var hook = Assert.Single(provider.Decoders);
        Assert.Equal("ff7r-character-evidence", hook.Id);

        var file = SampleSaves.Load("PPSA08664").AllDiskFiles.First(file => file.FileName == "ff7rebirth000");
        var original = file.ReadAllBytes();
        var decoder = new HookSaveDecoder(hook);
        var tree = decoder.Decode(file, new());

        Assert.False(tree.Truncated, string.Join("; ", tree.Warnings));
        Assert.Equal(49, tree.Values.Count);
        Assert.Contains(tree.Values, value => value.Path == "character/0/experience" && value.Value == "148409");
        Assert.Equal(original, file.ReadAllBytes());

        var bounded = decoder.Decode(file, new DecodeLimits { MaxValues = 1 });
        Assert.True(bounded.Truncated);
        Assert.Single(bounded.Values);
        Assert.False(hook.CanDecode(MemorySaveSet.SingleFile("other.dat", [0])));
    }
}
