using SaveToolkit.Core.Plugins;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Core.Storage;
using SaveToolkit.Core.Tests.Fakes;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Saves;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Core.Tests;

public sealed class WorkspaceOpenCancellationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CancelledReplacement_DisposesCandidateAndRetainsExistingOwner(bool duringView)
    {
        using var cancellation = new CancellationTokenSource();
        var candidate = new CandidateDocument(duringView ? cancellation.Cancel : () => { });
        var plugin = new CandidatePlugin(() =>
        {
            if (!duringView) cancellation.Cancel();
            return candidate;
        });
        using var store = new WorkspaceStore(new PluginCatalog([new CounterPlugin(), plugin]),
            new() { DataRoot = TestZips.TempDir() });
        var ws = store.Create("replacement", [new ImportSource("counter.dat", () => new MemoryStream(CounterPlugin.Create(1)))]);
        var file = ws.Set.AllDiskFiles.Single();
        var previous = ws.Open(file.Id, "test.counter");

        Assert.ThrowsAny<OperationCanceledException>(() => ws.Open(file.Id, plugin.Info.Id, out _, cancellation.Token));

        Assert.True(candidate.Disposed);
        Assert.Same(previous, Assert.Single(ws.Documents));
        _ = previous.BuildView();
    }

    [Fact]
    public async Task CancelledWaiter_ExitsWhileAnotherOpenStillOwnsTheGate()
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        using var cancellation = new CancellationTokenSource();
        using var resume = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var plugin = new CandidatePlugin(() =>
        {
            entered.TrySetResult();
            resume.Wait(timeout.Token);
            return new CandidateDocument(() => { });
        });
        using var store = new WorkspaceStore(new PluginCatalog([plugin]), new() { DataRoot = TestZips.TempDir() });
        var ws = store.Create("waiter", [new ImportSource("save.dat", () => new MemoryStream([1]))]);
        var file = ws.Set.AllDiskFiles.Single();
        var holder = TestWorker.Run(() => ws.Open(file.Id, plugin.Info.Id), timeout.Token);
        try
        {
            await TestWorker.WaitForSignalAsync(entered.Task, holder, timeout.Token);
            var waiter = TestWorker.Run(() => ws.Open(file.Id, plugin.Info.Id, out _, cancellation.Token), timeout.Token);
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiter.WaitAsync(timeout.Token));
            Assert.False(holder.IsCompleted);
        }
        finally { resume.Set(); await holder; }
    }

    private sealed class CandidatePlugin(Func<ISaveDocument> open) : IGamePlugin
    {
        public PluginInfo Info { get; } = new("test.candidate", "Candidate", "1.0.0");
        public ProbeResult Probe(ISaveFile file) => ProbeResult.None;
        public ISaveDocument Open(ISaveFile file) => open();
    }

    private sealed class CandidateDocument(Action buildView) : BoundSaveDocument
    {
        public bool Disposed { get; private set; }
        public override string Title => "Candidate";
        public override ViewNode BuildView() { buildView(); return new SectionNode("Empty", []); }
        public override IReadOnlyList<FileOutput> Serialize() => [];
        protected override void Dispose(bool disposing) { Disposed = true; base.Dispose(disposing); }
    }
}
