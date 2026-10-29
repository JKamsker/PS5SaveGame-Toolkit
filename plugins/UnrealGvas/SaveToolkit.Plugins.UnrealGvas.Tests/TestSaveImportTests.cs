using System.Security.Cryptography;

namespace SaveToolkit.Plugins.UnrealGvas.Tests;

public sealed class TestSaveImportTests
{
    [Fact]
    public void SyntheticDiskFixtureHasDisposableOwnership()
    {
        using var import = HogwartsInventoryFixture.CreateDisk();
        byte[] expected = HogwartsInventoryFixture.Create().ReadAllBytes();
        Assert.Equal(expected, import.File.ReadAllBytes());
        Assert.Equal(expected.Length, import.File.Length);
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(expected)), import.File.Sha256);
        Assert.Equal("PPSA01603", import.File.Directory.TitleId);
        Assert.Equal("PPSA01603/sdimg_HL0000/save.sav", import.File.RelativePath);
        using var document = new UnrealGvasPlugin().Open(import.File);
        Assert.Equal(expected, Assert.Single(document.Serialize()).Content.ToArray());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NormalAndExceptionalExitRemoveOnlyTheOwnedImport(bool throwInside)
    {
        byte[] expected = GvasFixture.Primitives();
        using var survivor = new TestSaveImport(expected);
        string? removedRoot = null;
        void Run()
        {
            using var import = new TestSaveImport(expected);
            removedRoot = import.Root;
            Assert.NotEqual(survivor.Root, import.Root);
            Assert.True(Directory.Exists(import.Root));
            Assert.Equal(expected, import.File.ReadAllBytes());
            if (throwInside) throw new InvalidOperationException("fixture body failed");
        }

        if (throwInside) Assert.Equal("fixture body failed", Assert.Throws<InvalidOperationException>(Run).Message);
        else Run();

        Assert.NotNull(removedRoot);
        Assert.False(Directory.Exists(removedRoot));
        Assert.Equal(expected, survivor.File.ReadAllBytes());
        survivor.Dispose();
        survivor.Dispose(); // Cleanup is safe when a test explicitly closes its owner before scope exit.
        Assert.False(Directory.Exists(survivor.Root));
    }
}
