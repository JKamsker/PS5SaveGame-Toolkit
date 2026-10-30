using System.Text.Json;
using SaveToolkit.Core.Plugins;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Core.Storage;
using SaveToolkit.Core.Tests.Fakes;

namespace SaveToolkit.Core.Tests;

public sealed class WorkspaceIdleRaceTests
{
    private static readonly TimeSpan OperationTimeout = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task Sweep_skips_an_unleased_inflight_edit_and_preserves_the_committed_dirty_state()
    {
        var ct = TestContext.Current.CancellationToken;
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var resume = new ManualResetEventSlim();
        var plugin = new IdleRetentionPlugin();
        using var store = Store(plugin);
        var workspace = Create(store);
        var session = Open(workspace, plugin);
        plugin.BeforeEdit = () => { entered.TrySetResult(); resume.Wait(ct); };
        var edit = TestWorker.Run(() => Edit(session), ct);
        try
        {
            await TestWorker.WaitForSignalAsync(entered.Task, edit, ct);
            Assert.Equal(0, await TestWorker.Run(store.Sweep, ct).WaitAsync(OperationTimeout, ct));
            using var lease = Assert.IsType<WorkspaceLease>(store.Acquire(workspace.Id));
        }
        finally { plugin.BeforeEdit = null; resume.Set(); }
        Assert.True((await edit).Ok);
        Assert.True(session.State.IsDirty);
        Assert.Equal(0, store.Sweep());
        Assert.True(session.Undo().Ok);
        Assert.Equal(1, store.Sweep());
    }

    [Fact]
    public async Task Sweep_skips_an_inflight_open_without_blocking_store_access()
    {
        var ct = TestContext.Current.CancellationToken;
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var resume = new ManualResetEventSlim();
        var plugin = new IdleRetentionPlugin { OnOpen = () => { entered.TrySetResult(); resume.Wait(ct); } };
        using var store = Store(plugin);
        var workspace = Create(store);
        var opening = TestWorker.Run(() => Open(workspace, plugin), ct);
        try
        {
            await TestWorker.WaitForSignalAsync(entered.Task, opening, ct);
            Assert.Equal(0, await TestWorker.Run(store.Sweep, ct).WaitAsync(OperationTimeout, ct));
            using var lease = Assert.IsType<WorkspaceLease>(store.Acquire(workspace.Id));
        }
        finally { plugin.OnOpen = null; resume.Set(); }
        _ = await opening;
        Assert.Equal(1, store.Sweep());
        Assert.Equal(1, plugin.Disposals);
    }

    [Theory]
    [InlineData("access")]
    [InlineData("lease")]
    [InlineData("mutation")]
    [InlineData("remove")]
    [InlineData("shutdown")]
    public async Task Sweep_rechecks_snapshot_candidates_after_concurrent_activity(string activity)
    {
        var ct = TestContext.Current.CancellationToken;
        var cleaning = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var resume = new ManualResetEventSlim();
        var plugin = new IdleRetentionPlugin();
        using var store = Store(plugin);
        foreach (var workspace in new[] { Create(store), Create(store) }) Open(workspace, plugin);
        int cleanupCalls = 0;
        plugin.BeforeDispose = _ =>
        {
            if (Interlocked.Increment(ref cleanupCalls) != 1) return;
            cleaning.TrySetResult();
            resume.Wait(ct);
        };
        var sweep = TestWorker.Run(store.Sweep, ct);
        WorkspaceLease? lease = null;
        Workspace? remaining = null;
        try
        {
            await TestWorker.WaitForSignalAsync(cleaning.Task, sweep, ct);
            remaining = Assert.Single(store.All);
            switch (activity)
            {
                case "access": Assert.Same(remaining, store.Get(remaining.Id)); break;
                case "lease": lease = Assert.IsType<WorkspaceLease>(store.Acquire(remaining.Id)); break;
                case "mutation": Assert.True(Edit(Assert.Single(remaining.Documents)).Ok); break;
                case "remove": Assert.True(store.Remove(remaining.Id)); break;
                case "shutdown": store.Dispose(); break;
            }
        }
        finally { resume.Set(); }
        try
        {
            Assert.Equal(1, await sweep);
            if (activity is "remove" or "shutdown")
            {
                Assert.Empty(store.All);
                Assert.Equal(2, plugin.Disposals);
                Assert.False(Directory.Exists(remaining!.Set.StorageRoot));
            }
            else
            {
                Assert.Same(remaining, Assert.Single(store.All));
                var session = Assert.Single(remaining!.Documents);
                _ = session.BuildView();
                Assert.True(Directory.Exists(remaining.Set.StorageRoot));
                if (activity == "lease") Assert.Equal(0, store.Sweep());
                if (activity == "mutation") Assert.True(session.Undo().Ok);
                lease?.Dispose();
                await WaitForIdleCutoffAsync(remaining, ct);
                Assert.Equal(1, store.Sweep());
            }
        }
        finally { lease?.Dispose(); }
    }

