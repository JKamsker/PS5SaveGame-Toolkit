using System.Text.Json;
using SaveToolkit.Core.Export;
using SaveToolkit.Core.Plugins;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Core.Storage;
using SaveToolkit.Core.Tests.Fakes;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Saves;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Core.Tests;

public sealed class ExportCancellationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Concurrent_export_observes_cancellation_while_session_or_ownership_gate_is_held(bool ownershipGate)
    {
        var ct = TestContext.Current.CancellationToken;
        using var cancellation = new CancellationTokenSource();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var plugin = new CallbackPlugin();
        using var store = new WorkspaceStore(new PluginCatalog([plugin]), new() { DataRoot = TestZips.TempDir() });
        var workspace = store.Create("test", [new ImportSource("save.dat", () => new MemoryStream(CounterPlugin.Create(5)))]);
        var session = workspace.Open(Assert.Single(workspace.Set.AllDiskFiles).Id, plugin.Info.Id);
        _ = session.BuildView();
        Assert.True(session.ApplyEdits([new IncomingEdit("counter", JsonSerializer.SerializeToElement("6"))]).Ok);
        plugin.Callback = stage =>
        {
            if (stage != "serialize") return;
            entered.TrySetResult();
            release.Wait(ct);
        };
        var holder = TestWorker.Run(() =>
        {
            if (ownershipGate) _ = WorkspaceExporter.Plan(workspace, new());
            else _ = session.Serialize();
        }, ct);
        try
        {
            await TestWorker.WaitForSignalAsync(entered.Task, holder, ct);
            var waiter = TestWorker.Run(() =>
            {
                started.SetResult();
                return WorkspaceExporter.Plan(workspace, new(), cancellation.Token);
            }, ct);
            // The competing worker has started while the holder still owns the gate.
            // A delay cannot establish exactly where the OS has scheduled that worker.
            await TestWorker.WaitForSignalAsync(started.Task, waiter, ct);
            Assert.False(waiter.IsCompleted);
            cancellation.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiter.WaitAsync(ct));
        }
        finally
        {
            release.Set();
            await holder;
        }
        plugin.Callback = null;
        Assert.True(session.State.IsDirty);
        Assert.Single(WorkspaceExporter.Plan(workspace, new()).Overrides);
    }

    [Fact]
    public void Precancelled_zip_does_not_touch_destination()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        using var output = new MemoryStream();
        var plan = new ExportPlan([], new Dictionary<string, ReadOnlyMemory<byte>>(), []);

        Assert.ThrowsAny<OperationCanceledException>(() => WorkspaceExporter.WriteZip(plan, output, cancellation.Token));

        Assert.Equal(0, output.Length);
    }

    [Fact]
    public void Zip_observes_cancellation_while_finishing_archive()
    {
        using var cancellation = new CancellationTokenSource();
        using var output = new CancellingStream(cancellation);
        var plan = new ExportPlan([], new Dictionary<string, ReadOnlyMemory<byte>>(), []);

        Assert.ThrowsAny<OperationCanceledException>(() => WorkspaceExporter.WriteZip(plan, output, cancellation.Token));
    }

    [Theory]
    [InlineData("before")]
    [InlineData("serialize")]
    [InlineData("open")]
    [InlineData("view")]
    [InlineData("throw")]
    public async Task Cancelled_planning_preserves_document_and_releases_locks(string stage)
    {
        using var cancellation = new CancellationTokenSource();
        var plugin = new CallbackPlugin();
        using var store = new WorkspaceStore(new PluginCatalog([plugin]), new() { DataRoot = TestZips.TempDir() });
        var workspace = store.Create("test", [new ImportSource("save.dat", () => new MemoryStream(CounterPlugin.Create(5)))]);
        var session = workspace.Open(Assert.Single(workspace.Set.AllDiskFiles).Id, plugin.Info.Id);
        _ = session.BuildView();
        Assert.True(session.ApplyEdits([new IncomingEdit("counter", JsonSerializer.SerializeToElement("6"))]).Ok);
        var state = session.State;
        var original = session.File.ReadAllBytes();
        plugin.Callback = current =>
        {
            if (current == stage) cancellation.Cancel();
            if (current == "open" && stage == "throw") throw new OperationCanceledException(cancellation.Token);
        };
        if (stage == "before") cancellation.Cancel();

        Assert.ThrowsAny<OperationCanceledException>(() => WorkspaceExporter.Plan(workspace, new(), cancellation.Token));

        plugin.Callback = null;
        Assert.Equal(state, session.State);
        Assert.Equal(original, session.File.ReadAllBytes());
        var retry = await TestWorker.Run(() => WorkspaceExporter.Plan(workspace, new()), TestContext.Current.CancellationToken);
        Assert.Equal(CounterPlugin.Create(6), Assert.Single(retry.Overrides).Value.ToArray());
        Assert.True(session.Undo().Ok);
        Assert.False(session.State.IsDirty);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Zip_observes_cancellation_between_chunks(bool rewritten)
    {
        using var cancellation = new CancellationTokenSource();
        using var store = new WorkspaceStore(new PluginCatalog([]), new() { DataRoot = TestZips.TempDir() });
        var bytes = new byte[1024 * 1024];
        new Random(42).NextBytes(bytes);
        var workspace = store.Create("test", [new ImportSource("save.dat", () => new MemoryStream(bytes))]);
        var plan = WorkspaceExporter.Plan(workspace, new() { Scope = ExportScope.Everything });
        if (rewritten) plan = plan with { Overrides = new Dictionary<string, ReadOnlyMemory<byte>> { [Assert.Single(plan.Included).RelativePath] = bytes } };
        using var output = new CancellingStream(cancellation);

        Assert.ThrowsAny<OperationCanceledException>(() => WorkspaceExporter.WriteZip(plan, output, cancellation.Token));

        Assert.InRange(output.Length, 1, bytes.Length / 2);
        using var retry = new MemoryStream();
        WorkspaceExporter.WriteZip(plan, retry);
        Assert.True(retry.Length > output.Length);
    }

    private sealed class CancellingStream(CancellationTokenSource cancellation) : MemoryStream
    {
        public override void Write(byte[] buffer, int offset, int count)
        {
            base.Write(buffer, offset, count);
            cancellation.Cancel();
        }

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            base.Write(buffer);
            cancellation.Cancel();
        }
    }

    private sealed class CallbackPlugin : IGamePlugin
    {
        private readonly CounterPlugin _inner = new();
        public Action<string>? Callback { get; set; }
        public PluginInfo Info => _inner.Info;
        public ProbeResult Probe(ISaveFile file) => _inner.Probe(file);
        public ISaveDocument Open(ISaveFile file)
        {
            Callback?.Invoke("open");
            return new CallbackDocument(_inner.Open(file), stage => Callback?.Invoke(stage));
        }
    }

    private sealed class CallbackDocument(ISaveDocument inner, Action<string> callback) : ISaveDocument
    {
        public string Title => inner.Title;
        public bool CanEdit => inner.CanEdit;
        public ViewNode BuildView() { callback("view"); return inner.BuildView(); }
        public FieldValue GetValue(string valueId) => inner.GetValue(valueId);
        public EditOutcome ApplyEdits(IReadOnlyList<ValueEdit> edits) => inner.ApplyEdits(edits);
        public IReadOnlyList<FileOutput> Serialize() { callback("serialize"); return inner.Serialize(); }
        public void Dispose() => inner.Dispose();
    }
}
