using SaveToolkit.Core.Plugins;
using SaveToolkit.Plugins.Pragmata.Diff;
using SaveToolkit.Plugins.Pragmata.Format;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Diff;
using SaveToolkit.Testing;
using SaveToolkit.Testing.Diff;

namespace SaveToolkit.Plugins.Pragmata.Tests;

public sealed class DecoderHookTests
{
    [Fact]
    public void CataloguedProductionPluginPublishesTheCanonicalSdkOnlyHook()
    {
        var plugin = PluginCatalog.Discover().Find("pragmata");
        var provider = Assert.IsAssignableFrom<ISaveDecoderProvider>(plugin);
        var hook = Assert.Single(provider.Decoders);
        Assert.Equal("pragmata-canonical-v1", hook.Id);
        Assert.False(hook is ISaveDecoder);
        Assert.DoesNotContain(typeof(PragmataPlugin).Assembly.GetReferencedAssemblies(),
            assembly => assembly.Name == "SaveToolkit.Testing");
        var file = SampleEvidenceTests.Files().Single(file => file.FileName == "data000.bin");
        var original = file.ReadAllBytes();
        Assert.True(hook.CanDecode(file));
        var decoder = new HookSaveDecoder(hook);
        var first = decoder.Decode(file, new());
        Assert.Equal(first.Values, decoder.Decode(file, new()).Values);
        Assert.Equal(original, file.ReadAllBytes());
        Assert.NotEmpty(first.Values);
    }

    [Theory]
    [InlineData("PPSA00000", "data000.bin")]
    [InlineData("PPSA02530", "other.bin")]
    [InlineData("PPSA02530", "data000.bin")]
    public void HookDeclinesForeignOrMalformedHeaders(string title, string name)
    {
        var hook = Assert.Single(new PragmataPlugin().Decoders);
        Assert.False(hook.CanDecode(MemorySaveSet.SingleFile(name, new byte[16], title)));
    }

    [Fact]
    public void DirectHookChecksFileBudgetBeforeParsingAndStopsWhenSinkDeclines()
    {
        var hook = new PragmataDecoder();
        var sink = new DecliningSink();
        hook.Decode(MemorySaveSet.SingleFile("data000.bin", [1]), sink);
        Assert.Equal(0, sink.Calls);
        Assert.Contains("byte limit", Assert.Single(sink.Truncations), StringComparison.Ordinal);

        var root = new SaveNode(1, FieldTags.Class)
        {
            Children = [new SaveNode(2, FieldTags.Class), new SaveNode(3, FieldTags.Class)],
        };
        hook.Decode([], [root, root], sink);
        Assert.Equal(1, sink.Calls);
    }

    [Fact]
    public void HookRejectsCorruptionRatherThanReturningPlausibleEvidence()
    {
        var bytes = SampleEvidenceTests.Files().First().ReadAllBytes();
        bytes[^1] ^= 1;
        var file = MemorySaveSet.SingleFile("data000.bin", bytes, "PPSA02530");
        var hook = Assert.Single(new PragmataPlugin().Decoders);
        Assert.Throws<SaveFormatException>(() => new HookSaveDecoder(hook).Decode(file, new()));
    }

    private sealed class DecliningSink : ISaveDecodeSink
    {
        public int MaxValues => 0;
        public int MaxValueLength => 256;
        public int MaxDepth => 100;
        public long MaxFileBytes => 0;
        public int Calls { get; private set; }
        public List<string> Truncations { get; } = [];
        public bool Add(string path, string kind, string value, string? label = null)
        {
            Assert.Equal(0, Calls++);
            return false;
        }
        public void Warn(string message) => Assert.Fail(message);
        public void MarkTruncated(string reason) => Truncations.Add(reason);
    }
}
