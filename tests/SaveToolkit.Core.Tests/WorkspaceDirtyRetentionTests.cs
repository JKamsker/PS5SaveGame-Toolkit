using System.Text.Json;
using SaveToolkit.Core.Export;
using SaveToolkit.Core.Plugins;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Core.Storage;
using SaveToolkit.Core.Tests.Fakes;
using SaveToolkit.Sdk;

namespace SaveToolkit.Core.Tests;

public sealed class WorkspaceDirtyRetentionTests
{
    [Fact]
    public void One_dirty_session_retains_every_session_without_partial_retirement()
    {
        var plugin = new IdleRetentionPlugin();
        using var store = Store(plugin);
        var workspace = store.Create("mixed sessions", [
            new ImportSource("clean.dat", () => new MemoryStream([1])),
            new ImportSource("dirty.dat", () => new MemoryStream([1]))]);
        var sessions = workspace.Set.AllDiskFiles.Select(file => workspace.Open(file.Id, plugin.Info.Id)).ToArray();
        foreach (var session in sessions) _ = session.BuildView();
        Assert.True(Edit(sessions[1], "value", 2).Ok);

        Assert.Equal(0, store.Sweep());

        Assert.Equal(2, workspace.Documents.Count);
        Assert.True(Edit(sessions[0], "value", 2).Ok);
        Assert.True(sessions[0].Undo().Ok);
        Assert.True(sessions[1].Undo().Ok);
        Assert.Equal(1, store.Sweep());
    }

    [Fact]
    public void Clean_workspace_expires_but_dirty_independent_work_survives_until_undone()
    {
        var plugin = new DownloadPlugin();
        using var store = Store(plugin);
        var clean = Create(store, CounterPlugin.Create(5));
        var dirty = Create(store, CounterPlugin.Create(5));
        var session = Open(dirty, plugin);
        Assert.True(Edit(session, "counter", 6).Ok);
        var before = session.State;

        Assert.Equal(1, store.Sweep());

        Assert.Null(store.Get(clean.Id));
        Assert.False(Directory.Exists(clean.Set.StorageRoot));
        Assert.Same(dirty, store.Get(dirty.Id));
        Assert.Equal(before, session.State);
        Assert.Equal(0, plugin.SerializeCalls);
        Assert.True(session.Undo().Ok);
        Assert.False(session.State.IsDirty);
        Assert.Equal(1, store.Sweep());
        Assert.False(Directory.Exists(dirty.Set.StorageRoot));
        Assert.Throws<ObjectDisposedException>(() => session.BuildView());
    }

    [Fact]
    public void Dirty_group_survives_without_reopen_or_serialization_and_undo_allows_expiry()
    {
        var plugin = new GroupPlugin();
        using var store = Store(plugin);
        var zip = TestZips.Build(("PPSA99990/sdimg_SAVE/character.dat", GroupPlugin.Create("C", 10)),
            ("PPSA99990/sdimg_SAVE/profile.dat", GroupPlugin.Create("P", 20)));
        var workspace = store.Create("group", [new ImportSource("dump.zip", () => new MemoryStream(zip))]);
        var anchor = workspace.Set.AllDiskFiles.Single(f => f.FileName == "character.dat");
        var group = workspace.OpenGroup(Assert.Single(workspace.ProposeGroups(anchor.Id, plugin.Info.Id)).Token);
        _ = group.Session.BuildView();
        Assert.True(Edit(group.Session, "character/balance", 11).Ok);
        var before = group.Session.State;
        int reopens = plugin.RoleReopens;
        int opened = plugin.Opened.Count;

        Assert.Equal(0, store.Sweep());

        Assert.Equal(before, group.Session.State);
        Assert.Equal(reopens, plugin.RoleReopens);
        Assert.Equal(opened, plugin.Opened.Count);
        Assert.True(Directory.Exists(workspace.Set.StorageRoot));
        Assert.True(group.Session.Undo().Ok);
        Assert.False(group.Session.State.IsDirty);
        Assert.Equal(1, store.Sweep());
        Assert.False(Directory.Exists(workspace.Set.StorageRoot));
        Assert.All(plugin.Opened, document => Assert.True(document.IsDisposed));
    }

    [Fact]
    public void Irreversible_dirty_work_survives_even_when_no_undo_is_available()
    {
        var plugin = new RosterPlugin();
        using var store = Store(plugin);
        var workspace = Create(store, RosterPlugin.Create((1, 5, "hero")));
        var session = Open(workspace, plugin);
        Assert.True(session.InvokeAction("clear-all", new Dictionary<string, JsonElement>()).Ok);
        Assert.True(session.State.IsDirty);
        Assert.False(session.State.CanUndo);
        var before = session.State;

        Assert.Equal(0, store.Sweep());

        Assert.Equal(before, session.State);
        Assert.Same(workspace, store.Get(workspace.Id));
    }

    [Fact]
    public void Export_keeps_dirty_workspace_retained_until_explicit_close()
    {
        var plugin = new CounterPlugin();
        using var store = Store(plugin);
        var workspace = Create(store, CounterPlugin.Create(5));
        var session = Open(workspace, plugin);
        Assert.True(Edit(session, "counter", 6).Ok);

        Assert.Single(WorkspaceExporter.Plan(workspace, new()).Overrides);
        Assert.True(session.State.IsDirty);
        Assert.Equal(0, store.Sweep());
        Assert.True(workspace.Close(session.Id));
        Assert.Equal(1, store.Sweep());
    }

    [Theory]
    [InlineData("remove")]
    [InlineData("shutdown")]
    [InlineData("dispose")]
    public void Explicit_discard_still_closes_dirty_sessions_and_cleans_blobs(string operation)
    {
        var plugin = new IdleRetentionPlugin();
        using var store = Store(plugin);
        var workspace = Create(store, [1]);
        var session = Open(workspace, plugin);
        Assert.True(Edit(session, "value", 2).Ok);
        int disposedBefore = plugin.Disposals;
        using var lease = Assert.IsType<WorkspaceLease>(store.Acquire(workspace.Id));

        if (operation == "shutdown") store.Dispose();
        else if (operation == "remove") Assert.True(store.Remove(workspace.Id));
        else
        {
            workspace.Dispose();
            Assert.Equal(disposedBefore + 1, plugin.Disposals);
            Assert.True(store.Remove(workspace.Id));
        }

        Assert.Empty(store.All);
        Assert.True(Directory.Exists(workspace.Set.StorageRoot));
        lease.Dispose();
        Assert.Equal(disposedBefore + 1, plugin.Disposals);
        Assert.False(Directory.Exists(workspace.Set.StorageRoot));
        Assert.Throws<ObjectDisposedException>(() => session.BuildView());
    }

    private static WorkspaceStore Store(IGamePlugin plugin) => new(new PluginCatalog([plugin]),
        new() { DataRoot = TestZips.TempDir(), IdleTimeout = TimeSpan.Zero });
    private static Workspace Create(WorkspaceStore store, byte[] bytes) =>
        store.Create("retention", [new ImportSource("save.dat", () => new MemoryStream(bytes))]);
    private static DocumentSession Open(Workspace workspace, IGamePlugin plugin)
    {
        var session = workspace.Open(workspace.Set.AllDiskFiles.Single().Id, plugin.Info.Id);
        _ = session.BuildView();
        return session;
    }
    private static SessionEditResult Edit(DocumentSession session, string field, int value) =>
        session.ApplyEdits([new(field, JsonSerializer.SerializeToElement(value))]);
}
