using Microsoft.Extensions.Logging;
using SaveToolkit.Api.Infrastructure;
using SaveToolkit.Core.Plugins;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Core.Storage;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Saves;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Core.Tests;

public sealed class WorkspaceSweeperTests
{
    [Fact]
    public async Task CleanupFailure_IsLogged_AndTheNextScheduledSweepStillRuns()
    {
        var ct = TestContext.Current.CancellationToken;
        var cleaned = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var plugin = new CleanupPlugin(() => cleaned.TrySetResult());
        using var store = new WorkspaceStore(new PluginCatalog([plugin]),
            new() { DataRoot = TestZips.TempDir(), IdleTimeout = TimeSpan.Zero });
        var first = Open(store, plugin);
        var clock = new TickClock();
        var logger = new SweepLogger();
        using var sweeper = new WorkspaceSweeper(new WorkspaceSessions(store), logger, clock);
        await sweeper.StartAsync(ct);
        try
        {
            var timer = await clock.Created.Task.WaitAsync(TimeSpan.FromSeconds(10), ct);
            timer.Tick();
            await cleaned.Task.WaitAsync(TimeSpan.FromSeconds(10), ct);
            var completion = await Task.WhenAny(logger.Warning.Task, sweeper.ExecuteTask!).WaitAsync(TimeSpan.FromSeconds(10), ct);
            Assert.Same(logger.Warning.Task, completion);
            Assert.False(Directory.Exists(first.Set.StorageRoot));

            cleaned = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var second = Open(store, plugin);
            timer.Tick();
            await cleaned.Task.WaitAsync(TimeSpan.FromSeconds(10), ct);
            Assert.True(SpinWait.SpinUntil(() => !Directory.Exists(second.Set.StorageRoot), TimeSpan.FromSeconds(10)));
            Assert.False(sweeper.ExecuteTask!.IsCompleted);
        }
        finally { await sweeper.StopAsync(ct); }

        Assert.True(sweeper.ExecuteTask!.IsCompletedSuccessfully || sweeper.ExecuteTask.IsCanceled);
    }

    private static Workspace Open(WorkspaceStore store, IGamePlugin plugin)
    {
        var workspace = store.Create("sweep", [new ImportSource("save.dat", () => new MemoryStream([1]))]);
        workspace.Open(workspace.Set.AllDiskFiles.Single().Id, plugin.Info.Id);
        return workspace;
    }

    private sealed class CleanupPlugin(Action cleanup) : IGamePlugin
    {
        public PluginInfo Info { get; } = new("test.sweeper", "Sweeper test", "1.0.0");
        public ProbeResult Probe(ISaveFile file) => ProbeResult.None;
        public ISaveDocument Open(ISaveFile file) => new CleanupDocument(cleanup);
    }

    private sealed class CleanupDocument(Action cleanup) : BoundSaveDocument
    {
        public override string Title => "Sweeper test";
        public override ViewNode BuildView() => new TextNode(Title);
        public override IReadOnlyList<FileOutput> Serialize() => [];
        protected override void Dispose(bool disposing)
        {
            cleanup();
            base.Dispose(disposing);
            throw new InvalidOperationException("Injected disposal failure.");
        }
    }

    private sealed class SweepLogger : ILogger<WorkspaceSweeper>
    {
        public TaskCompletionSource Warning { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Warning && exception is AggregateException) Warning.TrySetResult();
        }
    }

    private sealed class TickClock : TimeProvider
    {
        public TaskCompletionSource<TickTimer> Created { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = new TickTimer(callback, state);
            Created.SetResult(timer);
            return timer;
        }
    }

    private sealed class TickTimer(TimerCallback callback, object? state) : ITimer
    {
        private bool _disposed;
        public void Tick() { if (!_disposed) callback(state); }
        public bool Change(TimeSpan dueTime, TimeSpan period) => !_disposed;
        public void Dispose() => _disposed = true;
        public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
    }
}
