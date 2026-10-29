using SaveToolkit.Core.Plugins;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Core.Storage;
using SaveToolkit.Core.Tests.Fakes;

namespace SaveToolkit.Core.Tests;

internal sealed class MetadataSessionFixture : IDisposable
{
    private readonly WorkspaceStore _store;
    public MetadataDocument Document { get; } = new();
    public DocumentSession Session { get; }

    public MetadataSessionFixture()
    {
        var plugin = new PlanningPlugin();
        _store = new WorkspaceStore(new PluginCatalog([plugin]), new WorkspaceStoreOptions { DataRoot = TestZips.TempDir() });
        var zip = TestZips.Build(("PPSA99999/sdimg_SAVE/save.dat", new byte[] { 2 }));
        var workspace = _store.Create("metadata", [new ImportSource("save.zip", () => new MemoryStream(zip))]);
        Session = new DocumentSession("metadata", workspace.Set.AllDiskFiles.Single(), plugin, Document,
            openOriginal: () => new MetadataDocument());
        _ = Session.BuildView();
    }

    public void Dispose() { Session.Dispose(); _store.Dispose(); }
}
