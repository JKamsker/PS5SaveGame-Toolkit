using SaveToolkit.Core.Plugins;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Core.Storage;
using SaveToolkit.Core.Tests.Fakes;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Saves;

namespace SaveToolkit.Core.Tests;

public sealed class WorkspaceMutationEffectivenessTests
{
    [Fact]
    public void ASelectedMemberDirtiedDuringGroupOpeningKeepsItsSessionAndDisposesTheCandidate()
    {
        var plugin = new ReferencedGroupPlugin();
        using var store = new WorkspaceStore(new PluginCatalog([plugin]), new() { DataRoot = TestZips.TempDir() });
        var root = ItemsPlugin.TitleId + "/sdimg_SAVE/";
        var zip = TestZips.Build((root + "character.dat", GroupPlugin.Create("C", 10, 7)),
            (root + "profile.dat", GroupPlugin.Create("P", 20, 9)));
        var workspace = store.Create("group confirmation", [new ImportSource("dump.zip", () => new MemoryStream(zip))]);
        var anchor = workspace.Set.AllDiskFiles.Single(file => file.FileName == "character.dat");
        var single = workspace.Open(anchor.Id, plugin.Info.Id);
        single.BuildView();
        var offer = Assert.Single(workspace.ProposeGroups(anchor.Id, plugin.Info.Id));
        plugin.OnOpenGroup = () => Assert.True(single.ApplyEdits(
            [new("balance", System.Text.Json.JsonSerializer.SerializeToElement(11))]).Ok);

        Assert.Throws<InvalidOperationException>(() => workspace.OpenGroup(offer.Token, single.Id));
        Assert.Same(single, Assert.Single(workspace.Documents));
        Assert.True(single.State.IsDirty);
        Assert.Empty(workspace.DocumentGroups);
        Assert.All(plugin.Inner.Opened, candidate => Assert.True(candidate.IsDisposed));
    }

    [Fact]
    public void UnknownWorkspaceIdsAndUnrecognizedSavesAreRejectedCleanly()
    {
        using var store = Store(TestZips.TempDir());
        Assert.False(store.Remove("missing"));
        var workspace = store.Create("unknown", [new ImportSource("unknown.dat", () => new MemoryStream([1]))]);
        Assert.False(workspace.Close("missing"));
        Assert.Throws<SaveFormatException>(() => workspace.Open(Assert.Single(workspace.Set.AllDiskFiles).Id, null));
        Assert.Empty(workspace.Documents);
    }

    [Fact]
    public void SwitchingPluginsCannotReplaceADirtySession()
    {
        using var store = new WorkspaceStore(new PluginCatalog([new CounterPlugin(), new RawFilePlugin()]),
            new() { DataRoot = TestZips.TempDir() });
        var workspace = store.Create("counter", [new ImportSource("save.dat", () => new MemoryStream(CounterPlugin.Create(1)))]);
        var file = Assert.Single(workspace.Set.AllDiskFiles);
        var session = workspace.Open(file.Id, null);
        session.BuildView();
        Assert.True(session.ApplyEdits([new("counter", System.Text.Json.JsonSerializer.SerializeToElement(2))]).Ok);
        Assert.Same(session, workspace.Open(file.Id, null));
        Assert.Throws<InvalidOperationException>(() => workspace.Open(file.Id, RawFilePlugin.PluginId));
        Assert.Same(session, Assert.Single(workspace.Documents));
        Assert.Equal(CounterPlugin.Create(2), Assert.Single(session.Serialize()).Content.ToArray());
    }

    [Fact]
    public void DisposingAWorkspaceClosesItsDocumentsAndRejectsFurtherOpens()
    {
        using var store = Store(TestZips.TempDir());
        var workspace = store.Create("counter", [new ImportSource("save.dat", () => new MemoryStream(CounterPlugin.Create(1)))]);
        var file = Assert.Single(workspace.Set.AllDiskFiles);
        var session = workspace.Open(file.Id, null);
        workspace.Dispose();
        Assert.Throws<ObjectDisposedException>(() => session.BuildView());
        Assert.Throws<ObjectDisposedException>(() => workspace.Open(file.Id, null));
        Assert.Empty(workspace.Documents);
    }

