using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json;
using SaveToolkit.Testing;
using SaveToolkit.Testing.Diff;

namespace SaveToolkit.SaveDiff.Tests;

public sealed class LargeSampleDiffTests(ITestOutputHelper output)
{
    [Fact]
    public void LargestAppendedSamplePairsHaveStableBoundedStreamingDiffs()
    {
        string? root = SampleSaves.ArtifactsDirectory;
        Assert.SkipWhen(root is null, "External samples unavailable.");
        var pairs = new List<(string Archive, int Before, int After, long Length)>();
        foreach (string archive in Directory.EnumerateFiles(root!, "*_saves_dec*.zip"))
        {
            using var zip = ZipFile.OpenRead(archive);
            pairs.AddRange(zip.Entries.Select((entry, index) => (entry, index))
                .Where(e => !e.entry.FullName.EndsWith('/'))
                .GroupBy(e => e.entry.FullName, StringComparer.Ordinal)
                .Where(group => group.Count() > 1)
                .Select(group => (archive, group.First().index, group.Skip(1).First().index,
                    Math.Max(group.First().entry.Length, group.Skip(1).First().entry.Length))));
        }
        Assert.SkipWhen(pairs.Count == 0, "No appended sample pairs available.");
        foreach (var pair in pairs.OrderByDescending(pair => pair.Length).Take(2))
        {
            using var zip = ZipFile.OpenRead(pair.Archive);
            var timer = Stopwatch.StartNew();
            long allocated = GC.GetAllocatedBytesForCurrentThread();
            var first = ByteDiffer.Compare(zip.Entries[pair.Before].Open, zip.Entries[pair.After].Open);
            long comparisonBytes = GC.GetAllocatedBytesForCurrentThread() - allocated;
            var second = ByteDiffer.Compare(zip.Entries[pair.Before].Open, zip.Entries[pair.After].Open);
            Assert.Equal(JsonSerializer.Serialize(first), JsonSerializer.Serialize(second));
            Assert.True(timer.Elapsed < TimeSpan.FromMinutes(2), "Streaming comparison exceeded two minutes.");
            Assert.InRange(comparisonBytes, 0, 16 * 1024 * 1024);
            output.WriteLine($"{Path.GetFileName(pair.Archive)}: {pair.Length} bytes, two scans {timer.ElapsedMilliseconds} ms, first scan {comparisonBytes} allocated bytes.");
        }
    }
}
