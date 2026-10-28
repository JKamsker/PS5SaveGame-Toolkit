using SaveToolkit.Testing;
using SaveToolkit.Testing.Diff;

namespace SaveToolkit.Plugins.AstroPlayroom.Tests;

public sealed class AstroDifferentialEvidenceTests
{
    [Fact]
    public void F1ReproducesAllObservedTransitionsWithoutTreatingMixedEventsAsDependencyProof()
    {
        var manifest = FixtureManifest.Load(Path.Combine(AppContext.BaseDirectory, "fixtures.json"));
        var fixture = Assert.Single(manifest.Fixtures);
        Assert.False(fixture.Control);
        Assert.Contains("Unlabeled mixed-event", fixture.Event, StringComparison.Ordinal);
        var samples = SampleSaves.Load("PPSA01325");
        var options = FixtureVerifier.OptionsFor(fixture, manifest, new PluginViewDecoder(new AstroPlayroomPlugin()));
        var verification = FixtureVerifier.Verify(fixture, samples, samples, options);
        Assert.True(verification.Passed, string.Join('\n', verification.Expectations.Where(e => !e.Passed).Select(e => e.Message)));
        Assert.Equal(96, verification.Expectations.Count);
        Assert.Equal(27999, verification.Report.Totals.ChangedBytes);
        Assert.Equal(107, verification.Report.Totals.SemanticChanges);
        Assert.Equal(0, verification.Report.Totals.SemanticErrors);
        Assert.Equal(1, verification.Report.Totals.MirrorsMain);
        Assert.Empty(verification.Report.Before.Twins.Mismatches);
        Assert.Empty(verification.Report.After.Twins.Mismatches);
        var semantic = Assert.Single(verification.Report.Files, f => !f.IsBackup).Semantic!;
        Assert.False(semantic.Truncated);
        Assert.False(semantic.InputTruncated);
        Assert.Empty(semantic.Warnings);
        Assert.Equal(0, semantic.Added);
        Assert.Equal(0, semantic.Removed);
    }
}
