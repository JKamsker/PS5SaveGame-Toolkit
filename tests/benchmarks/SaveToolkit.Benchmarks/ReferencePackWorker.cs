using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using SaveToolkit.Core.Plugins;
using SaveToolkit.Core.References;
using SaveToolkit.Sdk.References;

namespace SaveToolkit.Benchmarks;

/// <summary>Public-API content census; string payload counts are not estimates of total object size.</summary>
internal static class ReferencePackWorker
{
    private const int ExpectedPackCount = 40;

    public static void Run()
    {
        var catalog = PluginCatalog.Discover();
        var before = RetainedBytes();
        var construction = Measure(() => new ReferencePackStore(catalog.Plugins), out var store);
        var idleRetainedBytes = RetainedBytes() - before;
        if (store.Packs.Count != ExpectedPackCount || store.LoadErrors.Count != 0)
            throw new InvalidOperationException($"Expected all {ExpectedPackCount} shipped reference packs without errors.");

        var assets = DiscoverAssets(catalog.Plugins.Select(plugin => plugin.GetType().Assembly).Distinct());
        var firstAccess = new List<object>();
        foreach (var pack in store.Packs)
        {
            var plugin = catalog.Plugins.First(plugin => pack.Info.PluginIds.Contains(plugin.Info.Id, StringComparer.OrdinalIgnoreCase));
            var context = store.Pin(plugin, pack.Info.TitleIds.FirstOrDefault());
            var records = pack.Info.Datasets.Where(dataset => dataset.Kind == ReferenceDatasetKind.Records).ToArray();
            var firstCatalog = Measure(() => records.Length == 0 ? null :
                pack.Catalog(records[0].Id)?.Search(new ReferenceQuery { Limit = 1 }), out _);
            var remaining = Measure(() =>
            {
                foreach (var dataset in records.Skip(1))
                    if (pack.Catalog(dataset.Id)?.Search(new ReferenceQuery { Limit = 1 }) is null)
                        throw new InvalidOperationException("Missing records dataset.");
                long assetBytes = 0;
                foreach (var path in assets.GetValueOrDefault(pack.Id) ?? [])
                    assetBytes += (context.GetAsset(pack.Id + ":" + path)
                        ?? throw new InvalidOperationException("Missing embedded asset.")).Content.Length;
                return assetBytes;
            }, out var loadedAssetBytes);
            firstAccess.Add(new { pack.Id, firstCatalog, remaining, loadedAssetBytes });
        }
        var materializedRetainedBytes = RetainedBytes() - before;
        var strings = new StringCensus();
        long recordCount = 0, attributeEntries = 0;
        var locales = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (var pack in store.Packs)
        {
            long packRecords = 0;
            foreach (var dataset in pack.Info.Datasets.Where(dataset => dataset.Kind == ReferenceDatasetKind.Records))
            {
                var records = pack.Catalog(dataset.Id) ?? throw new InvalidOperationException("Missing catalog.");
                for (var offset = 0; offset < records.Count; offset += ReferenceQuery.MaxLimit)
                    foreach (var record in records.Search(new ReferenceQuery { Offset = offset, Limit = ReferenceQuery.MaxLimit }).Items)
                    {
                        packRecords++;
                        strings.Add(record.Key, record.Name, record.Category, record.Description, record.AssetId, record.Source);
                        attributeEntries += record.Attributes.Count;
                        foreach (var attribute in record.Attributes) strings.Add(attribute.Key, attribute.Value);
                    }
            }
            recordCount += packRecords;
            var locale = pack.Info.Locale ?? "<neutral>";
            locales[locale] = locales.GetValueOrDefault(locale) + packRecords;
        }
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            construction, idleRetainedBytes, materializedRetainedBytes, packCount = store.Packs.Count,
            datasetCount = store.Packs.Sum(pack => pack.Info.Datasets.Count), firstAccess,
            content = new
            {
                recordCount, attributeEntries, assetCount = assets.Values.Sum(paths => paths.Length),
                localeRecords = locales, strings = strings.Report(),
            },
        }));
        GC.KeepAlive(store);
    }

    private static Dictionary<string, string[]> DiscoverAssets(IEnumerable<Assembly> assemblies)
    {
        var result = new Dictionary<string, string[]>(StringComparer.Ordinal);
        foreach (var assembly in assemblies)
            foreach (var name in assembly.GetManifestResourceNames())
            {
                var path = name.Replace('\\', '/');
                if (!path.StartsWith("refpacks/", StringComparison.Ordinal) || !path.EndsWith("/pack.json", StringComparison.Ordinal))
                    continue;
                using var stream = assembly.GetManifestResourceStream(name)
                    ?? throw new InvalidOperationException("Missing manifest resource.");
                using var manifest = JsonDocument.Parse(stream);
                var root = manifest.RootElement;
                result.Add(root.GetProperty("id").GetString()!, root.GetProperty("datasets").EnumerateArray()
                    .Where(dataset => dataset.GetProperty("kind").GetString() == "assets")
                    .SelectMany(dataset => dataset.GetProperty("files").EnumerateArray())
                    .Select(file => file.GetProperty("path").GetString()!).ToArray());
            }
        return result;
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

    private sealed class StringCensus
    {
        private readonly HashSet<string> _instances = new(ReferenceEqualityComparer.Instance);
        private readonly HashSet<string> _values = new(StringComparer.Ordinal);
        private long _occurrences;

        public void Add(params string?[] values)
        {
            foreach (var value in values)
                if (value is not null)
                {
                    _occurrences++;
                    _instances.Add(value);
                    _values.Add(value);
                }
        }

        public object Report() => new
        {
            occurrences = _occurrences, distinctInstances = _instances.Count, distinctValues = _values.Count,
            instanceUtf16PayloadBytes = _instances.Sum(value => (long)value.Length * sizeof(char)),
            uniqueUtf16PayloadBytes = _values.Sum(value => (long)value.Length * sizeof(char)),
        };
    }
}