    [Fact]
    public void GroupReferencePacksRemainPinnedUntilTheGroupIsClosed()
    {
        var plugin = new ReferencedGroupPlugin();
        using var store = new WorkspaceStore(new PluginCatalog([plugin]), new() { DataRoot = TestZips.TempDir() });
        var root = ItemsPlugin.TitleId + "/sdimg_SAVE/";
        var zip = TestZips.Build((root + "character.dat", GroupPlugin.Create("C", 10, 7)),
            (root + "profile.dat", GroupPlugin.Create("P", 20, 9)));
        var workspace = store.Create("references", [new ImportSource("dump.zip", () => new MemoryStream(zip))]);
        var anchor = workspace.Set.AllDiskFiles.Single(file => file.FileName == "character.dat");
        var group = workspace.OpenGroup(Assert.Single(workspace.ProposeGroups(anchor.Id, plugin.Info.Id)).Token);
        Assert.NotEmpty(workspace.References(group.Session.Id).Datasets());
        Assert.Same(plugin.Inner.LastReferences, workspace.References(group.Session.Id));
        Assert.True(workspace.Close(group.Session.Id));
        Assert.Empty(workspace.References(group.Session.Id).Datasets());
    }

    [Fact]
    public void StartupRemovesNestedStaleBlobsBeforeAcceptingNewWorkspaces()
    {
        var root = TestZips.TempDir();
        var nested = Path.Combine(root, "stale", "nested");
        Directory.CreateDirectory(nested);
        var staleBlob = Path.Combine(nested, "blob");
        File.WriteAllBytes(staleBlob, [1]);
        using var store = Store(root);
        Assert.True(Directory.Exists(root));
        Assert.False(File.Exists(staleBlob));
        Assert.Empty(Directory.EnumerateFileSystemEntries(root));
    }

    [Fact]
    public void FreshWorkspacesAndAllLeaseTransitionsRefreshTheirIdleDeadline()
    {
        using var store = Store(TestZips.TempDir());
        var workspace = store.Create("counter", [new ImportSource("save.dat",
            () => new MemoryStream(CounterPlugin.Create(1)))]);
        Assert.True(workspace.LastAccess >= workspace.CreatedAt);
        Assert.Equal(0, store.Sweep());

        var previous = NextClockTick(workspace);
        Assert.Same(workspace, store.Get(workspace.Id));
        Assert.True(workspace.LastAccess > previous);
        previous = NextClockTick(workspace);
        var lease = Assert.IsType<WorkspaceLease>(store.Acquire(workspace.Id));
        Assert.True(workspace.LastAccess > previous);
        previous = NextClockTick(workspace);
        lease.Dispose();
        Assert.True(workspace.LastAccess > previous);
        Assert.Equal(0, store.Sweep());
    }

    private static DateTimeOffset NextClockTick(Workspace workspace)
    {
        var previous = workspace.LastAccess;
        Assert.True(SpinWait.SpinUntil(() => DateTimeOffset.UtcNow > previous, TimeSpan.FromSeconds(1)));
        return previous;
    }

    private static WorkspaceStore Store(string root) => new(
        new PluginCatalog([new CounterPlugin()]), new WorkspaceStoreOptions { DataRoot = root });

    private sealed class ReferencedGroupPlugin : IGamePlugin, IDocumentGroupPlugin
    {
        public Action? OnOpenGroup { get; set; }
        public GroupPlugin Inner { get; } = new();
        public PluginInfo Info { get; } = new ItemsPlugin().Info;
        public ProbeResult Probe(ISaveFile file) => Inner.Probe(file);
        public ISaveDocument Open(ISaveFile file) => Inner.Open(file);
        public ISaveDocument Open(ISaveFile file, IReferenceContext references) => Inner.Open(file, references);
        public IReadOnlyList<DocumentGroupProposal> ProposeGroups(ISaveFile anchor) => Inner.ProposeGroups(anchor);
        public IReadOnlyList<FieldError> ValidateGroup(DocumentGroupInput input, IReferenceContext references) => Inner.ValidateGroup(input, references);
        public IGroupedSaveDocument OpenGroup(DocumentGroupInput input, IReferenceContext references)
        {
            OnOpenGroup?.Invoke();
            return Inner.OpenGroup(input, references);
        }
    }
}
