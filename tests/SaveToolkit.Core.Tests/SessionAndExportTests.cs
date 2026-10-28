using System.Text.Json;
using SaveToolkit.Core.Export;
using SaveToolkit.Core.Plugins;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Core.Storage;
using SaveToolkit.Core.Tests.Fakes;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Core.Tests;

public sealed class SessionAndExportTests : IDisposable
{
    private readonly WorkspaceStore _store = new(
        new PluginCatalog([new CounterPlugin(), new RawFilePlugin()]),
        new WorkspaceStoreOptions { DataRoot = TestZips.TempDir() });

    public void Dispose() => _store.Dispose();

    private static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement;

    private (Workspace Workspace, DocumentSession Session) OpenCounter(int initial = 5)
    {
        var zip = TestZips.Build(
            ("PPSA99999/sdimg_SAVE/sce_sys/param.sfo", [0]),
            ("PPSA99999/sdimg_SAVE/save.dat", CounterPlugin.Create(initial)),
            ("PPSA99999/sdimg_sce_bu_SAVE/save.dat", CounterPlugin.Create(initial)),
            ("PPSA99999/sdimg_OTHER/other.dat", [1, 2, 3]));
        var ws = _store.Create("test", [new ImportSource("dump.zip", () => new MemoryStream(zip))]);
        var file = ws.Set.AllDiskFiles.Single(f => f.RelativePath == "PPSA99999/sdimg_SAVE/save.dat");
        var session = ws.Open(file.Id, pluginId: null);
        _ = session.BuildView();
        return (ws, session);
    }

    [Fact]
    public void TitleCatalog_MergesPluginGamesWithKnownTitles()
    {
        var titles = Catalog.TitleCatalog.Build(_store.Catalog);

        var counter = Assert.Single(titles, t => t.TitleId == "PPSA99999");
        Assert.Equal("Counter Game", counter.Name);
        Assert.Equal(Catalog.TitleSupport.Edit, counter.Support);
        Assert.Equal(["test.counter"], counter.PluginIds);
        Assert.Contains(titles, t => t is { TitleId: "PPSA01876", Name: "Far Cry 6", Support: Catalog.TitleSupport.None });
    }

    [Fact]
    public void Probe_PrefersTitlePluginOverFallback()
    {
        var (ws, session) = OpenCounter();

        Assert.Equal("test.counter", session.Plugin.Info.Id);
        Assert.Equal(["test.counter", RawFilePlugin.PluginId], ws.Probe(session.File).Select(m => m.Plugin.Info.Id));
    }

    [Fact]
    public void ApplyEdits_ValidatesAgainstPublishedSpec()
    {
        var (_, session) = OpenCounter();

        Assert.False(session.ApplyEdits([new IncomingEdit("counter", Json("\"5000\""))]).Ok);
        Assert.False(session.ApplyEdits([new IncomingEdit("checksum", Json("\"1\""))]).Ok);
        Assert.False(session.ApplyEdits([new IncomingEdit("nope", Json("1"))]).Ok);
        Assert.False(session.State.IsDirty);
    }

    [Fact]
    public void ApplyEdits_IsAtomic_WhenPluginRejectsOne()
    {
        var (_, session) = OpenCounter();

        var result = session.ApplyEdits([new IncomingEdit("counter", Json("\"7\"")), new IncomingEdit("name", Json("\"ääääääääääääääää\""))]);

        Assert.False(result.Ok);
        var view = (SectionNode)session.BuildView();
        Assert.Equal("5", view.Children.OfType<FieldNode>().Single(f => f.ValueId == "counter").Value.ToString());
    }

