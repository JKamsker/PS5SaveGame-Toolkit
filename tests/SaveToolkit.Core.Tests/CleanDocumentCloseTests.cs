using System.Text.Json;
using SaveToolkit.Core.Plugins;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Core.Storage;
using SaveToolkit.Core.Tests.Fakes;

namespace SaveToolkit.Core.Tests;

public sealed class CleanDocumentCloseTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void Acknowledged_edit_or_redo_prevents_clean_close_without_releasing_any_ownership(bool grouped, bool redo)
    {
        using var fixture = new CleanCloseFixture(grouped);
        Assert.True(fixture.Edit().Ok);
        if (redo)
        {
            Assert.True(fixture.Session.Undo().Ok);
            Assert.True(fixture.Session.Redo().Ok);
        }
        var state = fixture.Session.State;
        var outputs = fixture.Session.Serialize();

        var conflict = Assert.Throws<InvalidOperationException>(() => fixture.Workspace.CloseClean(fixture.Session.Id));

        Assert.Contains("unsaved edits", conflict.Message, StringComparison.Ordinal);
        fixture.AssertRetained(state, outputs);
        Assert.True(fixture.Session.Undo().Ok);
        Assert.True(fixture.Workspace.CloseClean(fixture.Session.Id));
        fixture.AssertClosed();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Clean_close_releases_document_pins_and_group_ownership_and_missing_ids_return_false(bool grouped)
    {
        using var fixture = new CleanCloseFixture(grouped);
        Assert.False(fixture.Workspace.CloseClean("missing"));
        Assert.True(fixture.Workspace.CloseClean(fixture.Session.Id));
        Assert.False(fixture.Workspace.CloseClean(fixture.Session.Id));
        fixture.AssertClosed();
        Assert.Throws<ObjectDisposedException>(() => fixture.Edit());
        Assert.Throws<ObjectDisposedException>(() => fixture.Session.Redo());
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Close_winning_before_a_queued_core_write_rejects_the_stale_session(bool grouped, bool redo)
    {
        using var fixture = new CleanCloseFixture(grouped);
        if (redo)
        {
            Assert.True(fixture.Edit().Ok);
            Assert.True(fixture.Session.Undo().Ok);
        }
        var ct = TestContext.Current.CancellationToken;
        var queued = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var resume = new ManualResetEventSlim();
        var writing = TestWorker.Run(() =>
        {
            queued.TrySetResult();
            resume.Wait(ct);
            return redo ? fixture.Session.Redo() : fixture.Edit();
        }, ct);
        try
        {
            await TestWorker.WaitForSignalAsync(queued.Task, writing, ct);
            Assert.True(fixture.Workspace.CloseClean(fixture.Session.Id));
        }
        finally { resume.Set(); }
        await Assert.ThrowsAsync<ObjectDisposedException>(async () => await writing);
        fixture.AssertClosed();
    }

    [Fact]
    public async Task Inflight_core_edit_publishes_before_clean_close_can_decide()
    {
        var ct = TestContext.Current.CancellationToken;
        var editing = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var closing = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var resume = new ManualResetEventSlim();
        var plugin = new IdleRetentionPlugin();
        using var store = new WorkspaceStore(new PluginCatalog([plugin]), new() { DataRoot = TestZips.TempDir() });
        var workspace = store.Create("inflight close", [new ImportSource("save.dat", () => new MemoryStream([1]))]);
        var session = workspace.Open(workspace.Set.AllDiskFiles.Single().Id, plugin.Info.Id);
        _ = session.BuildView();
        plugin.BeforeEdit = () => { editing.TrySetResult(); resume.Wait(ct); };
        var edit = TestWorker.Run(() => session.ApplyEdits([new("value", JsonSerializer.SerializeToElement(2))]), ct);
        Task<bool>? close = null;
        try
        {
            await TestWorker.WaitForSignalAsync(editing.Task, edit, ct);
            close = TestWorker.Run(() =>
            {
                closing.TrySetResult();
                return workspace.CloseClean(session.Id);
            }, ct);
            await TestWorker.WaitForSignalAsync(closing.Task, close, ct);
        }
        finally { plugin.BeforeEdit = null; resume.Set(); }

        Assert.True((await edit).Ok);
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await close!);
        Assert.Same(session, workspace.FindDocument(session.Id));
        Assert.True(session.State.IsDirty);
        Assert.True(session.Undo().Ok);
        Assert.True(workspace.CloseClean(session.Id));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Explicit_workspace_discard_still_removes_dirty_sessions(bool shutdown)
    {
        using var fixture = new CleanCloseFixture(grouped: true);
        Assert.True(fixture.Edit().Ok);
        if (shutdown) fixture.Store.Dispose();
        else Assert.True(fixture.Store.Remove(fixture.Workspace.Id));
        Assert.Empty(fixture.Store.All);
        Assert.False(Directory.Exists(fixture.Workspace.Set.StorageRoot));
        Assert.Throws<ObjectDisposedException>(() => fixture.Session.BuildView());
    }

    [Fact]
    public void Plugin_disposal_failure_is_not_misreported_as_a_retained_dirty_conflict()
    {
        var failure = new InvalidOperationException("Injected cleanup failure.");
        var plugin = new IdleRetentionPlugin { BeforeDispose = _ => throw failure };
        using var store = new WorkspaceStore(new PluginCatalog([plugin]), new() { DataRoot = TestZips.TempDir() });
        var workspace = store.Create("cleanup failure", [new ImportSource("save.dat", () => new MemoryStream([1]))]);
        var session = workspace.Open(workspace.Set.AllDiskFiles.Single().Id, plugin.Info.Id);

        var error = Assert.Throws<AggregateException>(() => workspace.CloseClean(session.Id));

        Assert.Same(failure, Assert.Single(error.InnerExceptions));
        Assert.Null(workspace.FindDocument(session.Id));
        Assert.Empty(workspace.References(session.Id).Packs);
        Assert.Throws<ObjectDisposedException>(() => session.BuildView());
        Assert.False(workspace.CloseClean(session.Id));
    }
}
