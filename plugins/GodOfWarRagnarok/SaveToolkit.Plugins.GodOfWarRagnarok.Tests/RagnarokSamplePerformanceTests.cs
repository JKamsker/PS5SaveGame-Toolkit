using SaveToolkit.Sdk.Collections;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.GodOfWarRagnarok.Tests;

[Collection(typeof(AllocationTestCollection))]
public sealed class RagnarokSamplePerformanceTests
{
    [Fact]
    public void LargestSampleOpenAndFirstPinnedViewStayWithinAllocationBudget()
    {
        var file = LargestSample();
        var plugin = new GodOfWarRagnarokPlugin();
        var references = RagnarokTestSupport.PinnedReferences(plugin);
        using (var warmup = plugin.Open(file, references)) _ = warmup.BuildView();
        var allocated = AllocationProbe.MeasureOpen(() => plugin.Open(file, references), out var opened, document => _ = document.BuildView());
        using var document = opened;
        allocated.AssertInRange(0, 5_950_000);
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
        using var document = (RagnarokDocument)plugin.Open(LargestSample(), RagnarokTestSupport.PinnedReferences(plugin));
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
        var allocated = AllocationProbe.Measure(() => document.Query(collection, query), out var page);
        Assert.NotEmpty(page.Rows);
        allocated.AssertInRange(0, budget);
    }

    private static SaveToolkit.Sdk.Saves.ISaveFile LargestSample() => SampleSaves.Load("PPSA08330").AllDiskFiles
        .Where(file => !file.IsSystemFile && !file.Directory.IsBackup).OrderByDescending(file => file.Length).First();
}
