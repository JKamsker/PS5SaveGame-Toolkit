using SaveToolkit.Testing.Diff;
using static SaveToolkit.SaveDiff.Tests.TestSaves;

namespace SaveToolkit.SaveDiff.Tests;

public sealed class SaveSetDifferTests
{
    private static readonly byte[] A = Bytes(32, 0xA0);
    private static readonly byte[] B = With(A, 4, 0xB0);
    private static readonly byte[] C = With(A, 8, 0xC0);

    [Fact]
    public void DuplicateZipEntries_BecomeGenerations_AndPairByGeneration()
    {
        // before: g0 = A, g1 = B; after: g0 = A, g1 = C  → only g1 changed.
        using var before = Load((Primary("data.bin"), A), (Primary("data.bin"), B));
        using var after = Load((Primary("data.bin"), A), (Primary("data.bin"), C));

        var report = SaveSetDiffer.Compare(before.Set, after.Set);

        Assert.Equal(2, report.Before.Generations);
        Assert.Collection(report.Files,
            g0 => Assert.Equal((0, FileDiffStatus.Unchanged), (g0.BeforeGeneration!.Value, g0.Status)),
            g1 => Assert.Equal((1, FileDiffStatus.Changed), (g1.AfterGeneration!.Value, g1.Status)));
    }

    [Fact]
    public void GenerationSelectors_CompareTwoSnapshotsOfOneDump()
    {
        using var dump = Load((Primary("data.bin"), A), (Primary("data.bin"), B), (Primary("data.bin"), C));

        var options = new SaveDiffOptions { BeforeGeneration = GenerationSelector.Only(0), AfterGeneration = GenerationSelector.Latest };
        var report = SaveSetDiffer.Compare(dump.Set, dump.Set, options);

        var file = Assert.Single(report.Files);
        Assert.Equal((0, 2), (file.BeforeGeneration, file.AfterGeneration));
        Assert.Equal(FileDiffStatus.Changed, file.Status);
        Assert.Equal(8, Assert.Single(file.Bytes!.Ranges).Offset);
    }

    [Fact]
    public void AddedAndRemovedFiles_AreReported()
    {
        using var before = Load((Primary("old.bin"), A), (Primary("same.bin"), A));
        using var after = Load((Primary("new.bin"), B), (Primary("same.bin"), A));

        var report = SaveSetDiffer.Compare(before.Set, after.Set);

        Assert.Equal(FileDiffStatus.Added, report.Files.Single(f => f.Path == "new.bin").Status);
        Assert.Equal(FileDiffStatus.Removed, report.Files.Single(f => f.Path == "old.bin").Status);
        Assert.Equal(FileDiffStatus.Unchanged, report.Files.Single(f => f.Path == "same.bin").Status);
        Assert.Equal((1, 1, 1), (report.Totals.Added, report.Totals.Removed, report.Totals.Unchanged));
        Assert.True(report.HasChanges);
    }

    [Fact]
    public void BackupTwins_ArePaired_AndMirroredBackupsCollapsed()
    {
        using var before = Load((Primary("data.bin"), A), (Backup("data.bin"), A));
        using var after = Load((Primary("data.bin"), B), (Backup("data.bin"), B));

        var report = SaveSetDiffer.Compare(before.Set, after.Set);

        Assert.Equal(1, report.Before.Twins.Identical);
        Assert.Empty(report.After.Twins.Mismatches);
        Assert.Equal(FileDiffStatus.Changed, report.Files.Single(f => !f.IsBackup).Status);
        var backup = report.Files.Single(f => f.IsBackup);
        Assert.Equal(FileDiffStatus.MirrorsMain, backup.Status);
        Assert.Null(backup.Bytes);

        var full = SaveSetDiffer.Compare(before.Set, after.Set, new SaveDiffOptions { CollapseMirroredBackups = false });
        Assert.Equal(FileDiffStatus.Changed, full.Files.Single(f => f.IsBackup).Status);
    }

    [Fact]
    public void DivergingAndMissingTwins_AreListedAsMismatches()
    {
        using var set = Load((Primary("data.bin"), A), (Backup("data.bin"), B), (Primary("only-main.bin"), A));

        var report = SaveSetDiffer.Compare(set.Set, set.Set);

        Assert.Contains(report.Before.Twins.Mismatches, m => m.Path == "data.bin" && m.Status == TwinStatus.Differs);
        Assert.Contains(report.Before.Twins.Mismatches, m => m.Path == "only-main.bin" && m.Status == TwinStatus.MissingBackup);
        Assert.False(report.HasChanges);
    }

