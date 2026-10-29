using System.IO.Compression;
using SaveToolkit.Core.Plugins;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Core.Storage;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.BaldursGate3.Tests;

/// <summary>Imports only authored bytes; callers dispose their documents/sessions before this fixture.</summary>
internal sealed class SyntheticSaveWorkspace : IDisposable
{
    private readonly WorkspaceStore? _store;

    internal SyntheticSaveWorkspace(string fileName, byte[] content)
    {
        RootPath = Path.Combine(Path.GetFullPath(TestScratch.ProcessFolder), Guid.NewGuid().ToString("N"));
        Plugin = new BaldursGate3Plugin();
        try
        {
            _store = new WorkspaceStore(new PluginCatalog([Plugin]), new WorkspaceStoreOptions { DataRoot = RootPath });
            using var zipBytes = new MemoryStream();
            using (var zip = new ZipArchive(zipBytes, ZipArchiveMode.Create, leaveOpen: true))
            {
                using var entry = zip.CreateEntry("PPSA18463/sdimg_SAVE/" + fileName).Open();
                entry.Write(content);
            }
            var archive = zipBytes.ToArray();
            var workspace = _store.Create("synthetic-bg3", [new ImportSource("fixture.zip", () => new MemoryStream(archive))]);
            File = Assert.Single(workspace.Set.AllDiskFiles);
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    internal string RootPath { get; }
    internal BaldursGate3Plugin Plugin { get; }
    internal DiskSaveFile File { get; }

    public void Dispose()
    {
        try { _store?.Dispose(); }
        finally
        {
            // RootPath is this fixture's immutable absolute path with a generated single GUID leaf.
            if (Directory.Exists(RootPath)) Directory.Delete(RootPath, recursive: true);
        }
    }
}
