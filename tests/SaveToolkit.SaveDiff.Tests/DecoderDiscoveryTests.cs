using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Diff;
using SaveToolkit.Sdk.Saves;
using SaveToolkit.Testing;

namespace SaveToolkit.SaveDiff.Tests;

public sealed class DecoderDiscoveryTests
{
    [Fact]
    public void RegistrationOrder_DoesNotChangeTheListOrAutoProbeTieBreak()
    {
        IGamePlugin[] plugins = [new NamedPlugin("test.z", [new Hook("hook.z"), new Hook("hook.a")]), new NamedPlugin("test.a", [])];
        var first = new DecoderResolver(plugins);
        var second = new DecoderResolver(plugins.Reverse().ToArray());
        var file = MemorySaveSet.SingleFile("inv.dat", InventoryPlugin.Create((1, 1)));

        Assert.Equal(["auto", "hook.a", "hook.z", "json", "none", "test.a", "test.z"], first.Available.Select(d => d.Id));
        Assert.Equal(first.Available, second.Available);
        Assert.Equal("test.a", first.Resolve("auto")!.Decode(file, new()).Decoder);
        Assert.Equal("test.a", second.Resolve("auto")!.Decode(file, new()).Decoder);
        foreach (var description in first.Available.Where(d => d.Id != "none"))
            Assert.Same(first.Resolve(description.Id), first.Resolve(description.Id.ToUpperInvariant()));
        Assert.Null(first.Resolve("NONE"));
    }

    [Fact]
    public void NoPlugins_StillRegistersEveryBuiltInMode()
    {
        var resolver = new DecoderResolver([]);

        Assert.Equal(["auto", "json", "none"], resolver.Available.Select(d => d.Id));
        Assert.NotNull(resolver.Resolve("json"));
        Assert.NotNull(resolver.Resolve("auto"));
        Assert.Null(resolver.Resolve(null));
        Assert.Null(resolver.Resolve(""));
        Assert.Null(resolver.Resolve("none"));
    }

    [Fact]
    public void DuplicatePluginIdsAndCrossPluginHookIds_AreRejectedCaseInsensitively()
    {
        var pluginCollision = Assert.Throws<UsageException>(() => new DecoderResolver([new NamedPlugin("test.a", []), new NamedPlugin("TEST.A", [])]));
        var hookCollision = Assert.Throws<UsageException>(() => new DecoderResolver([
            new NamedPlugin("test.a", [new Hook("hook.a")]), new NamedPlugin("test.b", [new Hook("HOOK.A")])]));

        Assert.Contains("Duplicate decoder id", pluginCollision.Message, StringComparison.Ordinal);
        Assert.Contains("Duplicate decoder id", hookCollision.Message, StringComparison.Ordinal);
        Assert.Contains("test.b", hookCollision.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("é")]
    [InlineData("name\n")]
    [InlineData("with:colon")]
    public void InvalidHookCharacters_AreRejected(string id)
    {
        Assert.Throws<UsageException>(() => new DecoderResolver([new NamedPlugin("test.a", [new Hook(id)])]));
    }

    [Fact]
    public void HookIdLength_Exactly128IsAccepted_129IsRejected()
    {
        var id = new string('a', 128);
        Assert.NotNull(new DecoderResolver([new NamedPlugin("test.a", [new Hook(id)])]).Resolve(id));
        Assert.Throws<UsageException>(() => new DecoderResolver([new NamedPlugin("test.a", [new Hook(id + "a")])]));
    }

    private sealed class NamedPlugin(string id, IReadOnlyList<ISaveDecoderHook> hooks) : IGamePlugin, ISaveDecoderProvider
    {
        private readonly InventoryPlugin _plugin = new();
        public PluginInfo Info { get; } = new(id, id, "1.0.0");
        public IReadOnlyList<ISaveDecoderHook> Decoders => hooks;
        public ProbeResult Probe(ISaveFile file) => _plugin.Probe(file);
        public ISaveDocument Open(ISaveFile file) => _plugin.Open(file);
    }

    private sealed class Hook(string id) : ISaveDecoderHook
    {
        public string Id => id;
        public bool CanDecode(ISaveFile file) => false;
        public void Decode(ISaveFile file, ISaveDecodeSink sink) => throw new NotSupportedException();
    }
}
