using System.Text.Json;
using SaveToolkit.Core.Export;
using SaveToolkit.Core.Plugins;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Core.Storage;
using SaveToolkit.Core.Tests.Fakes;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Saves;

namespace SaveToolkit.Core.Tests;

public sealed class DocumentGroupOwnershipTests : IDisposable
{
    private const string Root = "PPSA99990/sdimg_SAVE/";
    private readonly GroupPlugin _plugin = new();
    private readonly WorkspaceStore _store;
    public DocumentGroupOwnershipTests() => _store = new(new PluginCatalog([_plugin]), new() { DataRoot = TestZips.TempDir() });
    public void Dispose() => _store.Dispose();

    private Workspace Create()
    {
        var zip = TestZips.Build((Root + "character.dat", GroupPlugin.Create("C", 10, 7, 7)),
            (Root + "profile.dat", GroupPlugin.Create("P", 20, 9)),
            (Root + "character.dat", GroupPlugin.Create("C", 11, 7)),
            (Root + "profile.dat", GroupPlugin.Create("P", 21, 9)),
            (Root + "outside.dat", GroupPlugin.Create("P", 5)),
            ("PPSA99990/sdimg_sce_bu_SAVE/character.dat", GroupPlugin.Create("C", 10, 7, 7)));
        return _store.Create("generations", [new ImportSource("groups.zip", () => new MemoryStream(zip))]);
    }

    private static DiskSaveFile Anchor(Workspace workspace) => workspace.Set.AllDiskFiles.Single(f => f.RelativePath == Root + "character.dat");

    [Theory]
    [InlineData("generation")]
    [InlineData("missing")]
    [InlineData("duplicate-role")]
    [InlineData("duplicate-path")]
    [InlineData("missing-anchor")]
    public void InvalidProposals_AreReportedBeforeOwnership(string fault)
    {
        var workspace = Create();
        _plugin.Proposals = _ => [new("bad", "Bad selection", fault switch
        {
            "generation" => [new("character", Root + "character.dat"), new("profile", "gen1/" + Root + "profile.dat")],
            "missing" => [new("character", Root + "character.dat"), new("profile", "missing")],
            "duplicate-role" => [new("character", Root + "character.dat"), new("character", Root + "profile.dat")],
            "duplicate-path" => [new("character", Root + "character.dat"), new("profile", Root + "character.dat")],
            _ => [new("character", "gen1/" + Root + "character.dat"), new("profile", "gen1/" + Root + "profile.dat")],
        })];
        var offer = Assert.Single(workspace.ProposeGroups(Anchor(workspace).Id, GroupPlugin.PluginId));
        Assert.NotEmpty(offer.Errors);
        Assert.Throws<SaveFormatException>(() => workspace.OpenGroup(offer.Token));
        Assert.Empty(workspace.Documents);
        Assert.Empty(_plugin.Opened);
    }

    [Fact]
    public void FingerprintChange_BeforeConfirmation_RejectsOwnership()
    {
        var workspace = Create();
        var anchor = Anchor(workspace);
        var offer = Assert.Single(workspace.ProposeGroups(anchor.Id, GroupPlugin.PluginId));
        Tamper(anchor);
        Assert.Throws<InvalidOperationException>(() => workspace.OpenGroup(offer.Token));
        Assert.Empty(workspace.DocumentGroups);
        Assert.Empty(_plugin.Opened);
    }

    [Fact]
    public void FingerprintChange_BlocksMutationsAndExport_WithoutPublishing()
    {
        var workspace = Create();
        var anchor = Anchor(workspace);
        var offer = Assert.Single(workspace.ProposeGroups(anchor.Id, GroupPlugin.PluginId));
        var session = workspace.OpenGroup(offer.Token).Session;
        _ = session.BuildView();
        Tamper(anchor);
        var result = session.ApplyEdits([new("character/balance", JsonSerializer.SerializeToElement("11"))], 0);
        Assert.False(result.Ok);
        Assert.False(session.State.IsDirty);
        Assert.Equal(0, session.State.Revision);
        Assert.Throws<InvalidOperationException>(() => WorkspaceExporter.Plan(workspace, new()));
    }

    [Fact]
    public void ConfirmedCleanReplacement_IsAtomic_AndDirtyReplacementRejected()
    {
        var workspace = Create();
        var anchor = Anchor(workspace);
        var single = workspace.Open(anchor.Id, GroupPlugin.PluginId);
        _ = single.BuildView();
        var offer = Assert.Single(workspace.ProposeGroups(anchor.Id, GroupPlugin.PluginId));
        Assert.True(single.ApplyEdits([new("balance", JsonSerializer.SerializeToElement("11"))]).Ok);
        Assert.Throws<InvalidOperationException>(() => workspace.OpenGroup(offer.Token, single.Id));
        Assert.Same(single, workspace.FindDocument(single.Id));
        Assert.True(single.Undo().Ok);
        var group = workspace.OpenGroup(offer.Token, single.Id);
        Assert.Null(workspace.FindDocument(single.Id));
        Assert.Same(group.Session, Assert.Single(workspace.Documents));
        Assert.Throws<ObjectDisposedException>(() => single.BuildView());
        Assert.Same(workspace.References(group.Session.Id), _plugin.LastReferences);
    }

