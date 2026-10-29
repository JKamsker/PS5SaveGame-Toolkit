using SaveToolkit.Core.References;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.Pragmata.Tests;

public sealed class SamplePerformanceTests
{
    [Fact]
    public void LargestSampleOpenAndFirstViewStayWithinAllocationBudget()
    {
        var plugin = new PragmataPlugin();
        var references = new ReferencePackStore([plugin]).Pin(plugin, "PPSA02530");
        var file = SampleEvidenceTests.Files().MaxBy(file => file.Length)!;
        var bytes = file.ReadAllBytes();
        // Exclude shared resource loading, sample extraction and pinned pack construction.
        using (var warm = new PragmataDocument(file.RelativePath, bytes, references)) _ = warm.BuildView();
        var start = GC.GetAllocatedBytesForCurrentThread();
        using var document = new PragmataDocument(file.RelativePath, bytes, references);
        var view = document.BuildView();
        var allocated = GC.GetAllocatedBytesForCurrentThread() - start;
        Assert.IsType<TabsNode>(view);
        Assert.NotNull(document.Layout.Inventory);
        Assert.True(allocated < 45_000_000, $"Largest sample {file.FileName}: open + first view allocated {allocated:N0} bytes.");
    }
}
