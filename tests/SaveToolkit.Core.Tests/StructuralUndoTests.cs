using System.Text.Json;
using SaveToolkit.Core.Plugins;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Core.Storage;
using SaveToolkit.Core.Tests.Fakes;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Core.Tests;

public sealed class StructuralUndoTests : IDisposable
{
    private static readonly byte[] Original = RosterPlugin.Create((1, 5, "ann"), (2, 7, "bob"));
    private static readonly Dictionary<string, JsonElement> NoParameters = [];

    private readonly RosterPlugin _plugin = new();
    private readonly WorkspaceStore _store;

    public StructuralUndoTests()
    {
        _store = new WorkspaceStore(new PluginCatalog([_plugin]), new WorkspaceStoreOptions { DataRoot = TestZips.TempDir() });
    }

    public void Dispose() => _store.Dispose();

    private RosterPlugin.RosterDocument Doc => _plugin.LastOpened!;

    private byte[] DocumentBytes => Doc.Serialize().Single().Content.ToArray();

    private static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement;

    private static Dictionary<string, JsonElement> Param(string name, string json) => new() { [name] = Json(json) };

    private DocumentSession Open(DocumentSessionOptions? options = null)
    {
        var zip = TestZips.Build(("PPSA99998/sdimg_SAVE/roster.dat", Original));
        var ws = _store.Create("roster", [new ImportSource("dump.zip", () => new MemoryStream(zip))]);
        var file = ws.Set.AllDiskFiles.Single();
        var session = new DocumentSession("doc-test", file, _plugin, _plugin.Open(file), options,
            () => new RosterPlugin.RosterDocument(file.RelativePath, Original));
        _ = session.BuildView();
        return session;
    }

    private static string Name(DocumentSession session, ushort id)
    {
        var table = ((SectionNode)session.BuildView()).Children.OfType<TableNode>().Single();
        var row = table.Rows.Single(r => r.Key == $"m{id}");
        return ((FieldNode)row.Cells["name"]).Value.AsString();
    }

    private static void AssertOk(SessionEditResult result) =>
        Assert.True(result.Ok, string.Join("; ", result.Errors.Select(e => e.Message)));

    [Fact]
    public void Add_Undo_Redo_RestoresBytesModelAndBindings()
    {
        using var session = Open();

        var add = session.InvokeAction("add", Param("name", "\"cid\""));
        AssertOk(add);
        Assert.True(add.StructureChanged);
        Assert.Equal(1, add.State.Revision);
        Assert.Equal([1, 2, 3], Doc.MemberIds);
        AssertOk(session.ApplyEdits([new IncomingEdit("member/3/name", Json("\"cy\""))]));

        AssertOk(session.Undo());
        var undoAdd = session.Undo();
        AssertOk(undoAdd);
        Assert.True(undoAdd.StructureChanged);
        Assert.Equal([1, 2], Doc.MemberIds);
        Assert.Equal(Original, DocumentBytes);
        Assert.False(session.State.IsDirty);
        Assert.Empty(session.Serialize());

        AssertOk(session.Redo());
        AssertOk(session.Redo());
        Assert.Equal([1, 2, 3], Doc.MemberIds);
        Assert.Equal("cy", Name(session, 3));
        Assert.False(session.State.CanRedo);

        // Bindings of the re-created record are live again.
        AssertOk(session.ApplyEdits([new IncomingEdit("member/3/name", Json("\"cyd\""))]));
        Assert.Equal("cyd", Name(session, 3));
    }

    [Fact]
    public void RemoveAndReorder_UndoToOriginal_IsByteIdentical()
    {
        using var session = Open();

        AssertOk(session.InvokeAction("move-first", Param("member", "\"2\"")));
        Assert.Equal([2, 1], Doc.MemberIds);
        AssertOk(session.InvokeAction("remove", Param("member", "\"1\"")));
        Assert.Equal([2], Doc.MemberIds);
        AssertOk(session.ApplyEdits([new IncomingEdit("member/2/name", Json("\"bobby\""))]));

        while (session.State.CanUndo)
        {
            AssertOk(session.Undo());
        }

        Assert.Equal(Original, DocumentBytes);
        Assert.False(session.State.IsDirty);
        Assert.Equal(6, session.State.Revision);
    }

