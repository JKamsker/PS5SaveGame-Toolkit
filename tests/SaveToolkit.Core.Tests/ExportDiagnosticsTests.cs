using System.Text.Json;
using SaveToolkit.Core.Export;
using SaveToolkit.Core.Plugins;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Core.Storage;
using SaveToolkit.Core.Tests.Fakes;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Saves;

namespace SaveToolkit.Core.Tests;

public sealed class ExportDiagnosticsTests
{
    [Fact]
    public void Independent_verification_retains_original_failure_and_file_context()
    {
        var plugin = new FailingReopenPlugin();
        using var store = new WorkspaceStore(new PluginCatalog([plugin]), new() { DataRoot = TestZips.TempDir() });
        var workspace = store.Create("test", [new ImportSource("save.dat", () => new MemoryStream(CounterPlugin.Create(5)))]);
        var file = Assert.Single(workspace.Set.AllDiskFiles);
        var session = workspace.Open(file.Id, plugin.Info.Id);
        _ = session.BuildView();
        Assert.True(session.ApplyEdits([new IncomingEdit("counter", JsonSerializer.SerializeToElement("6"))]).Ok);
        plugin.Failure = new InvalidDataException("Injected malformed output.", new IOException("Underlying read failure."));

        var error = Assert.Throws<ExportException>(() => WorkspaceExporter.Plan(workspace, new()));

        Assert.Same(plugin.Failure, error.InnerException);
        Assert.Contains(file.RelativePath, error.Message, StringComparison.Ordinal);
        Assert.Contains(plugin.Info.Name, error.Message, StringComparison.Ordinal);
        Assert.True(session.State.IsDirty);
    }

    [Fact]
    public void Group_verification_retains_original_failure()
    {
        var plugin = new GroupPlugin();
        using var store = new WorkspaceStore(new PluginCatalog([plugin]), new() { DataRoot = TestZips.TempDir() });
        byte[] archive = TestZips.Build(
            ("PPSA99990/sdimg_SAVE/character.dat", GroupPlugin.Create("C", 10)),
            ("PPSA99990/sdimg_SAVE/profile.dat", GroupPlugin.Create("P", 20)));
        var workspace = store.Create("group", [new ImportSource("dump.zip", () => new MemoryStream(archive))]);
        var file = workspace.Set.AllDiskFiles.Single(f => f.FileName == "character.dat");
        var group = workspace.OpenGroup(Assert.Single(workspace.ProposeGroups(file.Id, GroupPlugin.PluginId)).Token);
        _ = group.Session.BuildView();
        Assert.True(group.Session.ApplyEdits([
            new IncomingEdit("character/balance", JsonSerializer.SerializeToElement("11"))]).Ok);
        plugin.RejectValidation = true;

        var error = Assert.Throws<ExportException>(() => WorkspaceExporter.Plan(workspace, new()));

        var cause = Assert.IsType<SaveFormatException>(error.InnerException);
        Assert.Contains("cross-member invariant", cause.Message, StringComparison.Ordinal);
        Assert.Contains("Group verification failed", error.Message, StringComparison.Ordinal);
        Assert.True(group.Session.State.IsDirty);
    }

    private sealed class FailingReopenPlugin : IGamePlugin
    {
        private readonly CounterPlugin _inner = new();
        public InvalidDataException? Failure { get; set; }
        public PluginInfo Info => _inner.Info;
        public ProbeResult Probe(ISaveFile file) => _inner.Probe(file);
        public ISaveDocument Open(ISaveFile file) => Failure is { } failure ? throw failure : _inner.Open(file);
    }
}
