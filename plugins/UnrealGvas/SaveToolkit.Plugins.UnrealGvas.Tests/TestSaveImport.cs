using SaveToolkit.Core.Storage;

namespace SaveToolkit.Plugins.UnrealGvas.Tests;

/// <summary>Owns exactly one synthetic import; declare before documents/sessions so they close first.</summary>
internal sealed class TestSaveImport : IDisposable
{
    public string Root { get; } = Path.Combine(Path.GetTempPath(), "unrealgvas-import-" + Guid.NewGuid().ToString("N"));
    public DiskSaveFile File { get; }

    public TestSaveImport(byte[] bytes, string path = "PPSA01603/sdimg_HL0000/save.sav")
    {
        try
        {
            File = SaveSetImporter.Import([new ImportSource(path, () => new MemoryStream(bytes, writable: false))], Root)
                .AllDiskFiles.Single();
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true);
    }
}
