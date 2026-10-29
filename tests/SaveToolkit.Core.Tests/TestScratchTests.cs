using SaveToolkit.Testing;

namespace SaveToolkit.Core.Tests;

public sealed class TestScratchTests
{
    [Fact]
    public void SyntheticImportFolders_AreOwnedByTheProcessScratchFolder()
    {
        var first = TestZips.TempDir();
        var second = TestZips.TempDir();

        Assert.Equal(Path.GetFullPath(TestScratch.ProcessFolder), Path.GetDirectoryName(Path.GetFullPath(first)));
        Assert.Equal(Path.GetFullPath(TestScratch.ProcessFolder), Path.GetDirectoryName(Path.GetFullPath(second)));
        Assert.NotEqual(first, second);
    }

    [Fact]
    public void SweepStale_RemovesOnlyRunFoldersOfDeadProcesses()
    {
        var root = Path.Combine(Path.GetTempPath(), "SaveToolkit.ScratchTest", Guid.NewGuid().ToString("N"));
        var dead = Directory.CreateDirectory(Path.Combine(root, "111-aaaa")).FullName;
        File.WriteAllText(Path.Combine(dead, "sample.dat"), "x");
        var alive = Directory.CreateDirectory(Path.Combine(root, "222-bbbb")).FullName;
        var unrelated = Directory.CreateDirectory(Path.Combine(root, "keep-me")).FullName;
        try
        {
            var removed = TestScratch.SweepStale(root, pid => pid == 222);

            Assert.Equal(1, removed);
            Assert.False(Directory.Exists(dead));
            Assert.True(Directory.Exists(alive));
            Assert.True(Directory.Exists(unrelated));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void SweepStale_MissingRootIsNoOp()
    {
        Assert.Equal(0, TestScratch.SweepStale(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")), _ => false));
    }

    [Fact]
    public void ProcessFolder_LivesUnderConfiguredRoot()
    {
        var folder = TestScratch.ProcessFolder;

        Assert.True(Directory.Exists(folder));
        Assert.StartsWith(Path.GetFullPath(TestScratch.Root), Path.GetFullPath(folder), StringComparison.OrdinalIgnoreCase);
        Assert.StartsWith($"{Environment.ProcessId}-", Path.GetFileName(folder), StringComparison.Ordinal);
    }
}
