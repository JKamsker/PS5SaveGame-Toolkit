using SaveToolkit.Plugins.GenericJson;
using SaveToolkit.Plugins.XorJson;
using SaveToolkit.Sdk;
using SaveToolkit.Testing;

namespace SaveToolkit.Core.Tests;

public sealed class MutationFuzzSyntheticSeedTests
{
    [Fact]
    public void BuiltInSeeds_AreValidAndOpenThroughTheIntendedPlugin()
    {
        Verify(new GenericJsonPlugin(), expectedCount: 1);
        Verify(new XorJsonPlugin(), expectedCount: 2);
        Assert.Empty(MutationFuzzSyntheticSeeds.For("unsupported.plugin"));
    }

    private static void Verify(IGamePlugin plugin, int expectedCount)
    {
        var seeds = MutationFuzzSyntheticSeeds.For(plugin.Info.Id).ToArray();
        Assert.Equal(expectedCount, seeds.Length);
        foreach (var seed in seeds)
        {
            Assert.True(plugin.Probe(seed).IsMatch);
            using var document = plugin.Open(seed);
            Assert.NotNull(document.BuildView());
        }
    }
}
