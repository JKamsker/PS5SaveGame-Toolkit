using System.Text.Json;
using SaveToolkit.Core.Export;
using SaveToolkit.Core.Plugins;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Core.Sessions.Groups;
using SaveToolkit.Core.Storage;
using SaveToolkit.Core.Tests.Fakes;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Saves;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Core.Tests;

public sealed class DocumentGroupTests : IDisposable
{
    private readonly GroupPlugin _plugin = new();
    private readonly WorkspaceStore _store;
    private const string Root = "PPSA99990/sdimg_SAVE/";

    public DocumentGroupTests() => _store = new(new PluginCatalog([_plugin]), new() { DataRoot = TestZips.TempDir() });
    public void Dispose() => _store.Dispose();

    private Workspace Create(params (string Path, byte[] Data)[] extra)
    {
        var zip = TestZips.Build([(Root + "character.dat", GroupPlugin.Create("C", 10, 7, 7)),
            (Root + "profile.dat", GroupPlugin.Create("P", 20, 9)), .. extra]);
        return _store.Create("groups", [new ImportSource("groups.zip", () => new MemoryStream(zip))]);
    }

    private static DiskSaveFile File(Workspace workspace, string name) =>
        workspace.Set.AllDiskFiles.Single(f => f.RelativePath == Root + name);

    private static DocumentGroupSession Open(Workspace workspace)
    {
        var offer = Assert.Single(workspace.ProposeGroups(File(workspace, "character.dat").Id, GroupPlugin.PluginId));
        Assert.Empty(offer.Errors);
        var group = workspace.OpenGroup(offer.Token);
        _ = group.Session.BuildView();
        return group;
    }

    private static IncomingEdit Edit(string id, int value) => new(id, JsonSerializer.SerializeToElement(value.ToString()));
    private static Dictionary<string, byte[]> Bytes(DocumentSession session) =>
        session.Serialize().ToDictionary(o => o.RelativePath, o => o.Content.ToArray());
    private static string[] Keys(DocumentSession session, string collection) =>
        session.QueryCollection(collection, new()).Rows.Select(r => r.Key).ToArray();
    private static void Transfer(DocumentSession session)
    {
        var preview = session.PlanAction("transfer", new Dictionary<string, JsonElement>(), expectedRevision: session.State.Revision);
        Assert.Empty(preview.Plan.Errors);
        Assert.True(session.ApplyActionPlan(preview.PlanId!, preview.Revision).Ok);
    }

    [Fact]
    public void AtomicBatch_UndoRedo_BothFilesAndOneHistory()
    {
        var workspace = Create();
        var group = Open(workspace);
        var session = group.Session;
        var result = session.ApplyEdits([Edit("character/balance", 11), Edit("profile/balance", 19)], 0);
        Assert.True(result.Ok);
        Assert.True(result.StructureChanged);
        Assert.Equal(2, session.Serialize().Count);
        var edited = Bytes(session);
        Assert.Equal(GroupPlugin.Create("C", 11, 7, 7), edited[Root + "character.dat"]);
        Assert.Equal(GroupPlugin.Create("P", 19, 9), edited[Root + "profile.dat"]);
        Assert.True(session.Undo().Ok);
        Assert.False(session.State.IsDirty);
        Assert.False(session.State.CanUndo);
        Assert.True(session.Redo().Ok);
        Assert.Equal(edited[Root + "character.dat"], Bytes(session)[Root + "character.dat"]);
        Assert.Equal(edited[Root + "profile.dat"], Bytes(session)[Root + "profile.dat"]);
        Assert.True(session.Undo().Ok);
        AssertOriginalLiveModel(workspace);
    }

    private void AssertOriginalLiveModel(Workspace workspace)
    {
        var live = _plugin.Opened.Last(d => !d.IsDisposed);
        Assert.All(live.Serialize(), output => Assert.Equal(
            ((ISaveSet)workspace.Set).FindFile(output.RelativePath)!.ReadAllBytes(), output.Content.ToArray()));
    }

