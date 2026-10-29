using System.Text.Json;
using SaveToolkit.Core.Plugins;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Core.Storage;
using SaveToolkit.Core.Tests.Fakes;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Views;

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

    [Fact]
    public void TargetedMetadataCapturesUnloadedOriginalWithCanonicalLabelAndPath()
    {
        var zip = TestZips.Build(("PPSA99999/sdimg_SAVE/save.dat", new byte[] { 1 }));
        using var workspace = _store.Create("targeted review", [new ImportSource("dump.zip", () => new MemoryStream(zip))]);
        using var current = new TargetedMetadataDocument(1);
        using var session = new DocumentSession("targeted", workspace.Set.AllDiskFiles.Single(),
            new CounterPlugin(), current, openOriginal: () => new TargetedMetadataDocument(1));
        _ = session.BuildView();

        Assert.True(session.ApplyEdits([new IncomingEdit("hidden", JsonSerializer.SerializeToElement(2))]).Ok);
        var change = Assert.Single(session.GetChanges().Changes);
        Assert.Equal("Canonical quantity", change.Label);
        Assert.Equal(["section:canonical"], change.Path);
        Assert.True(change.CanRevert);
        Assert.Equal("1", change.OriginalValue.ToString());
        Assert.Equal("2", change.CurrentValue.ToString());
    }

    [Fact]
    public void TargetedMetadataBudgetMarksReviewIncompleteWithoutInventingAChange()
    {
        var zip = TestZips.Build(("PPSA99999/sdimg_SAVE/save.dat", new byte[] { 1 }));
        using var workspace = _store.Create("targeted budget", [new ImportSource("dump.zip", () => new MemoryStream(zip))]);
        using var current = new TargetedMetadataDocument(1);
        using var session = new DocumentSession("targeted-budget", workspace.Set.AllDiskFiles.Single(),
            new CounterPlugin(), current, new DocumentSessionOptions { MaxOriginalBytes = 256 },
            () => new TargetedMetadataDocument(1));
        _ = session.BuildView();

        Assert.True(session.ApplyEdits([new IncomingEdit("hidden", JsonSerializer.SerializeToElement(2))]).Ok);
        var log = session.GetChanges();
        Assert.False(log.Complete);
        Assert.Empty(log.Changes);
    }

    private sealed class TargetedMetadataDocument : BoundSaveDocument, IFieldMetadataDocument
    {
        private int _value;

        public TargetedMetadataDocument(int value = 1)
        {
            _value = value;
            RegisterField(new FieldMetadata("hidden", "Canonical quantity", IntegerSpec.Int32)
            {
                Path = ["section:canonical"],
                Presentation = new FieldPresentation { Confidence = Confidence.Inferred, Warning = "Review before exporting." },
            }, () => FieldValue.Of(_value), field => _value = field.AsInt32());
        }

        public override string Title => "Targeted metadata";
        public override ViewNode BuildView() => new TextNode("No rendered fields");
        public override IReadOnlyList<FileOutput> Serialize() => [new("save.dat", new byte[] { (byte)_value })];
    }
}
