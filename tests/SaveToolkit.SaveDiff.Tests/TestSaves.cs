using System.IO.Compression;
using SaveToolkit.Testing;
using SaveToolkit.Testing.Diff;

namespace SaveToolkit.SaveDiff.Tests;

/// <summary>Synthetic dumps in the PS5 layout; nothing here comes from real samples.</summary>
internal static class TestSaves
{
    public const string Title = "PPSA99999";

    public static string Primary(string file, string dir = "SLOT") => $"{Title}/sdimg_{dir}/{file}";

    public static string Backup(string file, string dir = "SLOT") => $"{Title}/sdimg_sce_bu_{dir}/{file}";

    /// <summary>Builds a zip; entries may repeat (appended snapshots → generations).</summary>
    public static byte[] Zip(params (string Path, byte[] Content)[] entries)
    {
        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (path, content) in entries)
            {
                using var stream = zip.CreateEntry(path).Open();
                stream.Write(content);
            }
        }

        return buffer.ToArray();
    }

    public static string TempDir()
    {
        var dir = Path.Combine(TestScratch.ProcessFolder, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    public static string WriteZip(string dir, string name, params (string Path, byte[] Content)[] entries)
    {
        var path = Path.Combine(dir, name);
        File.WriteAllBytes(path, Zip(entries));
        return path;
    }

    public static LoadedSaveSet Load(params (string Path, byte[] Content)[] entries)
    {
        var path = WriteZip(TempDir(), "dump.zip", entries);
        return SaveSetLoader.Load(path);
    }

    public static byte[] Bytes(int length, byte fill = 0) => Enumerable.Repeat(fill, length).ToArray();

    public static byte[] With(byte[] source, int offset, params byte[] patch)
    {
        var copy = source.ToArray();
        patch.CopyTo(copy, offset);
        return copy;
    }

    public static byte[] Utf8(string text) => System.Text.Encoding.UTF8.GetBytes(text);
}
