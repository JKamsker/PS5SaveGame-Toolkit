using SaveToolkit.Sdk.Diff;

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
        Assert.Equal(ids.Order(StringComparer.Ordinal), ids);
        Assert.Equal(resolver.Available, DecoderResolver.Discover().Available);

        // Pin the bundled inventory: comparing only with Discover().Plugins cannot detect a missing assembly.
        string[] expectedViews = ["astro-playroom", "atomic-heart", "bg3", "builtin.raw", "cyberpunk2077",
            "eldenring", "engine.unreal-gvas", "ff7r", "ffxvi", "format.json", "format.json-xor", "gearbox",
            "gow-ragnarok", "insomniac", "pragmata", "suckerpunch", "tales-of-arise", "witcher3"];
        Assert.Equal(expectedViews, resolver.Available.Where(d => d.Kind == "view").Select(d => d.Id));
        Assert.Equal(["auto", "json", "none"], resolver.Available.Where(d => d.Kind == "built-in").Select(d => d.Id));

        string[] expectedHooks = ["atomic-heart.saveextension-v1", "cyberpunk2077-evidence", "eldenring-evidence",
            "ff7r-character-evidence", "ffxvi-evidence", "gearbox-evidence", "genericjson.title-values-v1",
            "gow-ragnarok-evidence", "insomniac-evidence", "pragmata-canonical-v1", "suckerpunch.native-v1",
            "tales-of-arise.sections-v1", "unrealgvas.hogwarts.rows", "witcher3-native-identities", "xorjson.tokens"];
        Assert.Equal(expectedHooks, resolver.Available.Where(d => d.Kind == "hook").Select(d => d.Id));
        foreach (var plugin in resolver.Plugins)
        {
            Assert.Contains(resolver.Available, d => d.Id == plugin.Info.Id && d.Kind == "view" && d.PluginId == plugin.Info.Id);
            if (plugin is not ISaveDecoderProvider provider) continue;
            foreach (var hook in provider.Decoders)
            {
                Assert.Contains(resolver.Available, d => d.Id == hook.Id && d.Kind == "hook" && d.PluginId == plugin.Info.Id);
                Assert.Equal(hook.Id, resolver.Resolve(hook.Id)!.Id);
            }
        }
    }
}
