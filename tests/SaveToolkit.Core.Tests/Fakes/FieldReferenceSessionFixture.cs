using SaveToolkit.Core.Plugins;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Core.Storage;
using SaveToolkit.Core.Tests.Fakes;

namespace SaveToolkit.Core.Tests;

internal sealed class FieldReferenceSessionFixture : IDisposable
{
    private readonly WorkspaceStore _store;
    public FieldReferenceDocument Document { get; }
    public DocumentSession Session { get; }

    public FieldReferenceSessionFixture(bool loaded = true, bool editable = true)
    {
        Document = new() { ShowCanonical = loaded, Editable = editable };
        var plugin = new PlanningPlugin();
        _store = new WorkspaceStore(new PluginCatalog([plugin]), new WorkspaceStoreOptions { DataRoot = TestZips.TempDir() });
        var zip = TestZips.Build(("PPSA99999/sdimg_SAVE/save.dat", new byte[] { 2, 1 }));
        var workspace = _store.Create("references", [new ImportSource("save.zip", () => new MemoryStream(zip))]);
        Session = new DocumentSession("references", workspace.Set.AllDiskFiles.Single(), plugin, Document,
            openOriginal: () => new FieldReferenceDocument { ShowCanonical = loaded, Editable = editable });
    }

    public void Dispose() { Session.Dispose(); _store.Dispose(); }
}
