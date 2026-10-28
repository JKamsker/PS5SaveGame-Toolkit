using System.IO.Compression;
using SaveToolkit.Core.Storage;

namespace SaveToolkit.Plugins.FinalFantasy16.Tests;

internal sealed class FaithSessionFile : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "ff16-session-" + Guid.NewGuid().ToString("N"));

    public FaithSessionFile(byte[] bytes)
    {
        using var memory = new MemoryStream();
        using (var zip = new ZipArchive(memory, ZipArchiveMode.Create, leaveOpen: true))
        {
            using var stream = zip.CreateEntry("PPSA10665/sdimg_FAITHDATA0001/game-save-data").Open();
            stream.Write(bytes);
        }
        var zipBytes = memory.ToArray();
        var set = SaveSetImporter.Import([new ImportSource("synthetic.zip", () => new MemoryStream(zipBytes))], _directory);
        File = Assert.Single(set.AllDiskFiles);
    }

    public DiskSaveFile File { get; }

    public void Dispose() => Directory.Delete(_directory, recursive: true);
}