    [Fact]
    public async Task Retirement_rejects_stale_session_writers_before_slow_plugin_disposal_finishes()
    {
        var ct = TestContext.Current.CancellationToken;
        var cleaning = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var resume = new ManualResetEventSlim();
        var plugin = new IdleRetentionPlugin();
        using var store = Store(plugin);
        var workspace = store.Create("two sessions", [
            new ImportSource("one.dat", () => new MemoryStream([1])),
            new ImportSource("two.dat", () => new MemoryStream([1]))]);
        var sessions = workspace.Set.AllDiskFiles.Select(file => workspace.Open(file.Id, plugin.Info.Id)).ToArray();
        foreach (var session in sessions) _ = session.BuildView();
        plugin.BeforeDispose = path =>
        {
            if (!cleaning.TrySetResult(path)) return;
            resume.Wait(ct);
        };
        var sweep = TestWorker.Run(store.Sweep, ct);
        try
        {
            await TestWorker.WaitForSignalAsync(cleaning.Task, sweep, ct);
            string disposingPath = await cleaning.Task;
            var notYetDisposed = sessions.Single(session => session.File.RelativePath != disposingPath);
            Assert.Null(store.Acquire(workspace.Id));
            Assert.Throws<ObjectDisposedException>(() => Edit(notYetDisposed));
            Assert.True(Directory.Exists(workspace.Set.StorageRoot));
        }
        finally { resume.Set(); }
        Assert.Equal(1, await sweep);
        Assert.Equal(2, plugin.Disposals);
        Assert.False(Directory.Exists(workspace.Set.StorageRoot));
    }

    [Fact]
    public void Reentrant_sweep_during_an_edit_cannot_retire_the_unpublished_mutation()
    {
        var plugin = new IdleRetentionPlugin();
        using var store = Store(plugin);
        var workspace = Create(store);
        var session = Open(workspace, plugin);
        plugin.BeforeEdit = () => Assert.Equal(0, store.Sweep());

        Assert.True(Edit(session).Ok);

        Assert.True(session.State.IsDirty);
        Assert.Same(workspace, store.Get(workspace.Id));
    }

    private static async Task WaitForIdleCutoffAsync(Workspace workspace, CancellationToken cancellationToken)
    {
        const int clockPollMilliseconds = 1;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(OperationTimeout);
        // Zero-timeout expiry still requires a cutoff strictly after the final access (including lease release).
        while (DateTimeOffset.UtcNow <= workspace.LastAccess)
            await Task.Delay(clockPollMilliseconds, timeout.Token);
    }

    private static WorkspaceStore Store(IdleRetentionPlugin plugin) => new(new PluginCatalog([plugin]),
        new() { DataRoot = TestZips.TempDir(), IdleTimeout = TimeSpan.Zero });
    private static Workspace Create(WorkspaceStore store) =>
        store.Create("idle race", [new ImportSource("save.dat", () => new MemoryStream([1]))]);
    private static DocumentSession Open(Workspace workspace, IdleRetentionPlugin plugin)
    {
        var session = workspace.Open(workspace.Set.AllDiskFiles.Single().Id, plugin.Info.Id);
        _ = session.BuildView();
        return session;
    }
    private static SessionEditResult Edit(DocumentSession session) =>
        session.ApplyEdits([new("value", JsonSerializer.SerializeToElement(2))]);
}