    [Theory]
    [InlineData("fail")]
    [InlineData("bad-profile")]
    [InlineData("unowned")]
    [InlineData("duplicate")]
    [InlineData("omitted")]
    public void FailedCandidate_DoesNotPublishEitherMember(string action)
    {
        var workspace = Create();
        var session = Open(workspace).Session;
        Assert.False(session.InvokeAction(action, new Dictionary<string, JsonElement>(), 0).Ok);
        Assert.Equal(0, session.State.Revision);
        Assert.False(session.State.IsDirty);
        AssertOriginalLiveModel(workspace);
        Assert.Single(_plugin.Opened, d => !d.IsDisposed);
    }

    [Fact]
    public void RejectedFinalInvariant_RetainsBothMembersAndPriorUndoHistory()
    {
        var session = Open(Create()).Session;
        Assert.True(session.ApplyEdits([Edit("character/balance", 12)], 0).Ok);
        var before = Bytes(session);
        var revision = session.State.Revision;
        Assert.False(session.ApplyEdits([Edit("character/balance", 99), Edit("profile/balance", -1)], revision).Ok);
        Assert.Equal(revision, session.State.Revision);
        Assert.Equal(before[Root + "character.dat"], Bytes(session)[Root + "character.dat"]);
        Assert.Equal(before[Root + "profile.dat"], Bytes(session)[Root + "profile.dat"]);
        Assert.True(session.Undo().Ok);
        Assert.False(session.State.IsDirty);
    }

    [Fact]
    public void Transfer_RestoresDuplicateIdentities_AndInvalidatesBothMemberPagesAndPlans()
    {
        var workspace = Create();
        var session = Open(workspace).Session;
        var originalCharacterKeys = Keys(session, "character");
        var originalProfileKeys = Keys(session, "profile");
        var pending = session.PlanAction("transfer", new Dictionary<string, JsonElement>());
        Transfer(session);
        Assert.Equal([originalCharacterKeys[1]], Keys(session, "character"));
        Assert.Equal([.. originalProfileKeys, originalCharacterKeys[0]], Keys(session, "profile"));
        Assert.Throws<StaleViewException>(() => session.QueryCollection("character", new(), 0));
        Assert.Throws<StaleViewException>(() => session.QueryCollection("profile", new(), 0));
        Assert.True(session.ApplyEdits([Edit("profile/balance", 1)], 0).Stale);
        Assert.True(session.ApplyActionPlan(pending.PlanId!, pending.Revision).Stale);
        var transferred = Bytes(session);
        Assert.True(session.Undo().Ok);
        Assert.Equal(originalCharacterKeys, Keys(session, "character"));
        Assert.Equal(originalProfileKeys, Keys(session, "profile"));
        AssertOriginalLiveModel(workspace);
        Assert.True(session.Redo().Ok);
        Assert.Equal(transferred[Root + "profile.dat"], Bytes(session)[Root + "profile.dat"]);
        Assert.Equal([.. originalProfileKeys, originalCharacterKeys[0]], Keys(session, "profile"));
    }

    [Fact]
    public void StagedEdits_RebindLazyAndQueriedFields()
    {
        var session = Open(Create()).Session;
        _ = session.Expand("details");
        var key = Keys(session, "character")[1];
        Assert.True(session.ApplyEdits([Edit("profile/lazy", 25), Edit($"item/{key}", 8)], 0).Ok);
        Assert.Equal(GroupPlugin.Create("C", 10, 7, 8), Bytes(session)[Root + "character.dat"]);
        Assert.True(session.Undo().Ok);
        Assert.True(session.ApplyEdits([Edit("profile/lazy", 22)], session.State.Revision).Ok);
    }

