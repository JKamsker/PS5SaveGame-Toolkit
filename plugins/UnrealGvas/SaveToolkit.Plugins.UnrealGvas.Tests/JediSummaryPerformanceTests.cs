using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Views;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.UnrealGvas.Tests;

[Collection(typeof(AllocationTestCollection))]
public sealed class JediSummaryPerformanceTests
{
    [Fact]
    public void LargestActorSummaryDoesNotRebuildTheCompletePropertyIndex()
    {
        var plugin = new UnrealGvasPlugin();
        var sample = SampleSaves.Load("PPSA02199").AllDiskFiles.Where(file => plugin.Probe(file).IsMatch)
            .OrderByDescending(file => file.Length).ThenBy(file => file.RelativePath, StringComparer.Ordinal).First();
        using var document = plugin.Open(sample);
        document.BuildView();
        var first = Assert.Single(document.ExpandNode("jedi/actors").OfType<TableNode>());
        var allocated = AllocationProbe.Measure(() => Assert.Single(document.ExpandNode("jedi/actors").OfType<TableNode>()), out var repeated);
        const int summaryBudget = 512 * 1024;
        allocated.AssertLessThan(summaryBudget, $"Allocated {allocated.MinimumBytes:N0} bytes for a 100-row summary.");
        Assert.Equal(100, repeated.Rows.Count);
        Assert.Equal("actors/0", repeated.Rows[0].Key);
        Assert.Equal(first.Rows.Select(row => row.Key), repeated.Rows.Select(row => row.Key));
        long total = Assert.IsAssignableFrom<IQueryableDocument>(document).Collections.Single(collection => collection.Id == "jedi/actor-tags").TotalCount;
        Assert.Contains($"of {total} indexed nodes", repeated.Description);
        Assert.Equal(sample.ReadAllBytes(), Assert.Single(document.Serialize()).Content.ToArray());
    }
}