    [Fact]
    public void UndoRedo_RestoresValues_AndDirtyState()
    {
        var (_, session) = OpenCounter();

        Assert.True(session.ApplyEdits([new IncomingEdit("counter", Json("\"9\""))]).Ok);
        Assert.True(session.State.IsDirty);

        var undo = session.Undo();
        Assert.True(undo.Ok);
        Assert.Equal("5", undo.Changes.Single(c => c.ValueId == "counter").NewValue.ToString());
        Assert.False(session.State.IsDirty);
        Assert.Empty(session.Serialize());

        Assert.True(session.Redo().Ok);
        Assert.True(session.State.IsDirty);
    }

    [Fact]
    public void InvokeAction_UsesDefaultsAndValidatesParameters()
    {
        var (_, session) = OpenCounter();

        Assert.False(session.InvokeAction("max", new Dictionary<string, JsonElement> { ["value"] = Json("\"2000\"") }).Ok);
        var result = session.InvokeAction("max", new Dictionary<string, JsonElement>());

        Assert.True(result.Ok);
        Assert.Equal("Counter set.", result.Message);
        Assert.True(session.State.CanUndo);
    }

    [Fact]
    public void Undo_RevertsActionChangesToValuesNeverShownInTheView()
    {
        var (_, session) = OpenCounter();
        Assert.True(session.InvokeAction("toggle-hidden", new Dictionary<string, JsonElement>()).Ok);
        Assert.True(session.State.IsDirty);

        var undo = session.Undo();

        Assert.True(undo.Ok, string.Join("; ", undo.Errors.Select(e => e.Message)));
        var reverted = Assert.Single(undo.Changes, c => c.ValueId == "hidden.flag");
        Assert.False(reverted.NewValue.AsBool());
    }

    [Fact]
    public void Export_MirrorsIdenticalBackup_AndVerifiesOutput()
    {
        var (ws, session) = OpenCounter();
        Assert.True(session.ApplyEdits([new IncomingEdit("counter", Json("\"42\""))]).Ok);

        var plan = WorkspaceExporter.Plan(ws, new ExportOptions { Scope = ExportScope.ChangedFiles });

        Assert.Equal(["PPSA99999/sdimg_SAVE/save.dat", "PPSA99999/sdimg_sce_bu_SAVE/save.dat"], plan.Files.Select(f => f.RelativePath).Order());
        Assert.All(plan.Overrides.Values, c => Assert.Equal(CounterPlugin.Create(42), c.ToArray()));
    }

    [Fact]
    public void Export_ChangedDirectories_IncludesSystemFiles()
    {
        var (ws, session) = OpenCounter();
        Assert.True(session.ApplyEdits([new IncomingEdit("counter", Json("\"42\""))]).Ok);

        var plan = WorkspaceExporter.Plan(ws, new ExportOptions { MirrorBackups = false });

        Assert.Equal(["PPSA99999/sdimg_SAVE/save.dat", "PPSA99999/sdimg_SAVE/sce_sys/param.sfo"], plan.Files.Select(f => f.RelativePath).Order());
        using var zip = new MemoryStream();
        WorkspaceExporter.WriteZip(plan, zip);
        Assert.True(zip.Length > 0);
    }

    [Fact]
    public void Open_DifferentPlugin_RefusedWhileDirty()
    {
        var (ws, session) = OpenCounter();
        Assert.True(session.ApplyEdits([new IncomingEdit("flag", Json("true"))]).Ok);

        Assert.Throws<InvalidOperationException>(() => ws.Open(session.File.Id, RawFilePlugin.PluginId));
    }

    [Fact]
    public void RawPlugin_IsReadOnly_AndServesBlobs()
    {
        var (ws, _) = OpenCounter();
        var other = ws.Set.AllDiskFiles.Single(f => f.FileName == "other.dat");

        var raw = ws.Open(other.Id, null);
        var view = (SectionNode)raw.BuildView();

        Assert.False(raw.CanEdit);
        var hex = view.Children.OfType<HexNode>().Single();
        Assert.Equal([2, 3], raw.ReadBlob(hex.BlobId, 1, 100).ToArray());
        Assert.Throws<KeyNotFoundException>(() => raw.ReadBlob("unknown", 0, 1));
    }
}