    [Fact]
    public void StaleIds_AreRejected_AfterStructuralChange()
    {
        using var session = Open();
        _ = session.Expand("details/1");
        AssertOk(session.InvokeAction("remove", Param("member", "\"1\"")));

        var edit = session.ApplyEdits([new IncomingEdit("member/1/name", Json("\"x\""))]);
        Assert.True(edit.Stale);
        Assert.False(edit.Ok);
        Assert.Throws<StaleViewException>(() => session.Expand("details/1"));
        Assert.Throws<KeyNotFoundException>(() => session.Expand("details/99"));

        // Ids that come back with undo are live again.
        AssertOk(session.Undo());
        AssertOk(session.ApplyEdits([new IncomingEdit("member/1/name", Json("\"x\""))]));
    }

    [Fact]
    public void OldRevision_IsRejectedAsStale_EvenWhenTheIdStillExists()
    {
        using var session = Open();
        var before = session.State.Revision;
        AssertOk(session.InvokeAction("move-first", Param("member", "\"2\"")));

        var edit = session.ApplyEdits([new IncomingEdit("member/1/name", Json("\"x\""))], expectedRevision: before);
        var action = session.InvokeAction("remove", Param("member", "\"1\""), expectedRevision: before);

        Assert.True(edit.Stale);
        Assert.True(action.Stale);
        Assert.Equal([2, 1], Doc.MemberIds);
        AssertOk(session.ApplyEdits([new IncomingEdit("member/1/name", Json("\"x\""))], expectedRevision: session.State.Revision));
    }

    [Fact]
    public void LazyBindings_SurviveStructuralChanges_WithoutClientReExpansion()
    {
        using var session = Open();
        _ = session.Expand("details/2");
        AssertOk(session.ApplyEdits([new IncomingEdit("member/2/level", Json("\"50\""))]));

        AssertOk(session.InvokeAction("remove", Param("member", "\"1\"")));
        AssertOk(session.ApplyEdits([new IncomingEdit("member/2/level", Json("\"60\""))]));

        while (session.State.CanUndo)
        {
            AssertOk(session.Undo());
        }

        Assert.Equal(Original, DocumentBytes);
    }

    [Fact]
    public void LazyBindings_OfARemovedRecord_ComeBackWithUndo()
    {
        using var session = Open();
        _ = session.Expand("details/1");
        AssertOk(session.ApplyEdits([new IncomingEdit("member/1/level", Json("\"40\""))]));
        AssertOk(session.InvokeAction("remove", Param("member", "\"1\"")));

        AssertOk(session.Undo());
        AssertOk(session.Undo());

        Assert.Equal(Original, DocumentBytes);
        AssertOk(session.Redo());
        AssertOk(session.ApplyEdits([new IncomingEdit("member/1/level", Json("\"41\""))]));
    }

    [Fact]
    public void UndoAndRedo_AreRejectedInsideAGroup()
    {
        using var session = Open();
        AssertOk(session.ApplyEdits([new IncomingEdit("member/1/name", Json("\"x\""))]));

        var group = session.RunGroup(s => s.Undo());

        Assert.False(group.Ok);
        Assert.True(session.State.CanUndo);
    }

    [Fact]
    public void FailedStructuralAction_ChangesNothing()
    {
        using var session = Open();

        var failed = session.InvokeAction("add", Param("name", $"\"{RosterPlugin.FailingName}\""));

        Assert.False(failed.Ok);
        Assert.Equal(Original, DocumentBytes);
        Assert.False(session.State.CanUndo);
        Assert.Equal(0, session.State.Revision);
    }

