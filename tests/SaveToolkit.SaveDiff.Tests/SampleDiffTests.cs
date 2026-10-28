using SaveToolkit.Plugins.AstroPlayroom;
using SaveToolkit.Testing;
using SaveToolkit.Testing.Diff;

namespace SaveToolkit.SaveDiff.Tests;

/// <summary>
/// Real-sample checks (skipped when <c>artifacts/</c> is absent). Both samples hold two or more snapshots
/// in one ZIP, so generation 0 is compared with generation 1 of the same dump.
/// </summary>
public sealed class SampleDiffTests
{
    private static readonly SaveDiffOptions FirstTwoGenerations = new()
    {
        BeforeGeneration = GenerationSelector.Only(0),
        AfterGeneration = GenerationSelector.Only(1),
    };

    [Fact]
    public void AstroPlayroom_ByteMode_FindsChangedRanges_AndMirroredBackup()
    {
        var set = SampleSaves.Load("PPSA01325");

        var report = SaveSetDiffer.Compare(set, set, FirstTwoGenerations);

        var main = report.Files.Single(f => f is { Path: "memory.dat", IsBackup: false });
        Assert.Equal(FileDiffStatus.Changed, main.Status);
        Assert.Equal(1_048_576, main.Bytes!.AfterLength);
        Assert.InRange(main.Bytes.Ranges.Count, 1, new ByteDiffOptions().MaxRanges);
        Assert.Equal(FileDiffStatus.MirrorsMain, report.Files.Single(f => f is { Path: "memory.dat", IsBackup: true }).Status);
        Assert.Empty(report.Before.Twins.Mismatches);
    }

    [Fact]
    public void AstroPlayroom_SemanticMode_ThroughThePlugin_AlignsByValueId()
    {
        var set = SampleSaves.Load("PPSA01325");

        var report = SaveSetDiffer.Compare(set, set, FirstTwoGenerations with { Decoder = new PluginViewDecoder(new AstroPlayroomPlugin()) });

        var semantic = report.Files.Single(f => f is { Path: "memory.dat", IsBackup: false }).Semantic!;
        Assert.Null(semantic.Error);
        Assert.False(semantic.InputTruncated);
        // Known values from the plugin README: coins 0 → 1749 between the two snapshots.
        var coins = Assert.Single(semantic.Changes, c => c.Path.Contains("NumCoin", StringComparison.Ordinal));
        Assert.Equal(("0", "1749"), (coins.Before, coins.After));
    }

    [Fact]
    public void ItTakesTwo_JsonProfile_SemanticDiffAcrossGenerations()
    {
        var set = SampleSaves.Load("PPSA02343");

        var report = SaveSetDiffer.Compare(set, set, FirstTwoGenerations with { Decoder = new JsonSaveDecoder() });

        var profile = report.Files.Single(f => f is { Path: "ue4savegame.dpx.sav", IsBackup: false });
        Assert.Equal(FileDiffStatus.Changed, profile.Status);
        Assert.Null(profile.Semantic!.Error);
        Assert.True(profile.Semantic.Total > 0);
        // Generation 1 dropped several minigame flags of generation 0: removals, not offset shifts.
        Assert.Contains(profile.Semantic.Changes, c => c.Kind == SemanticChangeKind.Removed && c.Path.StartsWith("Profile/Minigame", StringComparison.Ordinal));
        Assert.Equal(profile.Semantic.Total, profile.Semantic.Changes.Count);
    }

    [Fact]
    public void FixtureVerifier_WorksOnSampleSets()
    {
        var set = SampleSaves.Load("PPSA01325");
        var fixture = new Fixture
        {
            Id = "ppsa01325-gen0-gen1",
            TitleId = "PPSA01325",
            Event = "unknown play session between two appended snapshots",
            Before = new SaveRef { Archive = "PPSA01325_saves_dec.zip", Generation = GenerationSelector.Only(0) },
            After = new SaveRef { Archive = "PPSA01325_saves_dec.zip", Generation = GenerationSelector.Only(1) },
            Files = ["memory.dat"],
            Expected =
            [
                new ExpectedChange { File = "memory.dat", Path = "/PreInstall[0]/Common[0]/NumCoin[0]/text[0]", Before = "0", After = "1749" },
                new ExpectedChange { File = "memory.dat", Offset = 0, Length = 16, Changed = false, Note = "zero header" },
            ],
        };

        var options = FixtureVerifier.OptionsFor(fixture, new FixtureManifest(), new PluginViewDecoder(new AstroPlayroomPlugin()));
        var verification = FixtureVerifier.Verify(fixture, set, set, options);

        Assert.True(verification.Passed, TextReportWriter.Render(verification));
    }
}