    [Fact]
    public void ByteChurnRule_MasksOnlyItsRange()
    {
        using var before = Load((Primary("data.bin"), A));
        using var after = Load((Primary("data.bin"), With(A, 4, 1, 2, 3, 4)));
        var timestampOnly = new ChurnRule { TitleId = Title, File = "*.bin", Offset = 4, Length = 4, Reason = "save timestamp" };

        var masked = SaveSetDiffer.Compare(before.Set, after.Set, new SaveDiffOptions { Churn = new ChurnFilter([timestampOnly]) });
        var partly = SaveSetDiffer.Compare(before.Set, after.Set, new SaveDiffOptions { Churn = new ChurnFilter([timestampOnly with { Length = 2 }]) });

        var file = Assert.Single(masked.Files);
        Assert.Equal(FileDiffStatus.ChurnOnly, file.Status);
        Assert.Equal(4, file.Bytes!.IgnoredBytes);
        Assert.False(masked.HasChanges);
        Assert.Contains("save timestamp", Assert.Single(masked.IgnoreRules), StringComparison.Ordinal);

        var partial = Assert.Single(partly.Files);
        Assert.Equal(FileDiffStatus.Changed, partial.Status);
        Assert.Equal((2L, 2L), (partial.Bytes!.ChangedBytes, partial.Bytes.IgnoredBytes));
        Assert.Equal(6, Assert.Single(partial.Bytes.Ranges).Offset);
    }

    [Fact]
    public void ChurnRules_ApplyPerTitle_AndWholeFileRulesSkipTheDiff()
    {
        using var before = Load((Primary("clock.dat"), A), (Primary("data.bin"), A));
        using var after = Load((Primary("clock.dat"), B), (Primary("data.bin"), B));
        var rules = new ChurnFilter(
        [
            new ChurnRule { TitleId = Title, File = "clock.dat", Reason = "rewritten on every save" },
            new ChurnRule { TitleId = "PPSA00001", File = "data.bin", Reason = "other title" },
        ]);

        var report = SaveSetDiffer.Compare(before.Set, after.Set, new SaveDiffOptions { Churn = rules });

        var clock = report.Files.Single(f => f.Path == "clock.dat");
        Assert.Equal(FileDiffStatus.ChurnOnly, clock.Status);
        Assert.Contains("rewritten on every save", clock.Note, StringComparison.Ordinal);
        Assert.Equal(FileDiffStatus.Changed, report.Files.Single(f => f.Path == "data.bin").Status);
    }

    [Fact]
    public void ChurnRules_RequireAReason()
    {
        Assert.Throws<ArgumentException>(() => new ChurnFilter([new ChurnRule { File = "*" }]));
        Assert.Throws<ArgumentException>(() => new ChurnFilter([new ChurnRule { Offset = 4, Reason = "no length" }]));
    }

    [Fact]
    public void SystemFiles_AreExcludedUnlessRequested_AndFileFiltersApply()
    {
        using var before = Load((Primary("sce_sys/param.sfo"), A), (Primary("data.bin"), A), (Primary("other.bin"), A));
        using var after = Load((Primary("sce_sys/param.sfo"), B), (Primary("data.bin"), B), (Primary("other.bin"), B));

        var defaults = SaveSetDiffer.Compare(before.Set, after.Set);
        var withSystem = SaveSetDiffer.Compare(before.Set, after.Set, new SaveDiffOptions { IncludeSystemFiles = true });
        var filtered = SaveSetDiffer.Compare(before.Set, after.Set, new SaveDiffOptions { FileFilters = ["data.*"] });

        Assert.DoesNotContain(defaults.Files, f => f.Path.StartsWith("sce_sys/", StringComparison.Ordinal));
        Assert.Contains(withSystem.Files, f => f.Path == "sce_sys/param.sfo");
        Assert.Equal("data.bin", Assert.Single(filtered.Files).Path);
    }

    [Fact]
    public void FolderInput_IsReadLikeAZip()
    {
        var root = Path.Combine(TempDir(), Title);
        Directory.CreateDirectory(Path.Combine(root, "sdimg_SLOT"));
        File.WriteAllBytes(Path.Combine(root, "sdimg_SLOT", "data.bin"), A);
        using var folder = SaveSetLoader.Load(root);
        using var zip = Load((Primary("data.bin"), A));

        var report = SaveSetDiffer.Compare(folder.Set, zip.Set);

        var file = Assert.Single(report.Files);
        Assert.Equal((Title, "SLOT", FileDiffStatus.Unchanged), (file.TitleId, file.Directory, file.Status));
    }

    [Fact]
    public void Dispose_RemovesTheTemporaryImport()
    {
        var loaded = Load((Primary("data.bin"), A));
        var root = loaded.Set.StorageRoot;
        Assert.StartsWith(Path.GetTempPath(), root, StringComparison.OrdinalIgnoreCase);

        loaded.Dispose();

        Assert.False(Directory.Exists(root));
    }
}
