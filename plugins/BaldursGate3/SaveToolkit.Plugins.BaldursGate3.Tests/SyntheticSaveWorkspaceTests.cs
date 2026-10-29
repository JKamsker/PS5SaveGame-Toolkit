using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.BaldursGate3.Tests;

public sealed class SyntheticSaveWorkspaceTests
{
    [Fact]
    public void DisposingRemovesTheOwnedRootAndImportedFiles()
    {
        string root;
        using (var workspace = new SyntheticSaveWorkspace("Globals.lsf", ResourceFixture.Create()))
        {
            root = workspace.RootPath;
            Assert.Equal(Path.GetFullPath(TestScratch.ProcessFolder), Path.GetDirectoryName(root));
            Assert.True(Directory.Exists(root));
            Assert.Equal("Globals.lsf", workspace.File.FileName);
            Assert.Equal("PPSA18463/sdimg_SAVE/Globals.lsf", workspace.File.RelativePath);
            Assert.Equal(ResourceFixture.Create(), workspace.File.ReadAllBytes());
            using var document = workspace.Plugin.Open(workspace.File);
            Assert.Equal(ResourceFixture.Create(), Assert.Single(document.Serialize()).Content.ToArray());
        }
        Assert.False(Directory.Exists(root));
    }

    [Fact]
    public void ExceptionalTestExitStillRemovesTheOwnedRoot()
    {
        string? root = null;
        var expected = new InvalidOperationException("Simulated test failure.");
        Action failAfterImport = () =>
        {
            using var workspace = new SyntheticSaveWorkspace("Globals.lsf", ResourceFixture.Create());
            root = workspace.RootPath;
            throw expected;
        };
        var actual = Assert.Throws<InvalidOperationException>(failAfterImport);
        Assert.Same(expected, actual);
        Assert.NotNull(root);
        Assert.False(Directory.Exists(root));
    }

    [Fact]
    public void FixtureCleanupCannotRemoveAnotherFixturesFilesAndIsRepeatable()
    {
        using var first = new SyntheticSaveWorkspace("first.lsf", ResourceFixture.Create());
        using var second = new SyntheticSaveWorkspace("second.lsf", LsmfFixture.Resource(LiveNewAgeFixture.Create()));
        Assert.NotEqual(first.RootPath, second.RootPath);
        first.Dispose();
        first.Dispose();
        Assert.False(Directory.Exists(first.RootPath));
        Assert.True(Directory.Exists(second.RootPath));
        Assert.Equal(LsmfFixture.Resource(LiveNewAgeFixture.Create()), second.File.ReadAllBytes());
        second.Dispose();
        Assert.False(Directory.Exists(second.RootPath));
    }
}
