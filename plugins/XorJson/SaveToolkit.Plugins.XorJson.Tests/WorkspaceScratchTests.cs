using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.XorJson.Tests;

public sealed class WorkspaceScratchTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NestedScratchFilesAreRemovedAfterSuccessOrFailure(bool fail)
    {
        string root = "";
        void Run()
        {
            using var scratch = new WorkspaceScratch();
            root = scratch.Root;
            Assert.Equal(Path.GetFullPath(TestScratch.ProcessFolder), Path.GetDirectoryName(root));
            var nested = Directory.CreateDirectory(Path.Combine(root, "nested"));
            File.WriteAllText(Path.Combine(nested.FullName, "fixture.txt"), "synthetic fixture");
            if (fail) throw new InvalidOperationException("Simulated assertion failure.");
        }

        if (fail) Assert.Throws<InvalidOperationException>(Run);
        else Run();

        Assert.False(Directory.Exists(root));
    }
}
