using SaveToolkit.Core.References;
using SaveToolkit.Sdk.Views;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.Pragmata.Tests;

[Collection(typeof(AllocationTestCollection))]
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
        var allocated = AllocationProbe.MeasureOpen(() => new PragmataDocument(file.RelativePath, bytes, references), out var opened, document => _ = document.BuildView());
        using var document = opened;
        var view = document.BuildView();
        Assert.IsType<TabsNode>(view);
        Assert.NotNull(document.Layout.Inventory);
        allocated.AssertLessThan(45_000_000, $"Largest sample {file.FileName}: open + first view allocated {allocated.MinimumBytes:N0} bytes.");
    }
}
