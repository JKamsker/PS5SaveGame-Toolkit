using SaveToolkit.Core.Plugins;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Core.Storage;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Saves;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Core.Tests;

public sealed class WorkspaceLeaseTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RemovalOrShutdown_WaitsForEveryLease_AndEachLeaseReleasesOnce(bool shutdown)
    {
        var plugin = new CleanupPlugin(() => { });
        using var store = Store(plugin);
        var workspace = Create(store);
        var session = workspace.Open(workspace.Set.AllDiskFiles.Single().Id, plugin.Info.Id);
        using var first = Assert.IsType<WorkspaceLease>(store.Acquire(workspace.Id));
        using var second = Assert.IsType<WorkspaceLease>(store.Acquire(workspace.Id));

        if (shutdown) store.Dispose();
        else Assert.True(store.Remove(workspace.Id));

        Assert.Empty(store.All);
        Assert.Null(store.Acquire(workspace.Id));
        Assert.True(Directory.Exists(workspace.Set.StorageRoot));
        Parallel.Invoke(first.Dispose, first.Dispose);
        Assert.True(Directory.Exists(workspace.Set.StorageRoot));
        _ = session.BuildView();

        second.Dispose();

        Assert.False(Directory.Exists(workspace.Set.StorageRoot));
        Assert.Throws<ObjectDisposedException>(() => session.BuildView());
    }

    [Fact]
    public void Sweep_SkipsActiveOperations_AndReclaimsAfterRelease()
    {
        using var store = Store(new CleanupPlugin(() => { }));
        var workspace = Create(store);
        using var lease = Assert.IsType<WorkspaceLease>(store.Acquire(workspace.Id));

        Assert.Equal(0, store.Sweep());
        Assert.Same(workspace, store.Get(workspace.Id));
        lease.Dispose();

        Assert.Equal(1, store.Sweep());
        Assert.Empty(store.All);
        Assert.False(Directory.Exists(workspace.Set.StorageRoot));
    }

    [Fact]
    public void Sweep_RechecksAccessAfterAnotherWorkspaceCleanup()
    {
        WorkspaceStore? activeStore = null;
        var plugin = new CleanupPlugin(() =>
        {
            // Cleanup can take arbitrarily long; another request may touch any remaining workspace.
            foreach (var remaining in activeStore!.All) activeStore.Get(remaining.Id);
        });
        using var store = Store(plugin);
        activeStore = store;
        foreach (var workspace in new[] { Create(store), Create(store) })
            workspace.Open(workspace.Set.AllDiskFiles.Single().Id, plugin.Info.Id);

        Assert.Equal(1, store.Sweep());

        var survivor = Assert.Single(store.All);
        Assert.True(Directory.Exists(survivor.Set.StorageRoot));
        _ = Assert.Single(survivor.Documents).BuildView();
    }

    private static WorkspaceStore Store(IGamePlugin plugin) => new(new PluginCatalog([plugin]),
        new() { DataRoot = TestZips.TempDir(), IdleTimeout = TimeSpan.Zero });

    private static Workspace Create(WorkspaceStore store) =>
        store.Create("leased workspace", [new ImportSource("save.dat", () => new MemoryStream([1]))]);

    private sealed class CleanupPlugin(Action cleanup) : IGamePlugin
    {
        public PluginInfo Info { get; } = new("test.lease", "Lease test", "1.0.0");
        public ProbeResult Probe(ISaveFile file) => ProbeResult.None;
        public ISaveDocument Open(ISaveFile file) => new CleanupDocument(cleanup);
    }

    private sealed class CleanupDocument(Action cleanup) : BoundSaveDocument
    {
        public override string Title => "Lease test";
        public override ViewNode BuildView() => new TextNode(Title);
        public override IReadOnlyList<FileOutput> Serialize() => [];
        protected override void Dispose(bool disposing)
        {
            try { cleanup(); }
            finally { base.Dispose(disposing); }
        }
    }
}
