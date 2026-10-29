using SaveToolkit.Core.References;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.Gearbox.Tests;

[Collection(typeof(AllocationTestCollection))]
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
        ReferencePackStore store = null!;
        for (var iteration = 0; iteration < 2; iteration++)
        {
            var allocated = AllocationProbe.Measure(() =>
            {
                if (iteration == 0) store = new ReferencePackStore([plugin]);
                var references = store.Pin(plugin, title);
                using var document = plugin.Open(file, references);
                _ = document.BuildView();
                var queryable = (IQueryableDocument)document;
                foreach (var collection in queryable.Collections)
                    _ = queryable.Query(collection.Id, new() { Limit = 20 });
            });
            Assert.Empty(store.LoadErrors);
            output.WriteLine($"{title} largest={file.Length} bytes iteration={iteration + 1}: {allocated}.");
            var limit = iteration == 0 ? FirstLoadAllocationLimit : RepeatedLoadAllocationLimit;
            allocated.AssertLessThan(limit, $"Load {iteration + 1}.");
        }
    }
}
