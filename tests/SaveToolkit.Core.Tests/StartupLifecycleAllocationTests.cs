using System.Runtime.CompilerServices;
using SaveToolkit.Core.Plugins;
using SaveToolkit.Core.References;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Core.Storage;
using SaveToolkit.Plugins.GenericJson;
using SaveToolkit.Testing;

namespace SaveToolkit.Core.Tests;

[Collection(typeof(AllocationTestCollection))]
public sealed class StartupLifecycleAllocationTests(ITestOutputHelper output)
{
    private const int Cycles = 20;
    private const long BatchAllocationBudget = 2 * 1024 * 1024;

    [Fact]
    public void ClosedSyntheticWorkspacesAndSessionsAreCollectible()
    {
        var root = TestZips.TempDir();
        var catalog = PluginCatalog.Discover();
        var packs = new ReferencePackStore(catalog.Plugins);
        var zip = TestZips.Build(("PPSA99998/sdimg_STARTUP/data.json", "[0,1,2,3]"u8.ToArray()));
        var references = new List<WeakReference>();
        try
        {
            using var store = new WorkspaceStore(catalog, new() { DataRoot = root }, packs);
            var allocated = AllocationProbe.Measure(() =>
            {
                for (var cycle = 0; cycle < Cycles; cycle++) Cycle(store, zip, references);
            }, beforeRun: references.Clear);
            output.WriteLine($"{Cycles} import/open/view/close/remove cycles: {allocated}");
            allocated.AssertAtMost(BatchAllocationBudget, "Small closed workspaces must not accumulate session allocation.");
            Assert.Empty(store.All);
            Assert.Empty(Directory.EnumerateFileSystemEntries(root));
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            Assert.Equal(Cycles * 2, references.Count);
            Assert.All(references, reference => Assert.False(reference.IsAlive));
            GC.KeepAlive(store);
        }
        finally { Cleanup(root); }
    }

    // Keep locals out of the asserting frame so JIT liveness cannot retain the final cycle.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Cycle(WorkspaceStore store, byte[] zip, List<WeakReference> references)
    {
        var workspace = store.Create("synthetic-startup", [new ImportSource("synthetic.zip", () => new MemoryStream(zip, writable: false))]);
        var file = workspace.Set.AllDiskFiles.Single();
        var session = workspace.Open(file.Id, new GenericJsonPlugin().Info.Id);
        session.BuildView();
        references.Add(new(workspace));
        references.Add(new(session));
        Assert.True(workspace.Close(session.Id));
        Assert.True(store.Remove(workspace.Id));
    }

    private static void Cleanup(string root)
    {
        if (Path.GetDirectoryName(root) != Path.GetFullPath(TestScratch.ProcessFolder))
            throw new InvalidOperationException("Test directory escaped scratch.");
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }
}