    [Fact]
    public void SemanticReviewAndIndividualRevert_CoverBothMembers()
    {
        var session = Open(Create()).Session;
        Assert.True(session.ApplyEdits([Edit("character/balance", 11), Edit("profile/balance", 19)], 0).Ok);
        var changes = session.GetChanges();
        Assert.Contains(changes.Changes, change => change.ValueId == "character/balance");
        Assert.Contains(changes.Changes, change => change.ValueId == "profile/balance");
        Assert.True(session.RevertChange("profile/balance", changes.Revision).Ok);
        Assert.Equal(GroupPlugin.Create("P", 20, 9), Bytes(session)[Root + "profile.dat"]);
        Assert.Equal(GroupPlugin.Create("C", 11, 7, 7), Bytes(session)[Root + "character.dat"]);
        Assert.True(session.Undo().Ok);
        Assert.True(session.Undo().Ok);
        Assert.False(session.State.IsDirty);
    }

    [Fact]
    public void SemanticReviewPublishesAuthoritativeMemberContextForRenderedGroupFields()
    {
        var session = Open(Create()).Session;
        Assert.True(session.ApplyEdits([Edit("character/balance", 11)], 0).Ok);

        var changes = session.GetChanges().Changes;
        var change = Assert.Single(changes, item => item.ValueId == "character/balance");
        Assert.Equal("character", change.Member?.Role);
        Assert.Equal(Root + "character.dat", change.Member?.RelativePath);
        Assert.Equal("Character + profile", change.Member?.GroupTitle);
        Assert.Equal("synthetic group input", change.Member?.Provenance);
        var projectedAlias = Assert.Single(change.Aliases!);
        Assert.Equal("character/raw/balance", projectedAlias.ValueId);
        Assert.Equal("character", projectedAlias.Member?.Role);
        Assert.Equal(Root + "character.dat", projectedAlias.Member?.RelativePath);
        var live = _plugin.Opened.Last(document => !document.IsDisposed);
        Assert.NotEmpty(live.ContextRequests);
        Assert.DoesNotContain(live.ContextRequests.SelectMany(request => request), id => id.Contains("/raw/", StringComparison.Ordinal));
        Assert.Contains(live.ContextRequests.SelectMany(request => request), id => id == "character/balance");
        foreach (var alias in changes.Where(item => item.ValueId is "character/raw/balance" or "profile/raw/balance"))
        {
            Assert.Equal(alias.ValueId.StartsWith("character/", StringComparison.Ordinal) ? "character" : "profile",
                alias.Member?.Role);
            Assert.Equal(alias.ValueId.StartsWith("character/", StringComparison.Ordinal)
                ? Root + "character.dat" : Root + "profile.dat", alias.Member?.RelativePath);
        }
    }

