using System.Text.Json;
using SaveToolkit.Core.Plugins;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Core.Storage;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Saves;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Testing;

namespace SaveToolkit.Core.Tests.SdkHelpers;

public sealed class DocumentMemberTransactionTests
{
    private const string Root = "PPSA99999/sdimg_SAVE/";

    [Fact]
    public void ComposedGroupOwnsRollbackAndFreshReusableMementos()
    {
        var set = new MemorySaveSet().With("PPSA99999", "SAVE", new Dictionary<string, byte[]> { ["a"] = [1], ["b"] = [2] });
        var input = new DocumentGroupInput(set.Directories[0].Files.Select(file => new DocumentGroupFile(file.FileName, file)).ToArray());
        using var group = new MemberAdapterGroup(input);
        var before = GroupAdapterPluginFixture.Bytes(group);
        var first = "a/" + MemberAdapterDocument.ValueId;
        var second = "b/" + MemberAdapterDocument.ValueId;
        var rejected = group.ApplyEdits([new(first, FieldValue.Of(5)), new(second, FieldValue.Of(-1))]);
        Assert.False(rejected.Succeeded);
        Assert.Equal(second, Assert.Single(rejected.Errors).ValueId);
        GroupAdapterPluginFixture.EqualBytes(before, group);
        Assert.Throws<KeyNotFoundException>(() => group.ApplyEdits([new(first, FieldValue.Of(6)), new("unknown/x", FieldValue.Of(1))]));
        GroupAdapterPluginFixture.EqualBytes(before, group);
        var result = group.ApplyEdits([new(first, FieldValue.Of(7)), new(second, FieldValue.Of(8)), new(first, FieldValue.Of(9))]);
        Assert.True(result.Succeeded);
        Assert.Equal([first, first, second], result.Changes.Select(change => change.ValueId));
        var changed = GroupAdapterPluginFixture.Bytes(group);
        using var fresh = new MemberAdapterGroup(input);
        _ = fresh.RestoreMemento(group.CaptureMemento());
        GroupAdapterPluginFixture.EqualBytes(changed, fresh);
        var redo = group.RestoreMemento(result.Revert!);
        GroupAdapterPluginFixture.EqualBytes(before, group);
        _ = group.RestoreMemento(redo);
        GroupAdapterPluginFixture.EqualBytes(changed, group);
        _ = group.RestoreMemento(result.Revert!);
        _ = group.RestoreMemento(result.Revert!);
        GroupAdapterPluginFixture.EqualBytes(before, group);
    }

    [Fact]
    public void CoreStagingHistoryAndQueryScopedPlansWorkWithAdapterComposition()
    {
        var plugin = new AdapterPlugin();
        using var store = new WorkspaceStore(new PluginCatalog([plugin]), new() { DataRoot = TestZips.TempDir() });
        var zip = TestZips.Build((Root + "a", new byte[] { 1 }), (Root + "b", new byte[] { 2 }));
        var workspace = store.Create("adapter", [new ImportSource("adapter.zip", () => new MemoryStream(zip))]);
        var offer = Assert.Single(workspace.ProposeGroups(workspace.Set.AllDiskFiles.First().Id, plugin.Info.Id));
        var session = workspace.OpenGroup(offer.Token).Session;
        _ = session.BuildView();
        var before = session.Serialize().ToDictionary(output => output.RelativePath, output => output.Content.ToArray());
        var first = "a/" + MemberAdapterDocument.ValueId;
        var second = "b/" + MemberAdapterDocument.ValueId;
        Assert.False(session.ApplyEdits([Edit(first, 5), Edit(second, -1)]).Ok);
        Assert.False(session.State.IsDirty);
        Assert.False(session.State.CanUndo);
        Assert.All(session.Serialize(), output => Assert.Equal(before[output.RelativePath], output.Content.ToArray()));
        Assert.True(session.ApplyEdits([Edit(first, 5), Edit(second, 6)]).Ok);
        Assert.True(session.Undo().Ok);
        Assert.False(session.State.CanUndo);
        Assert.False(session.State.IsDirty);
        Assert.True(session.Redo().Ok);
        Assert.Equal(new byte[] { 5 }, session.Serialize().Single(output => output.RelativePath == Root + "a").Content.ToArray());
        Assert.Equal(new byte[] { 6 }, session.Serialize().Single(output => output.RelativePath == Root + "b").Content.ToArray());
        var preview = session.PlanAction("b/set", new Dictionary<string, JsonElement> { ["value"] = JsonSerializer.SerializeToElement("9") },
            new ActionScope { QueryScope = new("b/items", new(), session.State.Revision) });
        Assert.Empty(preview.Plan.Errors);
        Assert.Equal(["b/" + MemberAdapterDocument.RowKey], preview.Scope.RowKeys);
        Assert.Equal(second, Assert.Single(preview.Plan.Changes).ValueId);
        Assert.True(session.ApplyActionPlan(preview.PlanId!, preview.Revision).Ok);
        Assert.Equal(new byte[] { 9 }, session.Serialize().Single(output => output.RelativePath == Root + "b").Content.ToArray());
        Assert.True(session.Undo().Ok);
        Assert.Equal(new byte[] { 6 }, session.Serialize().Single(output => output.RelativePath == Root + "b").Content.ToArray());
        Assert.True(session.Undo().Ok);
        Assert.False(session.State.IsDirty);
    }

    private static IncomingEdit Edit(string id, int value) => new(id, JsonSerializer.SerializeToElement(value.ToString()));

    private sealed class AdapterPlugin : IGamePlugin, IDocumentGroupPlugin
    {
        public PluginInfo Info { get; } = new("test.sdk04", "Adapter fixture", "1.0.0");
        public ProbeResult Probe(ISaveFile file) => ProbeResult.Match(ProbeScore.Format, "Fixture");
        public ISaveDocument Open(ISaveFile file) => new MemberAdapterDocument(file.RelativePath, file.ReadAllBytes()[0]);
        public IReadOnlyList<DocumentGroupProposal> ProposeGroups(ISaveFile anchor) => [new("pair", "Pair",
            anchor.Directory.Files.Select(file => new DocumentGroupMember(file.FileName, file.RelativePath)).ToArray())];
        public IReadOnlyList<FieldError> ValidateGroup(DocumentGroupInput input, IReferenceContext references) => [];
        public IGroupedSaveDocument OpenGroup(DocumentGroupInput input, IReferenceContext references) => new MemberAdapterGroup(input);
    }
}
