using System.Text.Json;
using SaveToolkit.Core.Export;
using SaveToolkit.Core.Plugins;
using SaveToolkit.Core.References;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Core.Storage;
using SaveToolkit.Core.Tests.Fakes;
using SaveToolkit.Core.Tests.References;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Saves;

namespace SaveToolkit.Core.Tests;

public sealed class VerifiedDownloadTests
{
    [Fact]
    public void Reopened_view_failure_preserves_diagnostics_history_and_retryability()
    {
        var plugin = new DownloadPlugin();
        using var store = Store(plugin);
        var (workspace, session) = Open(store, plugin);
        var before = session.State;
        var failure = new InvalidDataException("Injected staged view failure.");
        plugin.BeforeBuildView = () => throw failure;

        var error = Assert.Throws<ExportException>(() => DocumentDownloader.Capture(workspace, session));

        Assert.Same(failure, error.InnerException);
        Assert.Contains(session.File.RelativePath, error.Message, StringComparison.Ordinal);
        Assert.Equal(before, session.State);
        plugin.BeforeBuildView = null;
        Assert.NotNull(DocumentDownloader.Capture(workspace, session));
        Assert.Equal(before, session.State);
    }

    [Fact]
    public void Verification_keeps_reference_packs_pinned_before_a_pack_is_replaced_and_removed()
    {
        var plugin = new DownloadPlugin();
        var packs = new ReferencePackStore([plugin]);
        var pack = PackBuilder.WithItems("Pinned Potion") with { Plugins = [plugin.Info.Id] };
        packs.Import(pack.Zip(), "original.zip");
        using var store = new WorkspaceStore(new PluginCatalog([plugin]), new() { DataRoot = TestZips.TempDir() }, packs);
        var (workspace, session) = Open(store, plugin);
        var pinned = workspace.References(session.Id);
        packs.Import((PackBuilder.WithItems("Replacement Potion") with { Plugins = [plugin.Info.Id] }).Zip(), "replacement.zip");
        Assert.True(packs.Remove(pack.Id));
        plugin.OnOpen = (_, references) =>
        {
            Assert.Same(pinned, references);
            Assert.Contains(references.Packs, p => p.Id == pack.Id);
            Assert.Equal("Pinned Potion", references.Catalog("items")!.Find("1001")!.Name);
        };

        Assert.NotNull(DocumentDownloader.Capture(workspace, session));
        Assert.Equal(1, plugin.SerializeCalls);
    }

    [Fact]
    public void Download_returns_the_single_verified_snapshot_even_if_live_buffers_and_revision_change()
    {
        var plugin = new DownloadPlugin();
        using var store = Store(plugin);
        var (workspace, session) = Open(store, plugin);
        byte[]? liveBuffer = null;
        byte[]? verifiedBytes = null;
        plugin.Outputs = outputs =>
        {
            liveBuffer = outputs[0].Content.ToArray();
            return [outputs[0] with { Content = liveBuffer }];
        };
        plugin.OnOpen = (file, references) =>
        {
            Assert.Same(workspace.References(session.Id), references);
            liveBuffer!.AsSpan().Clear();
            verifiedBytes = file.ReadAllBytes();
            // A Core caller can edit after capture. Verification and response still belong
            // to the captured revision; HTTP callers additionally use the revision filter.
            Assert.True(Edit(session, 7).Ok);
        };
        var opensBeforeCapture = plugin.OpenCalls;

        var output = Assert.IsType<FileOutput>(DocumentDownloader.Capture(workspace, session));

        Assert.Equal(CounterPlugin.Create(6), verifiedBytes);
        Assert.Equal(verifiedBytes, output.Content.ToArray());
        Assert.Equal(opensBeforeCapture + 1, plugin.OpenCalls);
        Assert.Equal(1, plugin.SerializeCalls);
        Assert.Equal(2, session.State.Revision);
        Assert.True(session.State.IsDirty);
        Assert.True(session.Undo().Ok);
        Assert.Equal(3, session.State.Revision);
        Assert.True(session.Undo().Ok);
        Assert.False(session.State.IsDirty);
    }

