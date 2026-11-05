using SaveToolkit.Core.Sessions;
using SaveToolkit.Core.Storage;
using SaveToolkit.Core.Tests.Fakes;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Core.Tests;

public sealed class LazyExpansionComplexityTests(ITestOutputHelper output)
{
    [Fact]
    public void BindingBearingExpansionsDoNotCopyPreviouslyPublishedFields()
    {
        const int small = 1_000;
        _ = Measure(10); // Warm the publication path before measuring allocations.
        var smaller = Measure(small);
        var larger = Measure(small * 2);
        output.WriteLine($"Expansion allocations: {small} = {smaller}; {small * 2} = {larger} bytes.");
        // Persistent index updates can grow logarithmically. Repeated full copies grow quadratically.
        Assert.True(larger < smaller * 3, $"Doubling expansions allocated {smaller} -> {larger} bytes.");
    }

    private static long Measure(int count)
    {
        using var document = new LazyFields(count);
        var set = SaveSetImporter.Import(
            [new ImportSource("save.dat", () => new MemoryStream(CounterPlugin.Create(1)))], TestZips.TempDir());
        using var session = new DocumentSession("complexity", set.AllDiskFiles.Single(), new CounterPlugin(), document);
        session.BuildView();
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < count; i++) Assert.Single(session.Expand($"lazy/{i}"));
        return GC.GetAllocatedBytesForCurrentThread() - before;
    }

    private sealed class LazyFields(int count) : BoundSaveDocument
    {
        public override string Title => "Synthetic lazy fields";
        public override ViewNode BuildView() => new SectionNode(Title,
            Enumerable.Range(0, count).Select(i => (ViewNode)SectionNode.Lazy("Field", $"lazy/{i}")).ToArray());
        public override IReadOnlyList<ViewNode> ExpandNode(string lazyId) =>
            [new FieldNode(lazyId + "/value", "Value", IntegerSpec.UInt8, FieldValue.Of(1L))];
        public override IReadOnlyList<FileOutput> Serialize() => [];
    }
}
