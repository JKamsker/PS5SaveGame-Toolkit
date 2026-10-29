namespace SaveToolkit.SaveDiff.Tests;

public sealed class BundledDecoderTests
{
    [Fact]
    public void Discover_RegistersEveryBundledPluginHookWithoutIdCollisions()
    {
        // The CLI discovers all bundled plugins; a hook id that equals a plugin id or another hook id
        // used to abort `savediff list-decoders` and every compare that relied on discovery.
        var resolver = DecoderResolver.Discover();

        var ids = resolver.Available.Select(d => d.Id).ToList();
        Assert.Equal(ids.Count, ids.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Contains(resolver.Available, d => d.Id == "ffxvi-evidence");
    }
}
