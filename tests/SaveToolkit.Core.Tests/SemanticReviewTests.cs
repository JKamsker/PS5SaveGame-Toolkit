using System.Text.Json;
using SaveToolkit.Core.Plugins;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Core.Storage;
using SaveToolkit.Core.Tests.Fakes;
using SaveToolkit.Sdk.Values;

namespace SaveToolkit.Core.Tests;

public sealed class SemanticReviewTests : IDisposable
{
    private static readonly Dictionary<string, JsonElement> NoParameters = [];
    private readonly WorkspaceStore _store = new(new PluginCatalog([new CounterPlugin(), new RosterPlugin()]),
        new WorkspaceStoreOptions { DataRoot = TestZips.TempDir() });

    public void Dispose() => _store.Dispose();

    private DocumentSession Open(byte[] bytes, string plugin)
    {
        var zip = TestZips.Build(("PPSA99999/sdimg_SAVE/save.dat", bytes));
        var workspace = _store.Create("review", [new ImportSource("dump.zip", () => new MemoryStream(zip))]);
        var session = workspace.Open(workspace.Set.AllDiskFiles.Single().Id, plugin);
        _ = session.BuildView();
        return session;
    }

    [Fact]
    public void UnexposedReportedValues_AreTrackedAcrossUndoAndRedo()
    {
        using var session = Open(CounterPlugin.Create(5), "test.counter");
        Assert.True(session.InvokeAction("toggle-hidden", NoParameters).Ok);
        var change = session.GetChanges().Changes.Single(c => c.ValueId == "hidden.flag");
        Assert.Equal("false", change.OriginalValue.ToString());
        Assert.Equal("true", change.CurrentValue.ToString());
        Assert.Empty(change.Path);
        Assert.False(change.CanRevert);
        Assert.True(session.Undo().Ok);
        Assert.Empty(session.GetChanges().Changes);
        Assert.True(session.Redo().Ok);
        Assert.Contains(session.GetChanges().Changes, c => c.ValueId == "hidden.flag");
    }

    [Fact]
    public void ReadOnlyDerivedValueIsComparedEvenIfPluginDoesNotReportIt()
    {
        using var session = Open(CounterPlugin.Create(5), "test.counter");
        Assert.True(session.ApplyEdits([new IncomingEdit("counter", JsonSerializer.SerializeToElement("7"))]).Ok);
        var log = session.GetChanges();
        Assert.Equal(2, log.Changes.Count);
        Assert.False(log.Changes.Single(c => c.ValueId == "checksum").CanRevert);
        Assert.True(session.RevertChange("counter", log.Revision).Ok);
        Assert.Empty(session.GetChanges().Changes);
    }

    [Fact]
    public void AddedFields_AreNotMistakenForOriginalValues_AfterFurtherEdits()
    {
        using var session = Open(RosterPlugin.Create((1, 5, "ann")), "test.roster");
        Assert.True(session.InvokeAction("add", new Dictionary<string, JsonElement> { ["name"] = JsonSerializer.SerializeToElement("bob") }).Ok);
        Assert.True(session.ApplyEdits([new IncomingEdit("member/2/name", JsonSerializer.SerializeToElement("bobby"))]).Ok);
        Assert.True(session.GetChanges().Complete);
        var change = session.GetChanges().Changes.Single(c => c.ValueId == "member/2/name");
        Assert.False(change.OriginalExists);
        Assert.True(change.CurrentExists);
        Assert.Equal("bobby", change.CurrentValue.ToString());
        Assert.False(change.CanRevert);
        Assert.True(session.Undo().Ok);
        Assert.True(session.Undo().Ok);
        Assert.Empty(session.GetChanges().Changes);
    }

    [Fact]
    public void EagerAndLazyReviewJsonRemainsByteIdentical()
    {
        using var session = Open(RosterPlugin.Create((1, 5, "ann")), "test.roster");
        _ = session.Expand("details/1");
        Assert.True(session.ApplyEdits([
            new IncomingEdit("member/1/name", JsonSerializer.SerializeToElement("anne")),
            new IncomingEdit("member/1/level", JsonSerializer.SerializeToElement("6")),
        ]).Ok);
        var expected = new
        {
            Changes = new object[]
            {
                new { ValueId = "member/1/name", Label = "Name", Path = new[] { "section:Roster", "row:m1" },
                    OriginalValue = FieldValue.Of("ann"), CurrentValue = FieldValue.Of("anne"),
                    OriginalExists = true, CurrentExists = true, CanRevert = true },
                new { ValueId = "member/1/level", Label = "Level", Path = new[] { "section:Roster", "section:details/1" },
                    OriginalValue = FieldValue.Of(5L), CurrentValue = FieldValue.Of(6L),
                    OriginalExists = true, CurrentExists = true, CanRevert = true },
            },
            Complete = true, Warnings = Array.Empty<string>(), Revision = 1L,
        };
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        Assert.Equal(JsonSerializer.SerializeToUtf8Bytes(expected, options),
            JsonSerializer.SerializeToUtf8Bytes(session.GetChanges(), options));
    }
}