    [Fact]
    public void Verification_sees_all_sibling_outputs_without_serializing_another_dirty_document()
    {
        var plugin = new DownloadPlugin();
        using var store = Store(plugin);
        var (workspace, session) = Open(store, plugin, withSibling: true);
        var sibling = workspace.Set.AllDiskFiles.Single(f => f.FileName == "sibling.dat");
        var other = workspace.Open(sibling.Id, plugin.Info.Id);
        _ = other.BuildView();
        Assert.True(Edit(other, 9).Ok);
        plugin.Outputs = outputs => [.. outputs, new(sibling.RelativePath, CounterPlugin.Create(8))];
        plugin.OnOpen = (file, _) => Assert.Equal(CounterPlugin.Create(8), file.Sibling("sibling.dat")!.ReadAllBytes());
        var before = session.State;
        var otherBefore = other.State;

        var output = Assert.IsType<FileOutput>(DocumentDownloader.Capture(workspace, session));

        Assert.Equal(CounterPlugin.Create(6), output.Content.ToArray());
        Assert.Equal(1, plugin.SerializeCalls);
        Assert.Equal(before, session.State);
        Assert.Equal(otherBefore, other.State);
        Assert.Equal(CounterPlugin.Create(5), sibling.ReadAllBytes());
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("duplicate")]
    public void Invalid_output_paths_are_rejected_before_reopen(string kind)
    {
        var plugin = new DownloadPlugin();
        using var store = Store(plugin);
        var (workspace, session) = Open(store, plugin);
        plugin.Outputs = outputs => kind == "unknown"
            ? [outputs[0] with { RelativePath = "../unknown.dat" }] : [outputs[0], outputs[0]];
        var before = session.State;
        int opens = plugin.OpenCalls;

        Assert.Throws<ExportException>(() => DocumentDownloader.Capture(workspace, session));

        Assert.Equal(opens, plugin.OpenCalls);
        Assert.Equal(before, session.State);
    }

    [Fact]
    public void Independent_output_cannot_overwrite_a_group_member()
    {
        var plugin = new GroupPlugin();
        using var store = Store(plugin);
        var zip = TestZips.Build(("PPSA99990/sdimg_SAVE/character.dat", GroupPlugin.Create("C", 10)),
            ("PPSA99990/sdimg_SAVE/profile.dat", GroupPlugin.Create("P", 20)),
            ("PPSA99990/sdimg_SAVE/outside.dat", GroupPlugin.Create("P", 30)));
        var workspace = store.Create("group", [new ImportSource("dump.zip", () => new MemoryStream(zip))]);
        var anchor = workspace.Set.AllDiskFiles.Single(f => f.FileName == "character.dat");
        var group = workspace.OpenGroup(Assert.Single(workspace.ProposeGroups(anchor.Id, GroupPlugin.PluginId)).Token);
        var outside = workspace.Open(workspace.Set.AllDiskFiles.Single(f => f.FileName == "outside.dat").Id, plugin.Info.Id);
        _ = outside.BuildView();
        Assert.True(outside.ApplyEdits([new("balance", JsonSerializer.SerializeToElement(31))]).Ok);
        plugin.AdditionalOutput = new(anchor.RelativePath, GroupPlugin.Create("C", 11));

        Assert.Throws<ExportException>(() => DocumentDownloader.Capture(workspace, outside));
        Assert.Throws<InvalidOperationException>(() => DocumentDownloader.Capture(workspace, group.Session));
    }

    [Fact]
    public void Cancellation_before_capture_and_during_reopen_preserves_history_and_releases_locks()
    {
        var plugin = new DownloadPlugin();
        using var store = Store(plugin);
        var (workspace, session) = Open(store, plugin);
        var before = session.State;
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => DocumentDownloader.Capture(workspace, session, cancellation.Token));
        Assert.Equal(0, plugin.SerializeCalls);
        using var duringReopen = new CancellationTokenSource();
        plugin.OnOpen = (_, _) => duringReopen.Cancel();
        Assert.Throws<OperationCanceledException>(() => DocumentDownloader.Capture(workspace, session, duringReopen.Token));
        Assert.Equal(before, session.State);
        plugin.OnOpen = null;
        Assert.NotNull(DocumentDownloader.Capture(workspace, session));
        Assert.Equal(before, session.State);
    }

    private static WorkspaceStore Store(IGamePlugin plugin) => new(new PluginCatalog([plugin]), new() { DataRoot = TestZips.TempDir() });

    private static (Workspace Workspace, DocumentSession Session) Open(WorkspaceStore store, DownloadPlugin plugin, bool withSibling = false)
    {
        var bytes = withSibling
            ? TestZips.Build(("PPSA99999/sdimg_SAVE/保存.dat", CounterPlugin.Create(5)),
                ("PPSA99999/sdimg_SAVE/sibling.dat", CounterPlugin.Create(5))) : CounterPlugin.Create(5);
        var workspace = store.Create("download", [new ImportSource(withSibling ? "dump.zip" : "保存.dat", () => new MemoryStream(bytes))]);
        var session = workspace.Open(workspace.Set.AllDiskFiles.Single(f => f.FileName == "保存.dat").Id, plugin.Info.Id);
        _ = session.BuildView();
        Assert.True(Edit(session, 6).Ok);
        return (workspace, session);
    }

    private static SessionEditResult Edit(DocumentSession session, int value) =>
        session.ApplyEdits([new("counter", JsonSerializer.SerializeToElement(value))]);
}
