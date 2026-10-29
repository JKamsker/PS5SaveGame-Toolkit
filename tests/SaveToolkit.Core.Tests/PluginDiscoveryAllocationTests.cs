using SaveToolkit.Core.Plugins;
using SaveToolkit.Testing;

namespace SaveToolkit.Core.Tests;

[Collection(typeof(AllocationTestCollection))]
public sealed class PluginDiscoveryAllocationTests(ITestOutputHelper output)
{
    // Allows runner/framework assembly growth; the old repeated GetName path uses >2 MiB here.
    private const long DiscoveryBudget = 512 * 1024;

    [Fact]
    public void DiscoveryDoesNotRepeatedlyAllocateNamesForEveryPluginAssemblyPair()
    {
        var first = PluginCatalog.Discover();
        var allocated = AllocationProbe.Measure(() => PluginCatalog.Discover(), out var next);
        output.WriteLine($"{next.Plugins.Count} plugins; {allocated}");
        Assert.True(next.Plugins.Count >= 17);
        Assert.Equal(first.Plugins.Select(p => p.Info.Id).Order(), next.Plugins.Select(p => p.Info.Id).Order());
        foreach (var plugin in first.Plugins)
        {
            var replacement = next.Find(plugin.Info.Id.ToUpperInvariant());
            Assert.NotNull(replacement);
            Assert.Equal(plugin.GetType(), replacement.GetType());
            Assert.NotSame(plugin, replacement);
        }
        allocated.AssertAtMost(DiscoveryBudget, "Assembly names should be read once per discovery, not once per DLL comparison.");
    }
}
