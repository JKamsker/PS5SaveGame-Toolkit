using SaveToolkit.Sdk;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.GenericJson.Tests;

public sealed class JsonSessionFixtureTests
{
    [Theory]
    [InlineData("{")]
    [InlineData("\"\\uD800\"")]
    public void FailedConstructionRemovesImportedFiles(string invalidSource)
    {
        var storage = NewStorageDirectory();
        try
        {
            Assert.Throws<SaveFormatException>(() => new JsonSessionFixture(invalidSource, storageDirectory: storage));
            Assert.False(Directory.Exists(storage), "A failed fixture must remove its imported files.");
        }
        finally
        {
            if (Directory.Exists(storage)) Directory.Delete(storage, recursive: true);
        }
    }

    [Fact]
    public void DisposalRemovesOnlyOwnedFilesAndCanBeRepeated()
    {
        var storage = NewStorageDirectory();
        using var peer = new JsonSessionFixture("[2]");
        using var fixture = new JsonSessionFixture("[1]", storageDirectory: storage);
        Assert.True(Directory.Exists(storage));
        fixture.Dispose();
        Assert.False(Directory.Exists(storage));
        Assert.Equal("[2]"u8.ToArray(), peer.Bytes);
        fixture.Dispose();
    }

    private static string NewStorageDirectory() => Path.Combine(TestScratch.ProcessFolder, "fixture-" + Guid.NewGuid().ToString("N"));
}
