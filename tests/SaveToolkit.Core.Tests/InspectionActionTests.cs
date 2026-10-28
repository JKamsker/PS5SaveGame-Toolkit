using System.Text.Json;
using SaveToolkit.Core.Plugins;
using SaveToolkit.Core.References;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Core.Storage;
using SaveToolkit.Core.Tests.Fakes;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Core.Tests;

public sealed class InspectionActionTests : IDisposable
{
    private readonly List<WorkspaceStore> _stores = [];
    public void Dispose() { foreach (var store in _stores) store.Dispose(); }

    private DocumentSession Open(bool editable = false)
    {
        var plugin = new InspectionPlugin(editable);
        var packs = new ReferencePackStore([plugin]);
        var store = new WorkspaceStore(new PluginCatalog([plugin]), new WorkspaceStoreOptions { DataRoot = TestZips.TempDir() }, packs);
        _stores.Add(store);
        var bytes = TestZips.Build(($"{ItemsPlugin.TitleId}/sdimg_SAVE/inspect.dat", new byte[] { 5, 7 }));
        var workspace = store.Create("inspection", [new ImportSource("inspection.zip", () => new MemoryStream(bytes))]);
        var session = workspace.Open(workspace.Set.AllDiskFiles.Single().Id, null);
        session.BuildView();
        return session;
    }

    [Fact]
    public void ReadOnlyInspection_IsAttributed_AndPreservesState()
    {
        var session = Open();
        var before = session.State;
        var result = session.InvokeAction("inspect", new Dictionary<string, JsonElement>(), expectedRevision: 0);
        Assert.True(result.Ok);
        Assert.Equal("Potion: 5", result.Message);
        Assert.Equal(["Inspection only."], result.Warnings);
        Assert.Equal(before, result.State);
        Assert.Empty(result.Changes);
        Assert.False(result.StructureChanged);
        Assert.Empty(session.Serialize());
        Assert.False(session.InvokeAction("legacy", new Dictionary<string, JsonElement>()).Ok);
        Assert.True(session.InvokeAction("inspect", new Dictionary<string, JsonElement>(), expectedRevision: 1).Stale);
    }

    [Theory]
    [InlineData("reported")]
    [InlineData("silent")]
    [InlineData("hidden")]
    [InlineData("transient-change")]
    [InlineData("structural")]
    [InlineData("irreversible")]
    [InlineData("memento")]
    [InlineData("throws")]
    [InlineData("planned")]
    public void BrokenInspection_IsRejectedWithoutTouchingLiveState(string actionId)
    {
        var session = Open();
        var before = session.State;
        var result = session.InvokeAction(actionId, new Dictionary<string, JsonElement>());
        Assert.False(result.Ok);
        Assert.Empty(result.Changes);
        Assert.Equal(before, session.State);
        var fields = ((SectionNode)session.BuildView()).Children.OfType<FieldNode>();
        Assert.Equal(FieldValue.Of(5L), fields.Single(f => f.ValueId == "number").Value);
        Assert.Equal(FieldValue.Of(0L), fields.Single(f => f.ValueId == "transient").Value);
        Assert.Equal("Potion: 5", session.InvokeAction("inspect", new Dictionary<string, JsonElement>()).Message);
    }

    [Fact]
    public void InspectionParameters_UsePublishedDefaultsAndBounds()
    {
        var session = Open();
        Assert.Equal("7", session.InvokeAction("parameter", new Dictionary<string, JsonElement>()).Message);
        Assert.Equal("9", session.InvokeAction("parameter", new Dictionary<string, JsonElement>
        {
            ["value"] = JsonSerializer.SerializeToElement("9"),
        }).Message);
        Assert.False(session.InvokeAction("parameter", new Dictionary<string, JsonElement>
        {
            ["value"] = JsonSerializer.SerializeToElement("10"),
        }).Ok);
        Assert.Equal(0, session.State.Revision);
        Assert.False(session.State.IsDirty);
    }

    [Fact]
    public void Inspection_ReopensCurrentEditsAndExpandedBindings_PreservingUndoAndRedo()
    {
        var session = Open(editable: true);
        session.Expand("details");
        Assert.True(session.ApplyEdits([new IncomingEdit("number", JsonSerializer.SerializeToElement("9"))]).Ok);
        var before = session.State;
        Assert.Equal("Potion: 9", session.InvokeAction("inspect", new Dictionary<string, JsonElement>()).Message);
        Assert.Equal("9", session.InvokeAction("lazy-inspect", new Dictionary<string, JsonElement>()).Message);
        Assert.False(session.InvokeAction("silent", new Dictionary<string, JsonElement>()).Ok);
        Assert.Equal(before, session.State);
        Assert.Equal(9, Assert.Single(session.Serialize()).Content.Span[0]);
        Assert.True(session.Undo().Ok);
        var undone = session.State;
        Assert.True(session.InvokeAction("inspect", new Dictionary<string, JsonElement>()).Ok);
        Assert.Equal(undone, session.State);
        Assert.True(session.Redo().Ok);
        Assert.Equal(9, Assert.Single(session.Serialize()).Content.Span[0]);
    }
}
