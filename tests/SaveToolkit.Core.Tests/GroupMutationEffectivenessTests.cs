using System.Text.Json;
using SaveToolkit.Core.Plugins;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Core.Sessions.Groups;
using SaveToolkit.Core.Storage;
using SaveToolkit.Core.Tests.Fakes;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Collections;

namespace SaveToolkit.Core.Tests;

public sealed class GroupMutationEffectivenessTests
{
    [Fact]
    public void ASecondGroupCannotClaimANonAnchorMemberOfACleanGroup()
    {
        var plugin = new GroupPlugin();
        using var store = Store(plugin);
        var workspace = Import(store);
        workspace.OpenGroup(Offer(workspace).Token);
        plugin.Proposals = _ => [new("overlap", "Overlap",
            [new("profile", Root + "profile.dat"), new("other", Root + "outside.dat")])];
        var profile = workspace.Set.AllDiskFiles.Single(file => file.FileName == "profile.dat");
        var offer = Assert.Single(workspace.ProposeGroups(profile.Id, GroupPlugin.PluginId));
        Assert.Empty(offer.Errors);
        Assert.Throws<InvalidOperationException>(() => workspace.OpenGroup(offer.Token));
        Assert.Single(workspace.DocumentGroups);
    }

    private const string Root = "PPSA99990/sdimg_SAVE/";

    [Theory]
    [InlineData("history")]
    [InlineData("snapshot")]
    [InlineData("query")]
    public void InvalidGroupBudgetsAreRejectedBeforeOpeningAPluginDocument(string kind)
    {
        var plugin = new GroupPlugin();
        using var store = Store(plugin);
        var workspace = Import(store);
        var options = kind switch
        {
            "history" => new DocumentSessionOptions { MaxHistory = 0 },
            "snapshot" => new() { MaxSnapshotBytes = -1 },
            _ => new() { MaxQueryActionRows = 0 },
        };
        Assert.Throws<ArgumentOutOfRangeException>(() => workspace.OpenGroup(Offer(workspace).Token, options: options));
        Assert.Empty(plugin.Opened);
        Assert.Empty(workspace.Documents);
    }

    [Fact]
    public void SnapshotBudgetAcceptsTokensExactlyAtTheLimit()
    {
        var plugin = new GroupPlugin();
        using var store = Store(plugin);
        var workspace = Import(store);
        const int snapshotBytes = 64 + 2 * 32;
        var group = workspace.OpenGroup(Offer(workspace).Token,
            options: new() { MaxSnapshotBytes = snapshotBytes });
        group.Session.BuildView();
        Assert.True(group.Session.ApplyEdits([new("character/balance", JsonSerializer.SerializeToElement(11))]).Ok);
        Assert.True(group.Session.Undo().Ok);
        Assert.True(group.Session.Redo().Ok);
    }

    [Fact]
    public void APartlyOwnedOutputSetIsRejectedEvenWhenItsCountMatches()
    {
        var plugin = new GroupPlugin();
        using var store = Store(plugin);
        var workspace = Import(store);
        var group = workspace.OpenGroup(Offer(workspace).Token);
        var outputs = new Dictionary<string, ReadOnlyMemory<byte>>
        {
            [Root + "character.dat"] = GroupPlugin.Create("C", 10, 7),
            [Root + "unowned.dat"] = GroupPlugin.Create("P", 20, 9),
        };
        Assert.Throws<SaveFormatException>(() => group.VerifyOutputs(outputs));
    }

    [Fact]
    public void VerificationAndSerializationIndependentlyRejectChangedImportedInputs()
    {
        var plugin = new GroupPlugin();
        using var store = Store(plugin);
        var workspace = Import(store);
        var group = workspace.OpenGroup(Offer(workspace).Token);
        group.Session.BuildView();
        Assert.True(group.Session.ApplyEdits([new("character/balance", JsonSerializer.SerializeToElement(11))]).Ok);
        var outputs = group.Session.Serialize().ToDictionary(output => output.RelativePath, output => output.Content);
        var file = workspace.Set.AllDiskFiles.First();
        File.WriteAllBytes(file.BlobPath, GroupPlugin.Create("C", 12, 7));

        Assert.Throws<InvalidOperationException>(() => group.VerifyOutputs(outputs));
        Assert.Throws<InvalidOperationException>(() => group.Session.Serialize());
    }

    [Fact]
    public void ReleasingAnUnselectedCleanDocumentCannotOpenAGroup()
    {
        var plugin = new GroupPlugin();
        using var store = Store(plugin);
        var workspace = Import(store);
        var outside = workspace.Set.AllDiskFiles.Single(file => file.FileName == "outside.dat");
        var session = workspace.Open(outside.Id, GroupPlugin.PluginId);
        Assert.Throws<InvalidOperationException>(() => workspace.OpenGroup(Offer(workspace).Token, session.Id));
        Assert.Same(session, workspace.FindDocument(session.Id));
        Assert.Empty(workspace.DocumentGroups);
    }

    [Fact]
    public void AcceptedGroupOffersCannotBeReusedAfterClosingTheGroup()
    {
        var plugin = new GroupPlugin();
        using var store = Store(plugin);
        var workspace = Import(store);
        var offer = Offer(workspace);
        var group = workspace.OpenGroup(offer.Token);
        Assert.True(workspace.Close(group.Session.Id));
        Assert.Throws<InvalidOperationException>(() => workspace.OpenGroup(offer.Token));
    }

    [Fact]
    public void RetainedQueryPagesAreBoundedButAnExistingPageCanBeReadAgain()
    {
        var plugin = new GroupPlugin();
        using var store = Store(plugin);
        var workspace = Import(store);
        var session = workspace.OpenGroup(Offer(workspace).Token).Session;
        const int retainedPages = 256;
        for (var offset = 0; offset < retainedPages; offset++)
            session.QueryCollection("character", new() { Offset = offset });
        Assert.Single(session.QueryCollection("character", new()).Rows);
        Assert.Throws<InvalidOperationException>(() => session.QueryCollection("character", new() { Offset = retainedPages }));
    }

    private static WorkspaceStore Store(GroupPlugin plugin) =>
        new(new PluginCatalog([plugin]), new() { DataRoot = TestZips.TempDir() });

    private static Workspace Import(WorkspaceStore store)
    {
        var archive = TestZips.Build((Root + "character.dat", GroupPlugin.Create("C", 10, 7)),
            (Root + "profile.dat", GroupPlugin.Create("P", 20, 9)), (Root + "outside.dat", GroupPlugin.Create("P", 30)));
        return store.Create("group mutation", [new ImportSource("dump.zip", () => new MemoryStream(archive))]);
    }

    private static DocumentGroupOffer Offer(Workspace workspace) => Assert.Single(workspace.ProposeGroups(
        workspace.Set.AllDiskFiles.Single(file => file.FileName == "character.dat").Id, GroupPlugin.PluginId));
}
