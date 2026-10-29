using System.Diagnostics;
using System.Text.Json;
using SaveToolkit.Core.Plugins;
using SaveToolkit.Core.References;
using SaveToolkit.Core.Storage;

namespace SaveToolkit.Benchmarks;

/// <summary>One fresh process per observation; no warmed plugin or decoder state crosses runs.</summary>
internal static class StartupWorker
{
    public static void Run(string scratch)
    {
        var catalogMeasurement = Measure(() => PluginCatalog.Discover(), out var catalog);
        var data = new SyntheticData(DataSize.Small, SaveFormat.GenericJson);
        var root = Path.Combine(Path.GetFullPath(scratch), "worker-" + Guid.NewGuid().ToString("N"));
        try
        {
            var set = SaveSetImporter.Import(
                [new ImportSource("synthetic.zip", () => new MemoryStream(data.ZipBytes, writable: false))], root);
            var file = set.AllDiskFiles.First();
            var probes = catalog.Plugins.Select(plugin =>
            {
                var measurement = Measure(() => plugin.Probe(file), out var result);
                return new { plugin.Info.Id, measurement, result.IsMatch, result.Score };
            }).ToArray();
            var heapBefore = RetainedBytes();
            var packsMeasurement = Measure(() => new ReferencePackStore(catalog.Plugins), out var packs);
            var retainedPackBytes = RetainedBytes() - heapBefore;
            if (packs.Packs.Count == 0 || packs.LoadErrors.Count != 0)
                throw new InvalidOperationException("Embedded pack loading was incomplete or rejected a pack.");
            var warmDiscovery = Measure(() => PluginCatalog.Discover(), out var warmCatalog);
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                catalogMeasurement, pluginCount = catalog.Plugins.Count, probes, packsMeasurement,
                packCount = packs.Packs.Count, errors = packs.LoadErrors, retainedPackBytes, warmDiscovery,
                datasets = packs.Packs.Sum(pack => pack.Info.Datasets.Count),
                items = packs.Packs.Sum(pack => pack.Info.Datasets.Sum(dataset => (long)dataset.ItemCount)),
            }));
            GC.KeepAlive(packs);
            GC.KeepAlive(warmCatalog);
        }
        finally
        {
            Cleanup(root, scratch);
        }
    }

    private static void Cleanup(string root, string scratch)
    {
        if (Path.GetDirectoryName(root) != Path.GetFullPath(scratch))
            throw new InvalidOperationException("Worker directory escaped scratch.");
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }

    private static Measurement Measure<T>(Func<T> operation, out T result)
    {
        var before = GC.GetAllocatedBytesForCurrentThread();
        var started = Stopwatch.GetTimestamp();
        result = operation();
        return new(Stopwatch.GetElapsedTime(started).TotalMilliseconds,
            GC.GetAllocatedBytesForCurrentThread() - before);
    }

    private static long RetainedBytes() => GC.GetTotalMemory(forceFullCollection: true);

    private sealed record Measurement(double Milliseconds, long AllocatedBytes);
}
