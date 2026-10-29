using SaveToolkit.Sdk.Diff;
using SaveToolkit.Testing;
using SaveToolkit.Testing.Diff;

namespace SaveToolkit.Plugins.EldenRing.Tests;

public sealed class DecoderHookTests
{
    [Fact]
    public void ProviderPublishesPrivacyFilteredHookAndHonorsHostBudgets()
    {
        ISaveDecoderProvider provider = new EldenRingPlugin();
        var hook = Assert.Single(provider.Decoders);
        Assert.Equal("eldenring-evidence", hook.Id);
        var fixture = new SemanticFixture();
        fixture.Row("held", "common", 0, 0xB00050F0, 12, 77);
        var file = fixture.Save.File();
        Assert.True(hook.CanDecode(file));
        Assert.False(hook.CanDecode(fixture.Save.File("PPSA00000")));
        Assert.False(hook.CanDecode(MemorySaveSet.SingleFile("other.dat", [0])));
        var decoder = new HookSaveDecoder(hook);
        var tree = decoder.Decode(file, new());
        Assert.False(tree.Truncated);
        Assert.Empty(tree.Warnings);
        Assert.Equal(8, tree.Values.Count);
        Assert.Contains(tree.Values, value => value.Path.EndsWith("/quantity", StringComparison.Ordinal) && value.Value == "12");
        Assert.DoesNotContain(tree.Values, value => value.Value.Contains("Hero", StringComparison.Ordinal));
        Assert.All(tree.Values, value => Assert.DoesNotContain("name", value.Path));
        foreach (var limit in new[] { 0, 1, 2 })
        {
            var bounded = decoder.Decode(file, new() { MaxValues = limit });
            Assert.True(bounded.Truncated);
            Assert.Equal(limit, bounded.Values.Count);
        }
        Assert.Throws<InvalidDataException>(() => decoder.Decode(file, new() { MaxFileBytes = file.Length - 1 }));
        var original = file.ReadAllBytes();
        hook.Decode(file, new RejectingSink()); // Must stop immediately when the host refuses output.
        Assert.True(original.AsSpan().SequenceEqual(file.ReadAllBytes()));
    }

    [Fact]
    public void FrameworkViewDecoderVisitsInventoryBeyondFirstPageAndReportsCaps()
    {
        const int rows = 203;
        var fixture = new SemanticFixture();
        for (var index = 0; index < rows; index++)
            fixture.Row("held", "common", index, 0xB00050F0, (uint)(index + 1), (uint)index);
        var decoder = new PluginViewDecoder(new EldenRingPlugin());
        var tree = decoder.Decode(fixture.Save.File(), new() { QueryPageSize = 17 });
        Assert.False(tree.Truncated, string.Join("; ", tree.Warnings));
        var quantities = tree.Values.Where(value => value.Path.EndsWith("/quantity", StringComparison.Ordinal)).ToArray();
        const int physicalSlots = SemanticLayout.HeldCommonCapacity + SemanticLayout.HeldKeyCapacity
            + SemanticLayout.StorageCommonCapacity + SemanticLayout.StorageKeyCapacity;
        Assert.Equal(physicalSlots, quantities.Length); // SaveDiff explicitly includes hidden slots.
        Assert.Equal(physicalSlots - rows, quantities.Count(value => value.Value == "0"));
        Assert.Contains(quantities, value => value.Value == "203");
        Assert.Equal(tree.Values, decoder.Decode(fixture.Save.File(), new() { QueryPageSize = 17 }).Values);
        var bounded = decoder.Decode(fixture.Save.File(), new() { MaxQueryRows = 1, QueryPageSize = 17 });
        Assert.True(bounded.Truncated);
        Assert.NotEmpty(bounded.Warnings);
    }

    private sealed class RejectingSink : ISaveDecodeSink
    {
        public int MaxValues => 0;
        public int MaxValueLength => 256;
        public int MaxDepth => 64;
        public long MaxFileBytes => long.MaxValue;
        private int _calls;
        public bool Add(string path, string kind, string value, string? label = null)
        {
            Assert.Equal(1, ++_calls);
            return false;
        }
        public void Warn(string message) => Assert.Fail(message);
        public void MarkTruncated(string reason) => Assert.Fail(reason);
    }
}
