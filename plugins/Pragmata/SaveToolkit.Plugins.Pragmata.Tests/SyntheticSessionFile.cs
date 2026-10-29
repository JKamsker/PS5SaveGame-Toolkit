using SaveToolkit.Core.Storage;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.Pragmata.Tests;

/// <summary>Owns only its uniquely named import folder; session originals read these same bytes.</summary>
internal sealed class SyntheticSessionFile : IDisposable
{
    public string StorageRoot { get; } = Path.Combine(TestScratch.ProcessFolder, $"pragmata-{Guid.NewGuid():N}");
    public DiskSaveFile File { get; }

    public SyntheticSessionFile(byte[] bytes)
    {
        try
        {
            var source = new ImportSource("PPSA02530/sdimg_SAVESERVICE-LINE-0-0/data000.bin",
                () => new MemoryStream(bytes, writable: false));
            File = SaveSetImporter.Import([source], StorageRoot).AllDiskFiles.Single();
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(StorageRoot)) Directory.Delete(StorageRoot, recursive: true);
    }
}
