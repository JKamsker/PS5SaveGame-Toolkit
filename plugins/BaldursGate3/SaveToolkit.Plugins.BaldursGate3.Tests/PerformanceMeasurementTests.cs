using System.Diagnostics;
using System.Text.Json;
using SaveToolkit.Sdk.Collections;

namespace SaveToolkit.Plugins.BaldursGate3.Tests;

[Collection(PerformanceTestCollection.Name)]
public sealed class PerformanceMeasurementTests
{
    private static readonly JsonSerializerOptions OutputOptions = new() { WriteIndented = true };

    [Fact]
    public void MeasureLargestSamplePhases()
    {
        var file = SampleLiveNewAgeTests.Save();
        var measurements = new List<Measurement>();
        // Run this class alone in Release for comparisons. Import and fixture preparation
        // are outside the measurements; retain the cold pass separately from five warm passes.
        for (var iteration = 0; iteration < 6; iteration++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            var plugin = new BaldursGate3Plugin();
            using var document = Measure("open", iteration, () =>
                Assert.IsType<BaldursGate3Document>(plugin.Open(file)));
            Measure("root-view", iteration, document.BuildView);
            var collections = Measure("collections", iteration, () => document.Collections);
            Measure("first-pages", iteration, () =>
            {
                foreach (var collection in collections)
                    document.Query(collection.Id, new QueryableCollectionQuery { Limit = 10 });
                return true;
            });
            var values = collections.First(c => c.Id.EndsWith("/values", StringComparison.Ordinal));
            Measure("twenty-value-pages", iteration, () =>
            {
                for (var page = 0; page < 20; page++)
                    document.Query(values.Id, new QueryableCollectionQuery { Offset = page * 10, Limit = 10 });
                return true;
            });
            Measure("story-first-expansion", iteration, () => document.ExpandNode("story/databases"));
            var package = LspkPackage.Read(file.ReadAllBytes());
            var globals = package.Extract(package.Entries.Single(e => e.Name == "Globals.lsf"));
            Measure("lsf-parse", iteration, () => LsfReader.Read(globals));
            var resource = LsfReader.Read(globals);
            var newAge = resource.Attributes.Single(a => a.Name == "NewAge").Data;
            var model = LsmfReader.Read(newAge);
            Measure("live-decode", iteration, () => new LsmfLiveSource(0, 0, newAge, resource));
            Measure("twenty-entity-reads", iteration, () =>
            {
                for (var read = 0; read < 20; read++) _ = model.Entities();
                return true;
            });
            var access = new LsmfAccess(model);
            _ = access.Owners();
            Measure("thousand-owner-lookups", iteration, () =>
            {
                for (var row = 0; row < 1000; row++)
                    _ = access.RowsForEntity("game.stats.v3.StatsComponent", 36, row);
                return true;
            });
        }
        if (Environment.GetEnvironmentVariable("BG3_PERF_OUTPUT") is { Length: > 0 } path)
            File.WriteAllText(path, JsonSerializer.Serialize(new { sampleBytes = file.Length, measurements },
                OutputOptions));

        T Measure<T>(string phase, int iteration, Func<T> operation)
        {
            var allocated = GC.GetAllocatedBytesForCurrentThread();
            var timer = Stopwatch.StartNew();
            var result = operation();
            timer.Stop();
            measurements.Add(new(phase, iteration, timer.Elapsed.TotalMilliseconds,
                GC.GetAllocatedBytesForCurrentThread() - allocated));
            return result;
        }
    }

    private sealed record Measurement(string Phase, int Iteration, double Milliseconds, long AllocatedBytes);
}
