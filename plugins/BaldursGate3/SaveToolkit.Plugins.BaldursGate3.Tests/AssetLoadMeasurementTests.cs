using System.Diagnostics;
using System.Text.Json;
using SaveToolkit.Core.References;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.References;

namespace SaveToolkit.Plugins.BaldursGate3.Tests;

public sealed class AssetLoadMeasurementTests
{
    [Fact]
    public void MeasureFirstReferenceLoadAndLargestSampleView()
    {
        var file = SampleLiveNewAgeTests.Save(); // Sample import is outside the measured operation.
        GC.Collect();
        GC.WaitForPendingFinalizers();
        var allocated = GC.GetTotalAllocatedBytes(precise: true);
        var timer = Stopwatch.StartNew();
        var plugin = new BaldursGate3Plugin();
        var store = new ReferencePackStore([plugin]);
        Assert.Empty(store.LoadErrors);
        var context = store.Pin(plugin, "PPSA18463");
        var packMilliseconds = timer.Elapsed.TotalMilliseconds;
        using var document = Assert.IsType<BaldursGate3Document>(plugin.Open(file, context));
        _ = document.BuildView();
        foreach (var collection in document.Collections)
            _ = document.Query(collection.Id, new QueryableCollectionQuery { Limit = 10 });
        foreach (var id in Bg3References.CatalogIds)
            foreach (var record in context.Catalog(id)!.Search(new ReferenceQuery()).Items)
                if (record.AssetId is { } asset)
                    Assert.NotNull(context.GetAsset(asset));
        timer.Stop();
        var measurement = new
        {
            sampleBytes = file.Length,
            packMilliseconds,
            firstLoadMilliseconds = timer.Elapsed.TotalMilliseconds,
            allocatedBytes = GC.GetTotalAllocatedBytes(precise: true) - allocated,
        };
        // Optional one-shot measurement artifact, never part of an exported save or committed sample.
        if (Environment.GetEnvironmentVariable("BG3_ASSET_BENCHMARK_OUTPUT") is { Length: > 0 } path)
            File.WriteAllText(path, JsonSerializer.Serialize(measurement));
    }
}
