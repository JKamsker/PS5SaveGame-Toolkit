using System.Diagnostics;
using SaveToolkit.Core.References;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.Gearbox.Tests;

public sealed class AssetLoadMeasurements(ITestOutputHelper output)
{
    private const long FirstLoadAllocationLimit = 384 * 1024 * 1024;
    private const long RepeatedLoadAllocationLimit = 64 * 1024 * 1024;

    [Theory]
    [InlineData("PPSA01463")]
    [InlineData("PPSA01493")]
    public void LargestSample_FirstAndRepeatedLoad(string title)
    {
        var file = SampleSaves.Load(title).AllDiskFiles.MaxBy(f => f.Length)!;
        var plugin = new GearboxPlugin();
        var started = Stopwatch.GetTimestamp();
        var allocated = GC.GetAllocatedBytesForCurrentThread();
        var store = new ReferencePackStore([plugin]);
        Assert.Empty(store.LoadErrors);
        for (var iteration = 0; iteration < 2; iteration++)
        {
            var references = store.Pin(plugin, title);
            using var document = plugin.Open(file, references);
            _ = document.BuildView();
            var queryable = (IQueryableDocument)document;
            foreach (var collection in queryable.Collections)
                _ = queryable.Query(collection.Id, new() { Limit = 20 });
            var bytes = GC.GetAllocatedBytesForCurrentThread() - allocated;
            output.WriteLine($"{title} largest={file.Length} bytes iteration={iteration + 1}: " +
                $"{Stopwatch.GetElapsedTime(started).TotalMilliseconds:F3} ms; {bytes} thread allocated bytes");
            var limit = iteration == 0 ? FirstLoadAllocationLimit : RepeatedLoadAllocationLimit;
            Assert.True(bytes < limit, $"Load {iteration + 1} allocated {bytes:N0} bytes (budget {limit:N0}).");
            started = Stopwatch.GetTimestamp();
            allocated = GC.GetAllocatedBytesForCurrentThread();
        }
    }
}
