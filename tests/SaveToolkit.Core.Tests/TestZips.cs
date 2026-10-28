using System.IO.Compression;

namespace SaveToolkit.Core.Tests;

internal static class TestZips
{
    /// <summary>Builds a zip; entries may repeat (as in real multi-snapshot dumps).</summary>
    public static byte[] Build(params (string Path, byte[] Content)[] entries)
    {
        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (path, content) in entries)
            {
                using var s = zip.CreateEntry(path).Open();
                s.Write(content);
            }
        }

        return buffer.ToArray();
    }

    public static string TempDir() => Path.Combine(Path.GetTempPath(), "SaveToolkit.Tests", Guid.NewGuid().ToString("N"));
}
