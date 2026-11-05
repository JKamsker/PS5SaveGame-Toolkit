using System.Diagnostics;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.Witcher3.Tests;

[Collection(typeof(AllocationTestCollection))]
public sealed class RelocationPerformanceTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(2048)]
    [InlineData(4096)]
    public void WideScalarBatchAvoidsPerNodePatchScans(int count)
    {
        var container = WitcherContainer.Parse(SyntheticSave.Create(valueCount: count));
        var directory = Sav3Directory.Parse(container);
        var tree = Sav3Tree.Parse(container.Payload, directory);
        var patches = tree.Nodes.Where(node => node.Tag == "AVAL")
            .Select(node => new Sav3Patch(node.ValueOffset, sizeof(uint), BitConverter.GetBytes(43U))).ToArray();
        Assert.Equal(count, patches.Length);
        var watch = Stopwatch.StartNew();
        var allocated = AllocationProbe.Measure(() => Sav3Writer.Write(container, directory, tree, patches), out var bytes);
        watch.Stop();
        allocated.AssertLessThan(container.Payload.Length * 6L + count * 32L,
            "Relocating a scalar batch must not allocate query iterators for each node and directory entry.");
        var reparsed = Sav3Tree.Parse(bytes, directory);
        Assert.Equal(count, reparsed.Nodes.Count(node => node.Tag == "AVAL"));
        foreach (var patch in patches) Assert.Equal(patch.Bytes, bytes.AsSpan(patch.Offset, patch.Length).ToArray());
        output.WriteLine($"{count} patches: {allocated.MinimumBytes} B; repeated measurement {watch.Elapsed.TotalMilliseconds:F1} ms.");
    }
}
