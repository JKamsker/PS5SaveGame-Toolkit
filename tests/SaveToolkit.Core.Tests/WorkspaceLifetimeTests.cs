using SaveToolkit.Core.Export;
using SaveToolkit.Core.Plugins;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Core.Storage;
using SaveToolkit.Core.Tests.Fakes;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Saves;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Core.Tests;

public sealed class WorkspaceLifetimeTests : IDisposable
{
    private readonly WorkspaceStore _store = new(new PluginCatalog([new CounterPlugin(), new GroupPlugin()]),
        new WorkspaceStoreOptions { DataRoot = TestZips.TempDir() });

    public void Dispose() => _store.Dispose();

    [Fact]
    public void DisposedWorkspace_CannotOpenAnotherSession()
    {
        var zip = TestZips.Build(("PPSA99999/sdimg_SAVE/save.dat", CounterPlugin.Create(1)));
        var workspace = _store.Create("lifetime", [new ImportSource("dump.zip", () => new MemoryStream(zip))]);
        var file = workspace.Set.AllDiskFiles.Single();
        var session = workspace.Open(file.Id, null);

        workspace.Dispose();
        workspace.Dispose();

        Assert.Throws<ObjectDisposedException>(() => session.BuildView());
        Assert.Throws<ObjectDisposedException>(() => workspace.Open(file.Id, null));
        Assert.Throws<ObjectDisposedException>(() => WorkspaceExporter.Plan(workspace, new()));
        Assert.Empty(workspace.Documents);
    }

    [Fact]
    public void DisposedWorkspace_CannotConfirmOrCreateGroupProposals()
    {
        const string root = "PPSA99990/sdimg_SAVE/";
        var zip = TestZips.Build((root + "character.dat", GroupPlugin.Create("C", 10, 7)),
            (root + "profile.dat", GroupPlugin.Create("P", 20, 9)));
        var workspace = _store.Create("group lifetime", [new ImportSource("dump.zip", () => new MemoryStream(zip))]);
        var anchor = workspace.Set.AllDiskFiles.Single(f => f.FileName == "character.dat");
        var proposal = Assert.Single(workspace.ProposeGroups(anchor.Id, GroupPlugin.PluginId));

        workspace.Dispose();

        Assert.Throws<ObjectDisposedException>(() => workspace.OpenGroup(proposal.Token));
        Assert.Throws<ObjectDisposedException>(() => workspace.ProposeGroups(anchor.Id, GroupPlugin.PluginId));
        Assert.Empty(workspace.Documents);
        Assert.Empty(workspace.DocumentGroups);
    }

    [Fact]
    public async Task Dispose_WaitsForAnOpeningSession_AndClosesIt()
    {
        var ct = TestContext.Current.CancellationToken;
        var opening = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var resume = new ManualResetEventSlim();
        var plugin = new BlockingPlugin(() =>
        {
            opening.SetResult();
            resume.Wait(ct);
        });
        using var store = new WorkspaceStore(new PluginCatalog([plugin]), new() { DataRoot = TestZips.TempDir() });
        var workspace = store.Create("concurrent lifetime", [new ImportSource("file.dat", () => new MemoryStream([1]))]);
        var file = workspace.Set.AllDiskFiles.Single();
        var open = TestWorker.Run(() => workspace.Open(file.Id, plugin.Info.Id), ct);
        Task? dispose = null;
        try
        {
            await TestWorker.WaitForSignalAsync(opening.Task, open, ct);
            dispose = TestWorker.Run(workspace.Dispose, ct);
            resume.Set();
            var session = await open;
            await dispose;

            Assert.Empty(workspace.Documents);
            Assert.Throws<ObjectDisposedException>(() => session.BuildView());
            Assert.Throws<ObjectDisposedException>(() => workspace.Open(file.Id, plugin.Info.Id));
        }
        finally
        {
            resume.Set();
            await Task.WhenAll(open, dispose ?? Task.CompletedTask);
        }
    }

