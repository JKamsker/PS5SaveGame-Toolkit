using SaveToolkit.Sdk.Diff;
using SaveToolkit.Testing;
using SaveToolkit.Testing.Diff;

namespace SaveToolkit.Plugins.Witcher3.Tests;

public sealed class WitcherDecoderHookTests
{
    [Fact]
    public void ProductionPluginPublishesSdkOnlyHookAndResearchAdapterUsesTheSameValues()
    {
        var plugin = new Witcher3Plugin();
        var hook = Assert.Single(Assert.IsAssignableFrom<ISaveDecoderProvider>(plugin).Decoders);
        Assert.Equal("witcher3-native-identities", hook.Id);
        Assert.DoesNotContain(typeof(Witcher3Plugin).Assembly.GetReferencedAssemblies(),
            assembly => assembly.Name == "SaveToolkit.Testing");
        var bytes = SyntheticPlayerSave.Create();
        var file = MemorySaveSet.SingleFile("Manual.sav", bytes, "PPSA04018");
        Assert.True(hook.CanDecode(file));
        Assert.False(hook.CanDecode(MemorySaveSet.SingleFile("foreign.sav", bytes)));
        Assert.False(hook.CanDecode(MemorySaveSet.SingleFile("Manual.sav", [], "PPSA04018")));
        var limits = new DecodeLimits();
        var tree = new HookSaveDecoder(hook).Decode(file, limits);
        Assert.Equal(new WitcherSemanticDecoder().Decode(file, limits).Values, tree.Values);
        Assert.False(tree.Truncated);
        Assert.Empty(tree.Warnings);
        Assert.Contains(tree.Values, value => value.Path.EndsWith("/inventory/2/quantity", StringComparison.Ordinal) && value.Value == "5");
        Assert.Equal(bytes, file.ReadAllBytes());
    }

    [Fact]
    public void HookStopsImmediatelyWhenSinkDeclinesAndHonorsDepthAndInputBounds()
    {
        var hook = Assert.Single(new Witcher3Plugin().Decoders);
        var file = MemorySaveSet.SingleFile("Manual.sav", SyntheticPlayerSave.Create(), "PPSA04018");
        var sink = new DecliningSink();
        hook.Decode(file, sink);
        Assert.Equal(1, sink.Calls);
        var decoder = new HookSaveDecoder(hook);
        var limited = decoder.Decode(file, new DecodeLimits { MaxValues = 1 });
        Assert.True(limited.Truncated);
        Assert.Single(limited.Values);
        var shallow = decoder.Decode(file, new DecodeLimits { MaxDepth = 0 });
        Assert.True(shallow.Truncated);
        Assert.Equal("schema", Assert.Single(shallow.Values).Path);
        Assert.Contains(shallow.Warnings, warning => warning.Contains("depth limit", StringComparison.Ordinal));
        Assert.Throws<InvalidDataException>(() => hook.Decode(file, new DecliningSink { MaxFileBytes = file.Length - 1 }));
    }

    private sealed class DecliningSink : ISaveDecodeSink
    {
        public int MaxValues => 1;
        public int MaxValueLength => 256;
        public int MaxDepth => 64;
        public long MaxFileBytes { get; init; } = long.MaxValue;
        public int Calls { get; private set; }
        public bool Add(string path, string kind, string value, string? label = null) { Calls++; return false; }
        public void Warn(string message) => Assert.Fail(message);
        public void MarkTruncated(string reason) => Assert.Fail(reason);
    }
}
