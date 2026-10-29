using SaveToolkit.Core.References;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.GodOfWarRagnarok.Tests;

public sealed class RagnarokSamplePerformanceTests
{
    [Fact]
    public void LargestSampleOpenAndFirstPinnedViewStayWithinAllocationBudget()
    {
        var file = LargestSample();
        var plugin = new GodOfWarRagnarokPlugin();
        var references = new ReferencePackStore([plugin]).Pin(plugin, "PPSA08330");
        using (var warmup = plugin.Open(file, references)) _ = warmup.BuildView();
        var before = GC.GetAllocatedBytesForCurrentThread();
        using var document = plugin.Open(file, references);
        _ = document.BuildView();
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.InRange(allocated, 0, 5_950_000);
        Assert.Equal(3_217_731, file.Length);
        Assert.Equal(file.ReadAllBytes(), Assert.Single(document.Serialize()).Content.ToArray());
    }

    [Theory]
    [InlineData("page", 128_000)]
    [InlineData("search", 32_000)]
    [InlineData("sort", 128_000)]
    [InlineData("directory", 56_000)]
    public void LargestSamplePagesStayWithinAllocationBudgets(string kind, int budget)
    {
        var plugin = new GodOfWarRagnarokPlugin();
        using var document = (RagnarokDocument)plugin.Open(LargestSample(), new ReferencePackStore([plugin]).Pin(plugin, "PPSA08330"));
        _ = document.BuildView();
        var collection = kind == "directory" ? "directory" : "resources";
        var query = kind switch
        {
            "search" => new QueryableCollectionQuery { Text = "  Hacksilver  ", Limit = 20 },
            "sort" => new() { SortColumn = "quantity", Descending = true, Offset = 200, Limit = 20 },
            "directory" => new() { SortColumn = "word", Offset = 600, Limit = 20 },
            _ => new() { Offset = 200, Limit = 20 },
        };
        _ = document.Query(collection, query);
        var before = GC.GetAllocatedBytesForCurrentThread();
        var page = document.Query(collection, query);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.NotEmpty(page.Rows);
        Assert.InRange(allocated, 0, budget);
    }

    private static SaveToolkit.Sdk.Saves.ISaveFile LargestSample() => SampleSaves.Load("PPSA08330").AllDiskFiles
        .Where(file => !file.IsSystemFile && !file.Directory.IsBackup).OrderByDescending(file => file.Length).First();
}
