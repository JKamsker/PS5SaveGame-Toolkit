using SaveToolkit.Sdk.Diff;
using SaveToolkit.Testing;
using SaveToolkit.Testing.Diff;

namespace SaveToolkit.Plugins.SuckerPunch.Tests;

public sealed class TsushimaDifferentialTests
{
    [Fact]
    public void F1FixtureUsesPluginDecoderAcrossObservedGenerations()
    {
        using var stream = typeof(TsushimaDifferentialTests).Assembly
            .GetManifestResourceStream("suckerpunch.fixtures.json");
        Assert.NotNull(stream);
        using var reader = new StreamReader(stream!);
        var manifest = FixtureManifest.Parse(reader.ReadToEnd());
        var fixture = Assert.Single(manifest.Fixtures);
        var samples = SampleSaves.Load(fixture.TitleId);

        var verification = FixtureVerifier.Verify(
            fixture,
            samples,
            samples,
            FixtureVerifier.OptionsFor(fixture, manifest,
                new HookSaveDecoder(Assert.Single(((ISaveDecoderProvider)new SuckerPunchPlugin()).Decoders))));

        Assert.True(verification.Passed,
            string.Join("; ", verification.Errors.Concat(verification.Unexpected)));
        Assert.NotEmpty(verification.Report.Files);
        Assert.All(verification.Report.Files, file => Assert.Null(file.Semantic?.Error));
    }

    [Fact]
    public void F1PluginDecoderHonorsBoundsAndExposesStableQuantityPaths()
    {
        var file = SuckerPunchTestData.Slot("suckerpunchUser1");
        var decoder = new PluginViewDecoder(new SuckerPunchPlugin());
        var tree = decoder.Decode(file, new DecodeLimits { MaxValues = 8, MaxValueLength = 64 });

        Assert.True(tree.Truncated);
        var complete = decoder.Decode(file, new DecodeLimits { MaxValues = 200, MaxValueLength = 64 });
        Assert.Contains(complete.Values, value => value.Path == SuckerPunchTestData.SuppliesId);
        Assert.Throws<InvalidDataException>(() => decoder.Decode(file, new DecodeLimits { MaxFileBytes = 1 }));
    }
}
