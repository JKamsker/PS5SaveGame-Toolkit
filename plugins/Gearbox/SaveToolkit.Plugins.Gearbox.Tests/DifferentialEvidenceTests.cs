using SaveToolkit.Testing;
using SaveToolkit.Testing.Diff;

namespace SaveToolkit.Plugins.Gearbox.Tests;

public sealed class DifferentialEvidenceTests
{
    [Theory]
    [InlineData("PPSA01463")]
    [InlineData("PPSA01493")]
    public void F1Fixtures_PassBothIndependentAndPluginDecoderHooks(string title)
    {
        using var stream = typeof(DifferentialEvidenceTests).Assembly.GetManifestResourceStream("gearbox.fixtures.json")!;
        using var reader = new StreamReader(stream);
        var manifest = FixtureManifest.Parse(reader.ReadToEnd());
        var fixture = manifest.Fixtures.Single(f => f.TitleId == title);
        var set = SampleSaves.Load(title);
        Assert.Null(FixtureVerifier.CheckArchive(fixture.Before!, SampleSaves.FindArchive(title)!));
        Assert.Null(FixtureVerifier.CheckArchive(fixture.After!, SampleSaves.FindArchive(title)!));
        foreach (var decoder in new ISaveDecoder[] { new GearboxEvidenceDecoder(), Native(), new PluginViewDecoder(new GearboxPlugin()) })
        {
            var result = FixtureVerifier.Verify(fixture, set, set, FixtureVerifier.OptionsFor(fixture, manifest, decoder));
            Assert.True(result.Passed, string.Join("; ", result.Expectations.Where(e => !e.Passed).Select(e => e.Message)));
            Assert.All(result.Report.Files, f => Assert.Null(f.Semantic?.Error));
        }
    }

    [Theory]
    [InlineData("PPSA01463")]
    [InlineData("PPSA01493")]
    public void EvidenceHook_IsBoundedAndOmitsIdentityPaths(string title)
    {
        var file = SampleSaves.Load(title).AllDiskFiles.First(f => f.FileName == "1");
        var decoder = Native();
        var tree = decoder.Decode(file, new DecodeLimits { MaxValues = 3, MaxValueLength = 8 });
        Assert.True(tree.Truncated);
        Assert.Equal(3, tree.Values.Count);
        Assert.All(tree.Values, value => Assert.True(value.Path is "character/xp" or "currency/618814354"
            || value.Path.StartsWith("serials[", StringComparison.Ordinal)));
        Assert.True(decoder.Decode(file, new DecodeLimits { MaxDepth = 1 }).Truncated);
        Assert.Throws<InvalidDataException>(() => decoder.Decode(file, new DecodeLimits { MaxFileBytes = 1 }));
    }

    [Theory]
    [InlineData("PPSA01463")]
    [InlineData("PPSA01493")]
    public void ProductionHook_AgreesWithIndependentProbeAcrossAllFiles(string title)
    {
        var native = Native();
        var independent = new GearboxEvidenceDecoder();
        foreach (var file in SampleSaves.Load(title).AllDiskFiles.Where(f => native.CanDecode(f)))
        {
            var expected = independent.Decode(file, new());
            var actual = native.Decode(file, new());
            Assert.False(actual.Truncated);
            Assert.Equal(expected.Values.OrderBy(v => v.Path, StringComparer.Ordinal), actual.Values.OrderBy(v => v.Path, StringComparer.Ordinal));
        }
    }

    private static HookSaveDecoder Native() => new(Assert.Single(new GearboxPlugin().Decoders));

    [Theory]
    [InlineData("PPSA01463")]
    [InlineData("PPSA01493")]
    public void F1SelfComparison_CoversAllGenerationsAndMirroredBackups(string title)
    {
        var set = SampleSaves.Load(title);
        var report = SaveSetDiffer.Compare(set, set, new SaveDiffOptions
        {
            TitleFilter = title, FileFilters = ["1", "profile"], Decoder = new GearboxEvidenceDecoder(),
        });
        Assert.False(report.HasChanges);
        Assert.Equal(12, report.Files.Count);
        Assert.Equal(6, report.Files.Count(f => f.IsBackup));
        Assert.All(report.Files, f => Assert.Equal(FileDiffStatus.Unchanged, f.Status));
        Assert.All(report.Files, f => Assert.Null(f.Semantic?.Error));
    }
}