    [Fact]
    public void NonUndoableAction_StillClearsHistoryExplicitly()
    {
        using var session = Open();
        AssertOk(session.InvokeAction("add", Param("name", "\"cid\"")));

        var clear = session.InvokeAction("clear-all", NoParameters);

        AssertOk(clear);
        Assert.True(clear.StructureChanged);
        Assert.False(clear.State.CanUndo);
        Assert.True(clear.State.IsDirty);
        Assert.True(session.ApplyEdits([new IncomingEdit("member/1/name", Json("\"x\""))]).Stale);
    }

    [Fact]
    public void SnapshotMemory_EvictsOldestHistory_WithNotice()
    {
        // Snapshots of 2, 3 and 4 members are 43, 59 and 75 bytes; 140 bytes hold only the newest two.
        using var session = Open(new DocumentSessionOptions { MaxSnapshotBytes = 140 });
        AssertOk(session.InvokeAction("add", Param("name", "\"c\"")));
        AssertOk(session.InvokeAction("add", Param("name", "\"d\"")));

        var third = session.InvokeAction("add", Param("name", "\"e\""));

        AssertOk(third);
        Assert.Contains(third.Warnings, w => w.Contains("trimmed", StringComparison.Ordinal));
        Assert.True(third.State.CanUndo);

        // Undo keeps a 91-byte redo snapshot (5 members); 59 + 91 exceeds the budget, so the oldest undo goes.
        var undo = session.Undo();
        AssertOk(undo);
        Assert.Contains(undo.Warnings, w => w.Contains("trimmed", StringComparison.Ordinal));
        Assert.Equal([1, 2, 3, 4], Doc.MemberIds);
        Assert.False(undo.State.CanUndo);
        Assert.True(undo.State.CanRedo);
        Assert.True(undo.State.IsDirty);

        AssertOk(session.Redo());
        Assert.Equal([1, 2, 3, 4, 5], Doc.MemberIds);
    }

    [Fact]
    public void ChangeLargerThanBudget_IsAppliedWithoutUndo_AndSaysSo()
    {
        using var session = Open(new DocumentSessionOptions { MaxSnapshotBytes = 10 });
        AssertOk(session.ApplyEdits([new IncomingEdit("member/1/name", Json("\"x\""))]));

        var add = session.InvokeAction("add", Param("name", "\"cid\""));

        AssertOk(add);
        Assert.Contains(add.Warnings, w => w.Contains("too large to undo", StringComparison.Ordinal));
        Assert.False(add.State.CanUndo);
        Assert.True(add.State.IsDirty);
    }

    [Fact]
    public void RunGroup_RecordsOneUndoEntry_AcrossStructuralAndValueSteps()
    {
        using var session = Open();

        var group = session.RunGroup(s =>
        {
            var add = s.InvokeAction("add", Param("name", "\"cid\""));
            return add.Ok ? s.ApplyEdits([new IncomingEdit("member/3/name", Json("\"cy\"")), new IncomingEdit("member/1/name", Json("\"anna\""))]) : add;
        });

        AssertOk(group);
        Assert.Equal("anna", Name(session, 1));
        AssertOk(session.Undo());
        Assert.False(session.State.CanUndo);
        Assert.Equal(Original, DocumentBytes);

        AssertOk(session.Redo());
        Assert.Equal("cy", Name(session, 3));
        Assert.Equal("anna", Name(session, 1));
    }

    [Fact]
    public void RunGroup_RollsBackEverything_WhenOneStepFails()
    {
        using var session = Open();
        AssertOk(session.ApplyEdits([new IncomingEdit("member/2/name", Json("\"bo\""))]));
        var bytesBefore = DocumentBytes;

        var group = session.RunGroup(s =>
        {
            AssertOk(s.InvokeAction("add", Param("name", "\"cid\"")));
            AssertOk(s.ApplyEdits([new IncomingEdit("member/1/name", Json("\"anna\""))]));
            return s.InvokeAction("add", Param("name", $"\"{RosterPlugin.FailingName}\""));
        });

        Assert.False(group.Ok);
        Assert.Equal(bytesBefore, DocumentBytes);
        Assert.Equal([1, 2], Doc.MemberIds);
        AssertOk(session.Undo());
        Assert.Equal(Original, DocumentBytes);
    }
}
