using SaveToolkit.Core.Plugins;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Core.Storage;

namespace SaveToolkit.Core.Tests;

public sealed class WorkspaceStoreConcurrencyTests
{
    [Fact]
    public void Create_AfterDisposal_DoesNotReadSourcesOrCreateBlobs()
    {
        var root = TestZips.TempDir();
        using var store = new WorkspaceStore(new PluginCatalog([]), new() { DataRoot = root });
        store.Dispose();
        var sourceOpened = false;

        Assert.Throws<ObjectDisposedException>(() => store.Create("closed", [new ImportSource("save.dat", () =>
        {
            sourceOpened = true;
            return new MemoryStream([1]);
        })]));

        Assert.False(sourceOpened);
        Assert.Empty(store.All);
        Assert.Empty(Directory.EnumerateFileSystemEntries(root));
    }

    [Fact]
    public async Task Import_CompletingAfterDisposal_IsNotPublishedAndDeletesBlobs()
    {
        var ct = TestContext.Current.CancellationToken;
        using var importing = new ManualResetEventSlim();
        using var resume = new ManualResetEventSlim();
        var root = TestZips.TempDir();
        using var store = new WorkspaceStore(new PluginCatalog([]), new() { DataRoot = root });
        var import = Task.Run(() => store.Create("racing import", [new ImportSource("save.dat", () =>
        {
            importing.Set();
            Assert.True(resume.Wait(TimeSpan.FromSeconds(10), ct));
            return new MemoryStream([1]);
        })]), ct);
        try
        {
            Assert.True(importing.Wait(TimeSpan.FromSeconds(10), ct));
            store.Dispose();
            resume.Set();

            await Assert.ThrowsAsync<ObjectDisposedException>(async () => await import);
            Assert.Empty(store.All);
            Assert.Empty(Directory.EnumerateFileSystemEntries(root));
        }
        finally
        {
            resume.Set();
            try { await import; }
            catch (ObjectDisposedException) { }
        }
    }
}
