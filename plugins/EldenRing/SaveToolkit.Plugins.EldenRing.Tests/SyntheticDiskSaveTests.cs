using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.EldenRing.Tests;

public sealed class SyntheticDiskSaveTests
{
    [Fact]
    public void ImportedFixtureOwnsAnIndependentCopyAndDeletesOnlyItsOwnFolder()
    {
        var bytes = new byte[] { 1, 2, 3 };
        using var first = new SyntheticDiskSave(bytes);
        using var second = new SyntheticDiskSave(bytes);
        Assert.Equal(Path.GetFullPath(TestScratch.ProcessFolder), Path.GetDirectoryName(first.Root));
        Assert.NotEqual(first.Root, second.Root);
        Assert.Equal("PPSA03169/sdimg_sce_sdmemory/memory.dat", first.File.RelativePath);
        bytes[0] = 9;
        Assert.Equal(new byte[] { 1, 2, 3 }, first.File.ReadAllBytes());
        first.Dispose();
        Assert.False(Directory.Exists(first.Root));
        Assert.Equal(new byte[] { 1, 2, 3 }, second.File.ReadAllBytes());
        first.Dispose(); // Also safe when a test's using scope disposes again.
        second.Dispose();
        Assert.False(Directory.Exists(second.Root));
    }

    [Fact]
    public void SyntheticSessionReviewReopensTheSameOriginalAndCleanupSurvivesATestException()
    {
        var fixture = new SyntheticSave([0]);
        string? root = null;
        const string failureMessage = "Simulated test failure after opening a session.";
        void SimulateTestFailure()
        {
            using var disk = new SyntheticDiskSave(fixture.Bytes);
            root = disk.Root;
            using var session = WriteTestSupport.Session(disk.File, out var document);
            _ = session.Expand("characters/0");
            Assert.True(session.ApplyEdits([WriteTestSupport.Incoming("slots/0/runes", 101)]).Ok);
            var change = Assert.Single(session.GetChanges().Changes);
            Assert.Equal("slots/0/runes", change.ValueId);
            Assert.Equal(SyntheticSave.InitialRunes, change.OriginalValue.AsUInt32());
            Assert.Equal(101U, change.CurrentValue.AsUInt32());
            Assert.Equal(fixture.Bytes, disk.File.ReadAllBytes());
            Assert.True(session.Undo().Ok);
            WriteTestSupport.EqualBytes(fixture.Bytes, document);
            throw new InvalidOperationException(failureMessage);
        }
        var error = Assert.Throws<InvalidOperationException>(SimulateTestFailure);
        Assert.Equal(failureMessage, error.Message);
        Assert.NotNull(root);
        Assert.False(Directory.Exists(root));
    }
}
