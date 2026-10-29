using System.Text.Json;
using SaveToolkit.Core.Plugins;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Core.Storage;
using SaveToolkit.Core.Tests.Fakes;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Saves;

namespace SaveToolkit.Core.Tests;

public sealed class PluginReplacementConcurrencyTests
{
    [Fact]
    public async Task ConcurrentEditAndPluginSwitch_NeverDiscardASuccessfulEdit()
    {
        var ct = TestContext.Current.CancellationToken;
        var original = new CounterPlugin();
        var replacement = new AlternateCounterPlugin();
        using var store = new WorkspaceStore(new PluginCatalog([original, replacement]), new() { DataRoot = TestZips.TempDir() });
        var workspace = store.Create("switch race", [new ImportSource("save.dat", () => new MemoryStream(CounterPlugin.Create(1)))]);
        var file = workspace.Set.AllDiskFiles.Single();
        using var start = new Barrier(2);
        for (var attempt = 0; attempt < 2_000; attempt++)
        {
            var session = workspace.Open(file.Id, original.Info.Id);
            _ = session.BuildView();
            var editing = Task.Run(() =>
            {
                Assert.True(start.SignalAndWait(TimeSpan.FromSeconds(10), ct));
                try { return session.ApplyEdits([new("counter", JsonSerializer.SerializeToElement(2))]).Ok; }
                catch (ObjectDisposedException) { return false; }
            }, ct);
            var switching = Task.Run(() =>
            {
                Assert.True(start.SignalAndWait(TimeSpan.FromSeconds(10), ct));
                try { workspace.Open(file.Id, replacement.Info.Id); return true; }
                catch (InvalidOperationException) { return false; }
            }, ct);
            await Task.WhenAll(editing, switching);

            Assert.False(editing.Result && switching.Result, $"Successful edit discarded on attempt {attempt}.");
            if (editing.Result)
            {
                Assert.Same(session, workspace.FindDocument(session.Id));
                Assert.Equal(CounterPlugin.Create(2), Assert.Single(session.Serialize()).Content.ToArray());
            }
            foreach (var document in workspace.Documents) workspace.Close(document.Id);
        }
    }

    private sealed class AlternateCounterPlugin : IGamePlugin
    {
        private readonly CounterPlugin _inner = new();
        public PluginInfo Info { get; } = new("test.alternate-counter", "Alternate counter", "1.0.0");
        public ProbeResult Probe(ISaveFile file) => _inner.Probe(file);
        public ISaveDocument Open(ISaveFile file) => _inner.Open(file);
    }
}