    [Fact]
    public void Dispose_ClosesAllSessionsEvenWhenPluginCleanupThrows()
    {
        var disposed = 0;
        var plugin = new BlockingPlugin(() => { }, () =>
        {
            disposed++;
            throw new InvalidOperationException("Plugin cleanup failed.");
        });
        using var store = new WorkspaceStore(new PluginCatalog([plugin]), new() { DataRoot = TestZips.TempDir() });
        var workspace = store.Create("failing cleanup", Enumerable.Range(0, 3)
            .Select(i => new ImportSource($"PPSA00001/sdimg_SAVE/file{i}.dat", () => new MemoryStream([1]))));
        var sessions = workspace.Set.AllDiskFiles.Select(f => workspace.Open(f.Id, plugin.Info.Id)).ToArray();

        var failure = Assert.Throws<AggregateException>(workspace.Dispose);

        Assert.Equal(3, failure.InnerExceptions.Count);
        Assert.Equal(3, disposed);
        Assert.Empty(workspace.Documents);
        Assert.All(sessions, s => Assert.Throws<ObjectDisposedException>(() => s.BuildView()));
        workspace.Dispose();
        Assert.Equal(3, disposed);
    }

    [Fact]
    public void Sweep_ReleasesEveryExpiredWorkspaceWhenPluginCleanupThrows()
    {
        var plugin = new BlockingPlugin(() => { }, () => throw new InvalidOperationException("Plugin cleanup failed."));
        using var store = new WorkspaceStore(new PluginCatalog([plugin]),
            new() { DataRoot = TestZips.TempDir(), IdleTimeout = TimeSpan.Zero });
        var workspaces = Enumerable.Range(0, 3).Select(_ => store.Create("expired",
            [new ImportSource("save.dat", () => new MemoryStream([1]))])).ToArray();
        foreach (var workspace in workspaces) workspace.Open(workspace.Set.AllDiskFiles.Single().Id, plugin.Info.Id);

        var failure = Assert.Throws<AggregateException>(() => store.Sweep());

        Assert.Equal(3, failure.Flatten().InnerExceptions.Count);
        Assert.Empty(store.All);
        Assert.All(workspaces, workspace => Assert.False(Directory.Exists(workspace.Set.StorageRoot)));
        Assert.Equal(0, store.Sweep());
    }

    [Fact]
    public void StoreDisposal_ReleasesAllWorkspacesAndBlobsWhenPluginCleanupThrows()
    {
        var plugin = new BlockingPlugin(() => { }, () => throw new InvalidOperationException("Plugin cleanup failed."));
        using var store = new WorkspaceStore(new PluginCatalog([plugin]), new() { DataRoot = TestZips.TempDir() });
        var workspaces = Enumerable.Range(0, 2).Select(_ => store.Create("failing cleanup",
            [new ImportSource("save.dat", () => new MemoryStream([1]))])).ToArray();
        foreach (var workspace in workspaces) workspace.Open(workspace.Set.AllDiskFiles.Single().Id, plugin.Info.Id);

        var failure = Assert.Throws<AggregateException>(store.Dispose);

        Assert.Equal(2, failure.Flatten().InnerExceptions.Count);
        Assert.Empty(store.All);
        Assert.All(workspaces, workspace =>
        {
            Assert.Empty(workspace.Documents);
            Assert.False(Directory.Exists(workspace.Set.StorageRoot));
        });
        store.Dispose();
    }

    private sealed class BlockingPlugin(Action opening, Action? dispose = null) : IGamePlugin
    {
        public PluginInfo Info { get; } = new("test.lifetime", "Lifetime test", "1.0.0");
        public ProbeResult Probe(ISaveFile file) => ProbeResult.None;
        public ISaveDocument Open(ISaveFile file)
        {
            opening();
            return new EmptyDocument(dispose);
        }
    }

    private sealed class EmptyDocument(Action? dispose) : BoundSaveDocument
    {
        public override string Title => "Lifetime test";
        public override ViewNode BuildView() => new TextNode(Title);
        public override IReadOnlyList<FileOutput> Serialize() => [];
        protected override void Dispose(bool disposing)
        {
            try { dispose?.Invoke(); }
            finally { base.Dispose(disposing); }
        }
    }
}
