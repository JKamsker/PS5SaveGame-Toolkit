using SaveToolkit.Testing;

namespace SaveToolkit.SaveDiff.Tests;

public sealed class TestSavesTests
{
    [Fact]
    public void SyntheticFixtureFolders_AreOwnedByTheProcessScratchFolder()
    {
        var first = TestSaves.TempDir();
        var second = TestSaves.TempDir();
        try
        {
            Assert.Equal(Path.GetFullPath(TestScratch.ProcessFolder), Path.GetDirectoryName(Path.GetFullPath(first)));
            Assert.Equal(Path.GetFullPath(TestScratch.ProcessFolder), Path.GetDirectoryName(Path.GetFullPath(second)));
            Assert.NotEqual(first, second);
            Assert.True(Directory.Exists(first));
            Assert.True(Directory.Exists(second));
        }
        finally
        {
            Directory.Delete(first);
            Directory.Delete(second);
        }
    }
}
