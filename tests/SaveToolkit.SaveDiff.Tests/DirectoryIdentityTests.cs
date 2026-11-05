using SaveToolkit.Testing.Diff;
using static SaveToolkit.SaveDiff.Tests.TestSaves;

namespace SaveToolkit.SaveDiff.Tests;

public sealed class DirectoryIdentityTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LooseDirectoryDoesNotBecomeAConsoleGenerationOrTwin(bool reverse)
    {
        (string Path, byte[] Content)[] entries =
        [
            ($"{Title}/SLOT/data.bin", [9]),
            (Primary("data.bin"), [1]),
            (Backup("data.bin"), [1]),
        ];
        using var set = Load(reverse ? entries.Reverse().ToArray() : entries);
        var report = SaveSetDiffer.Compare(set.Set, set.Set);
        Assert.Equal(3, report.Files.Count);
        Assert.Equal(1, report.Before.Twins.Identical);
        Assert.Empty(report.Before.Twins.Mismatches);
        Assert.All(report.Files, file => Assert.Equal(FileDiffStatus.Unchanged, file.Status));
        var latest = SaveSetDiffer.Compare(set.Set, set.Set,
            new SaveDiffOptions { BeforeGeneration = GenerationSelector.Latest, AfterGeneration = GenerationSelector.Latest });
        Assert.Equal(3, latest.Files.Count);
    }

    [Fact]
    public void LooseDirectoryCannotSubstituteForMissingConsolePrimary()
    {
        using var set = Load(($"{Title}/SLOT/data.bin", [1]), (Backup("data.bin"), [1]));
        var report = SaveSetDiffer.Compare(set.Set, set.Set);
        Assert.Equal(0, report.Before.Twins.Identical);
        Assert.Empty(report.Before.Twins.Mismatches);
    }

    [Fact]
    public void MovingBetweenLooseAndConsoleDirectoriesIsNotAnUnchangedFile()
    {
        using var before = Load(($"{Title}/SLOT/data.bin", [1]));
        using var after = Load((Primary("data.bin"), [1]));
        var report = SaveSetDiffer.Compare(before.Set, after.Set);
        Assert.Equal(2, report.Files.Count);
        Assert.Equal(1, report.Totals.Added);
        Assert.Equal(1, report.Totals.Removed);
        Assert.True(report.HasChanges);
    }
}