    [Fact]
    public async Task CompetingGroupOpen_OnlyOneOwnerWins()
    {
        var workspace = Create();
        var token = Assert.Single(workspace.ProposeGroups(Anchor(workspace).Id, GroupPlugin.PluginId)).Token;
        var attempts = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => Task.Run(() =>
        {
            try { workspace.OpenGroup(token); return true; }
            catch (InvalidOperationException) { return false; }
        }, TestContext.Current.CancellationToken)));
        Assert.Single(attempts, success => success);
        Assert.Single(workspace.DocumentGroups);
        Assert.Single(workspace.Documents);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OpaqueSiblingWriter_IsRejectedAtOpeningOrExport(bool beforeOpening)
    {
        var workspace = Create();
        var outside = workspace.Set.AllDiskFiles.Single(f => f.FileName == "outside.dat");
        var other = workspace.Open(outside.Id, GroupPlugin.PluginId);
        _ = other.BuildView();
        Assert.True(other.ApplyEdits([new("balance", JsonSerializer.SerializeToElement("6"))]).Ok);
        var offer = Assert.Single(workspace.ProposeGroups(Anchor(workspace).Id, GroupPlugin.PluginId));
        if (!beforeOpening) workspace.OpenGroup(offer.Token);
        _plugin.AdditionalOutput = new(Root + "character.dat", GroupPlugin.Create("C", 99));
        if (beforeOpening)
        {
            Assert.Throws<InvalidOperationException>(() => workspace.OpenGroup(offer.Token));
            Assert.Empty(workspace.DocumentGroups);
        }
        else Assert.Throws<ExportException>(() => WorkspaceExporter.Plan(workspace, new()));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void IndependentBackupSession_IsNeverSilentlyMirrored(bool dirtyBackup)
    {
        var workspace = Create();
        var primary = workspace.Open(Anchor(workspace).Id, GroupPlugin.PluginId);
        var backupFile = workspace.Set.AllDiskFiles.Single(f => f.Directory.IsBackup);
        var backup = workspace.Open(backupFile.Id, GroupPlugin.PluginId);
        _ = primary.BuildView();
        _ = backup.BuildView();
        Assert.True(primary.ApplyEdits([new("balance", JsonSerializer.SerializeToElement("11"))]).Ok);
        if (dirtyBackup) Assert.True(backup.ApplyEdits([new("balance", JsonSerializer.SerializeToElement("12"))]).Ok);
        var plan = WorkspaceExporter.Plan(workspace, new() { Scope = ExportScope.ChangedFiles });
        if (dirtyBackup) Assert.Equal(GroupPlugin.Create("C", 12), plan.Overrides[backupFile.RelativePath].ToArray());
        else Assert.False(plan.Overrides.ContainsKey(backupFile.RelativePath));
    }

    [Fact]
    public void ExportGroupValidation_SeesTheCompleteFinalOverlay()
    {
        var workspace = Create();
        var outside = workspace.Set.AllDiskFiles.Single(f => f.FileName == "outside.dat");
        var other = workspace.Open(outside.Id, GroupPlugin.PluginId);
        _ = other.BuildView();
        Assert.True(other.ApplyEdits([new("balance", JsonSerializer.SerializeToElement("6"))]).Ok);
        var token = Assert.Single(workspace.ProposeGroups(Anchor(workspace).Id, GroupPlugin.PluginId)).Token;
        var group = workspace.OpenGroup(token).Session;
        _ = group.BuildView();
        Assert.True(group.ApplyEdits([new("character/balance", JsonSerializer.SerializeToElement("11"))]).Ok);
        _plugin.RequiredOutsideBalance = 6;
        var plan = WorkspaceExporter.Plan(workspace, new() { Scope = ExportScope.ChangedFiles });
        Assert.Equal(3, plan.Overrides.Count);
        _plugin.RequiredOutsideBalance = 99;
        Assert.Throws<ExportException>(() => WorkspaceExporter.Plan(workspace, new()));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(160)]
    public void OversizedSnapshot_IsRejectedBeforePublication(long budget)
    {
        var workspace = Create();
        var token = Assert.Single(workspace.ProposeGroups(Anchor(workspace).Id, GroupPlugin.PluginId)).Token;
        var session = workspace.OpenGroup(token, options: new() { MaxSnapshotBytes = budget }).Session;
        _ = session.BuildView();
        Assert.True(session.InvokeAction("inspect", new Dictionary<string, JsonElement>()).Ok);
        var result = session.InvokeAction("grow", new Dictionary<string, JsonElement>());
        Assert.False(result.Ok);
        Assert.Contains(result.Errors, error => error.Message.Contains("undo budget", StringComparison.Ordinal));
        Assert.Equal(0, session.State.Revision);
        Assert.False(session.State.IsDirty);
        Assert.Single(_plugin.Opened, document => !document.IsDisposed);
    }

    private static void Tamper(ISaveFile file)
    {
        string blob;
        using (var stream = (FileStream)file.OpenRead()) blob = stream.Name;
        System.IO.File.WriteAllBytes(blob, GroupPlugin.Create("C", 12, 7, 7));
    }
}
