using System.Text.Json;
using SaveToolkit.Core.Export;
using SaveToolkit.Core.Plugins;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Core.Storage;
using SaveToolkit.Sdk;

namespace SaveToolkit.Core.Tests;

public sealed class SystemOutputR6Tests
{
    [Fact]
    public void DownloadVerifier_UsesDirtyStatusCapturedBeforeAConcurrentEdit()
    {
        using var fixture = OpenDirty();
        var capture = fixture.Session.CaptureOutputs(CancellationToken.None);
        Assert.True(capture.IsDirty);
        Assert.True(fixture.Session.ApplyEdits([new("value", JsonSerializer.SerializeToElement(3))]).Ok);

        var overrides = VerifyCaptured(fixture.Workspace, fixture.Session, capture);

        Assert.Equal(2, overrides.Count);
        Assert.Contains(fixture.Session.File.RelativePath, overrides.Keys);
    }

    [Fact]
    public void ExportVerifier_UsesDirtyStatusCapturedBeforeUndo()
    {
        using var fixture = OpenDirty();
        var capture = fixture.Session.CaptureOutputs(CancellationToken.None);
        Assert.True(capture.IsDirty);
        Assert.True(fixture.Session.Undo().Ok);
        Assert.False(fixture.Session.State.IsDirty);

        var overrides = VerifyCaptured(fixture.Workspace, fixture.Session, capture);

        Assert.Equal(2, overrides.Count);
        Assert.Contains(fixture.Session.File.RelativePath, overrides.Keys);
    }

    private static Dictionary<string, ReadOnlyMemory<byte>> VerifyCaptured(
        Workspace workspace, DocumentSession session, DocumentOutputCapture capture)
    {
        var overrides = new Dictionary<string, ReadOnlyMemory<byte>>(StringComparer.OrdinalIgnoreCase);
        DocumentOutputVerifier.AddOutputs(workspace, session, capture, overrides, CancellationToken.None);
        DocumentOutputVerifier.Verify(workspace, [session], overrides, CancellationToken.None);
        return overrides;
    }

    private static Fixture OpenDirty()
    {
        var plugin = new SystemOutputOwnershipTests.OutputPlugin(true, true);
        var store = new WorkspaceStore(new PluginCatalog([plugin]), new() { DataRoot = TestZips.TempDir() });
        var workspace = SystemOutputOwnershipTests.CreateWorkspace(store, plugin);
        var session = SystemOutputOwnershipTests.OpenAndEdit(workspace, plugin);
        return new Fixture(store, workspace, session);
    }

    private sealed class Fixture(WorkspaceStore store, Workspace workspace, DocumentSession session) : IDisposable
    {
        public WorkspaceStore Store { get; } = store;
        public Workspace Workspace { get; } = workspace;
        public DocumentSession Session { get; } = session;
        public void Dispose() => Store.Dispose();
    }
}
