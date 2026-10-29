using System.Collections.Concurrent;
using System.IO.Compression;
using SaveToolkit.Sdk.Saves;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.FinalFantasy16.Tests;

internal static class FaithSamples
{
    private static readonly ConcurrentDictionary<string, Lazy<IReadOnlyList<ISaveFile>>> Cache = new(StringComparer.Ordinal);

    public static IEnumerable<ISaveFile> Records(string title)
    {
        var path = SampleSaves.FindArchive(title);
        if (path is null)
        {
            Assert.Skip($"Sample archive for {title} is absent.");
        }

        // MemorySaveSet owns the bytes and returns copies on reads. Documents and mutation inputs
        // remain independent, while repeated tests share one decompression of every ZIP occurrence.
        return Cache.GetOrAdd(title, _ => new Lazy<IReadOnlyList<ISaveFile>>(() =>
            Array.AsReadOnly(ReadRecords(title, path).ToArray()))).Value;
    }

    private static IEnumerable<ISaveFile> ReadRecords(string title, string path)
    {
        // Read ZIP entries themselves so repeated paths are tested without content deduplication.
        using var archive = ZipFile.OpenRead(path);
        foreach (var entry in archive.Entries)
        {
            var parts = entry.FullName.Split('/');
            if (parts.Length != 3 || !(parts[^1].StartsWith("game-save-data", StringComparison.Ordinal) ||
                parts[^1] == "system-save-data"))
            {
                continue;
            }

            using var stream = entry.Open();
            using var memory = new MemoryStream();
            stream.CopyTo(memory);
            yield return MemorySaveSet.SingleFile(parts[^1], memory.ToArray(), title,
                parts[1].StartsWith("sdimg_", StringComparison.Ordinal) ? parts[1][6..] : parts[1]);
        }
    }

    public static ISaveFile Snapshot(string title, string directory = "FAITHDATAAUTO", string name = "game-save-data00", int occurrence = 0) =>
        Records(title).Where(file => file.Directory.Name == directory && file.FileName == name).Skip(occurrence).First();

    public static ISaveFile WithBytes(ISaveFile original, byte[] bytes) =>
        MemorySaveSet.SingleFile(original.FileName, bytes, original.Directory.TitleId, original.Directory.Name);
}