    [Fact]
    public void QueryScopedPlan_ResolvesStableKeysThroughGroupAdapter()
    {
        var session = Open(Create()).Session;
        var preview = session.PlanAction("transfer", new Dictionary<string, JsonElement>(), new ActionScope
        { QueryScope = new("character", new QueryableCollectionQuery { Limit = 1 }, 0) });
        Assert.Empty(preview.Plan.Errors);
        Assert.Equal(["1", "2"], preview.Scope.RowKeys);
        Assert.True(session.ApplyActionPlan(preview.PlanId!, preview.Revision).Ok);
        Assert.True(session.Undo().Ok);
        Assert.Equal(["1", "2"], Keys(session, "character"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExistingSession_ExplicitConflict_EvenIfClean(bool dirty)
    {
        var workspace = Create();
        var single = workspace.Open(File(workspace, "profile.dat").Id, GroupPlugin.PluginId);
        _ = single.BuildView();
        if (dirty) Assert.True(single.ApplyEdits([Edit("balance", 21)]).Ok);
        var offer = Assert.Single(workspace.ProposeGroups(File(workspace, "character.dat").Id, GroupPlugin.PluginId));
        Assert.Throws<InvalidOperationException>(() => workspace.OpenGroup(offer.Token));
        Assert.Empty(workspace.DocumentGroups);
        Assert.True(workspace.Close(single.Id));
        Assert.NotNull(workspace.OpenGroup(offer.Token));
    }

    [Fact]
    public void ExclusiveOwnership_OverlapsRejected_ReleasedOnClose()
    {
        var workspace = Create();
        var group = Open(workspace);
        Assert.Throws<InvalidOperationException>(() => workspace.Open(File(workspace, "profile.dat").Id, GroupPlugin.PluginId));
        var offer = Assert.Single(workspace.ProposeGroups(File(workspace, "character.dat").Id, GroupPlugin.PluginId));
        Assert.Throws<InvalidOperationException>(() => workspace.OpenGroup(offer.Token));
        Assert.True(workspace.Close(group.Session.Id));
        Assert.NotNull(workspace.Open(File(workspace, "profile.dat").Id, GroupPlugin.PluginId));
    }

    [Fact]
    public void Inspection_DoesNotCreateUndoOrDirtyState()
    {
        var session = Open(Create()).Session;
        Assert.True(session.InvokeAction("inspect", new Dictionary<string, JsonElement>()).Ok);
        Assert.False(session.State.IsDirty);
        Assert.False(session.State.CanUndo);
        Assert.Equal(0, session.State.Revision);
    }

    [Fact]
    public void RejectedPlannedApply_DiscardsPartialMutation()
    {
        var workspace = Create();
        var session = Open(workspace).Session;
        var preview = session.PlanAction("transfer", new Dictionary<string, JsonElement>());
        _plugin.RejectApply = true;
        var result = session.ApplyActionPlan(preview.PlanId!, preview.Revision);
        Assert.False(result.Ok);
        Assert.False(session.State.IsDirty);
        Assert.Equal(0, session.State.Revision);
        AssertOriginalLiveModel(workspace);
    }

    [Fact]
    public void FailedUndo_DoesNotPublishPartialRestore()
    {
        var session = Open(Create()).Session;
        Transfer(session);
        var before = Bytes(session);
        var revision = session.State.Revision;
        _plugin.RejectRestore = true;
        Assert.False(session.Undo().Ok);
        // F5 conservatively rebuilds after a failed memento restore; bytes/history remain intact.
        Assert.True(session.State.Revision >= revision);
        Assert.True(session.State.CanUndo);
        Assert.Equal(before[Root + "character.dat"], Bytes(session)[Root + "character.dat"]);
        Assert.Equal(before[Root + "profile.dat"], Bytes(session)[Root + "profile.dat"]);
        _plugin.RejectRestore = false;
        Assert.True(session.Undo().Ok);
        Assert.False(session.State.IsDirty);
    }

    [Fact]
    public void Export_VerifiesEachRole_PublishesBothAndNeverMirrorsGroupBackups()
    {
        var workspace = Create(("PPSA99990/sdimg_sce_bu_SAVE/character.dat", GroupPlugin.Create("C", 10, 7, 7)),
            ("PPSA99990/sdimg_sce_bu_SAVE/profile.dat", GroupPlugin.Create("P", 20, 9)));
        var session = Open(workspace).Session;
        Transfer(session);
        var reopens = _plugin.RoleReopens;
        var plan = WorkspaceExporter.Plan(workspace, new() { Scope = ExportScope.ChangedFiles });
        Assert.Equal(2, plan.Overrides.Count);
        Assert.Equal(reopens + 2, _plugin.RoleReopens);
        Assert.All(plan.Files, file => Assert.StartsWith(Root, file.RelativePath));
        using var zip = new MemoryStream();
        WorkspaceExporter.WriteZip(plan, zip);
        zip.Position = 0;
        using var archive = new System.IO.Compression.ZipArchive(zip);
        Assert.Equal(2, archive.Entries.Count);
        _plugin.RejectValidation = true;
        Assert.Throws<ExportException>(() => WorkspaceExporter.Plan(workspace, new()));
        Assert.Equal(2, plan.Overrides.Count); // A previously verified plan remains a frozen snapshot.
    }
}
