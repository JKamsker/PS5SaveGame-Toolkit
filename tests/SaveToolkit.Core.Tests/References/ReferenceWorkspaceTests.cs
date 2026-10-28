using System.Text.Json;
using SaveToolkit.Core.Export;
using SaveToolkit.Core.Plugins;
using SaveToolkit.Core.References;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Core.Storage;
using SaveToolkit.Core.Tests.Fakes;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Core.Tests.References;

public sealed class ReferenceWorkspaceTests : IDisposable
{
    private readonly string _packRoot = TestZips.TempDir();
    private readonly List<WorkspaceStore> _stores = [];

    public void Dispose()
    {
        foreach (var store in _stores)
        {
            store.Dispose();
        }

        if (Directory.Exists(_packRoot))
        {
            Directory.Delete(_packRoot, recursive: true);
        }
    }

    private WorkspaceStore Store(ReferencePackStore? packs = null)
    {
        var catalog = new PluginCatalog([new ItemsPlugin(), new RawFilePlugin()]);
        var store = new WorkspaceStore(catalog, new WorkspaceStoreOptions { DataRoot = TestZips.TempDir() }, packs);
        _stores.Add(store);
        return store;
    }

    private static (Workspace Workspace, DocumentSession Session) Open(WorkspaceStore store)
    {
        var zip = TestZips.Build(($"{ItemsPlugin.TitleId}/sdimg_SAVE/items.dat", ItemsPlugin.Create(1, (1001, 3), (2001, 1), (9999, 7))));
        var ws = store.Create("items", [new ImportSource("dump.zip", () => new MemoryStream(zip))]);
        var session = ws.Open(ws.Set.AllDiskFiles.Single().Id, pluginId: null);
        return (ws, session);
    }

    private static TableNode Table(DocumentSession session) => (TableNode)((SectionNode)session.BuildView()).Children[0];

    private static FieldNode Quantity(TableNode table, int row) => (FieldNode)table.Rows[row].Cells["quantity"];

    [Fact]
    public void Open_PinsEmbeddedPack_PluginShowsNamesLimitsIconsAndAttribution()
    {
        var (ws, session) = Open(Store());

        var table = Table(session);

        Assert.Equal("Potion", Quantity(table, 0).Label);
        Assert.Equal(99, (int)((Sdk.Values.IntegerSpec)Quantity(table, 0).Spec).Max);
        Assert.Contains("PS5 Save Game Toolkit contributors", Quantity(table, 0).Description, StringComparison.Ordinal);
        Assert.Equal("Item 9999", Quantity(table, 2).Label);
        var icon = Assert.IsType<ImageNode>(table.Rows[0].Cells["icon"]);
        Assert.Equal("image/png", session.GetAsset(icon.AssetId)!.ContentType);

        var sources = ws.References(session.Id).Datasets();
        Assert.All(sources.Where(s => s.Pack.Id == "test.items.en"), s => Assert.True(s.Used));
    }

    [Fact]
    public void Open_WithoutPacks_FallsBackToRawIds()
    {
        // A pack store built from an assembly without embedded packs: nothing to pin.
        var (ws, session) = Open(Store(new ReferencePackStore([new RawFilePlugin()])));

        var table = Table(session);

        Assert.Equal("Item 1001", Quantity(table, 0).Label);
        Assert.Null(Quantity(table, 0).Description);
        Assert.Equal(Confidence.Unknown, Quantity(table, 0).Confidence);
        Assert.Empty(ws.References(session.Id).Datasets());
    }

    [Fact]
    public void Datasets_AreUnusedUntilThePluginResolvesThem()
    {
        var packs = new ReferencePackStore([new ItemsPlugin()]);

        var context = packs.Pin(new ItemsPlugin(), ItemsPlugin.TitleId);

        Assert.All(context.Datasets(), d => Assert.False(d.Used));
        _ = context.Catalog("items");
        Assert.True(context.Datasets().Single(d => d.Dataset.Id == "items").Used);
        Assert.False(context.Datasets().Single(d => d.Dataset.Id == "icons").Used);
    }

    [Fact]
    public void PackImportedAfterOpen_DoesNotChangeOpenDocument_ButNextOpenSeesIt()
    {
        var packs = new ReferencePackStore([new ItemsPlugin()]);
        var store = Store(packs);
        var (ws, session) = Open(store);

        packs.Import(PackBuilder.WithItems("User Potion").Zip(), "user.zip");

        Assert.Equal("Potion", Quantity(Table(session), 0).Label);
        Assert.DoesNotContain(ws.References(session.Id).Packs, p => p.Id == "user.items");
        ws.Close(session.Id);
        var reopened = ws.Open(ws.Set.AllDiskFiles.Single().Id, pluginId: null);
        Assert.Equal("User Potion", Quantity(Table(reopened), 0).Label);
        Assert.Empty(ws.References(session.Id).Packs);
    }

    [Fact]
    public void Edit_LimitFromPackIsEnforced_AndExportVerifiesWithTheSameContext()
    {
        var (ws, session) = Open(Store());
        var valueId = Quantity(Table(session), 0).ValueId;

        Assert.False(session.ApplyEdits([new IncomingEdit(valueId, JsonDocument.Parse("\"100\"").RootElement)]).Ok);
        Assert.True(session.ApplyEdits([new IncomingEdit(valueId, JsonDocument.Parse("\"99\"").RootElement)]).Ok);

        var plan = WorkspaceExporter.Plan(ws, new ExportOptions());
        Assert.Single(plan.Overrides);
    }

    [Fact]
    public void UserPacks_PersistAcrossRestarts_AndCanBeRemoved()
    {
        var options = new ReferencePackOptions { StorageRoot = _packRoot };
        var first = new ReferencePackStore([new ItemsPlugin()], options);
        first.Import(PackBuilder.WithItems().Zip(), "user.zip");

        var second = new ReferencePackStore([new ItemsPlugin()], options);

        Assert.Contains(second.Packs, p => p.Id == "user.items" && p.Info.Origin == Sdk.References.ReferencePackOrigin.User);
        Assert.True(second.Remove("user.items"));
        Assert.False(File.Exists(Path.Combine(_packRoot, "user.items.zip")));
        Assert.Throws<InvalidOperationException>(() => second.Remove("test.items.en"));
        Assert.False(second.Remove("nope"));
    }

    [Fact]
    public void BrokenStoredPack_IsReportedAsLoadError_NotThrown()
    {
        Directory.CreateDirectory(_packRoot);
        File.WriteAllBytes(Path.Combine(_packRoot, "broken.zip"), [1, 2, 3]);

        var store = new ReferencePackStore([new ItemsPlugin()], new ReferencePackOptions { StorageRoot = _packRoot });

        var error = Assert.Single(store.LoadErrors);
        Assert.Equal("broken.zip", error.Origin);
        Assert.Contains(store.Packs, p => p.Id == "test.items.en");
    }
}
