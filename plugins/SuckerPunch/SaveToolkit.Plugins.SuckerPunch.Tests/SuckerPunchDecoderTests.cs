using System.Globalization;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Diff;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Testing;
using SaveToolkit.Testing.Diff;
using static SaveToolkit.Plugins.SuckerPunch.Tests.SuckerPunchTestData;

namespace SaveToolkit.Plugins.SuckerPunch.Tests;

public sealed class SuckerPunchDecoderTests
{
    private static ISaveDecoderHook Hook => Assert.Single(((ISaveDecoderProvider)new SuckerPunchPlugin()).Decoders);

    [Theory]
    [InlineData(TsushimaTitle, 25)]
    [InlineData("PPSA26344", 8)]
    public void NativeHookReadsEverySampleWithoutChangingBytes(string title, int expectedFiles)
    {
        var plugin = new SuckerPunchPlugin();
        var decoder = new HookSaveDecoder(Hook);
        Assert.Equal("suckerpunch.native-v1", decoder.Id);
        var files = SampleSaves.Load(title).AllDiskFiles.Where(file => plugin.Probe(file).IsMatch).ToArray();
        Assert.Equal(expectedFiles, files.Length);
        foreach (var file in files)
        {
            var original = file.ReadAllBytes();
            Assert.True(decoder.CanDecode(file));
            var tree = decoder.Decode(file, new DecodeLimits());
            Assert.False(tree.Truncated);
            Assert.Equal(tree.Values.Count, tree.Values.Select(value => value.Path).Distinct().Count());
            Assert.Equal(Word(original, 12).ToString(CultureInfo.InvariantCulture),
                tree.Values.Single(value => value.Path == "container/word3").Value);
            if (title == TsushimaTitle)
            {
                Assert.Equal(48, tree.Values.Count(value => value.Path.EndsWith("/quantity", StringComparison.Ordinal)));
                using var document = Open(file);
                foreach (var value in tree.Values.Where(value => value.Path.StartsWith("inventory/", StringComparison.Ordinal)))
                {
                    Assert.Equal(document.GetValue(value.Path).AsInteger().ToString(CultureInfo.InvariantCulture), value.Value);
                }
            }
            else
            {
                Assert.Equal(4, tree.Values.Count);
                Assert.Contains(tree.Warnings, warning => warning.Contains("unknown", StringComparison.Ordinal));
            }

            Assert.Equal(tree.Values, decoder.Decode(file, new DecodeLimits()).Values);
            Assert.Equal(original, file.ReadAllBytes());
        }
    }

    [Theory]
    [InlineData(1, true)]
    [InlineData(2, true)]
    [InlineData(3, false)]
    public void NativeHookReportsIncompleteDepth(int depth, bool truncated)
    {
        var tree = new HookSaveDecoder(Hook).Decode(Slot("suckerpunchUser1"), new DecodeLimits { MaxDepth = depth });
        Assert.Equal(truncated, tree.Truncated);
        if (truncated) Assert.Contains(tree.Warnings, warning => warning.Contains("depth", StringComparison.Ordinal));
    }

    [Fact]
    public void NativeHookStopsImmediatelyWhenSinkDeclinesAndEnforcesFileLimit()
    {
        var file = Slot("suckerpunchUser1");
        var sink = new RejectingSink();
        Hook.Decode(file, sink);
        Assert.Equal(1, sink.AddCalls);
        Assert.Throws<SaveFormatException>(() => Hook.Decode(file, new RejectingSink { MaxFileBytes = 1 }));
        var decoder = new HookSaveDecoder(Hook);
        var tree = decoder.Decode(file, new DecodeLimits { MaxValues = 8, MaxValueLength = 2 });
        Assert.True(tree.Truncated);
        Assert.Equal(8, tree.Values.Count);
        Assert.Throws<InvalidDataException>(() => decoder.Decode(file, new DecodeLimits { MaxFileBytes = 1 }));
    }

    [Fact]
    public void NativeHookRejectsForeignAndCorruptContainers()
    {
        var foreign = WithBytes(new byte[16], "PPSA00000");
        Assert.False(Hook.CanDecode(foreign));
        Assert.Throws<SaveFormatException>(() => new HookSaveDecoder(Hook).Decode(foreign, new DecodeLimits()));
        var corrupt = Slot("suckerpunchUser1").ReadAllBytes();
        corrupt[8] ^= 1;
        Assert.Throws<SaveFormatException>(() => new HookSaveDecoder(Hook).Decode(WithBytes(corrupt), new DecodeLimits()));
    }

    [Fact]
    public void F12ViewDecoderTraversesAllEquipmentRowsAndReportsQueryCaps()
    {
        var file = Slot("suckerpunchUser1");
        var decoder = new PluginViewDecoder(new SuckerPunchPlugin());
        var tree = decoder.Decode(file, new DecodeLimits { QueryPageSize = 7 });
        Assert.False(tree.Truncated);
        using var document = Open(file);
        var count = Assert.Single(Assert.IsAssignableFrom<IQueryableDocument>(document).Collections).TotalCount;
        Assert.Equal(count, tree.Values.Count(value => value.Path.StartsWith("suckerpunch_equipment/", StringComparison.Ordinal)));
        Assert.Contains(tree.Values, value => value.Path.StartsWith("collections/equipment~1identifiers/rows/", StringComparison.Ordinal));
        var limited = decoder.Decode(file, new DecodeLimits { MaxQueryRows = 1, QueryPageSize = 1 });
        Assert.True(limited.Truncated);
        Assert.NotEmpty(limited.Warnings);
    }

    [Fact]
    public void NativeHookReportsExactEditedTimerAmmoAndChecksumPaths()
    {
        var file = Slot("suckerpunchUser1");
        var decoder = new HookSaveDecoder(Hook);
        var before = decoder.Decode(file, new DecodeLimits());
        using var document = Open(file);
        Assert.True(document.ApplyEdits([new(ArrowId, FieldValue.Of(17L)),
            new("progress/play-seconds", FieldValue.Of(123.25))]).Succeeded);
        var bytes = Assert.Single(document.Serialize()).Content.ToArray();
        var after = decoder.Decode(WithBytes(bytes), new DecodeLimits());
        var changed = before.Values.Zip(after.Values).Where(pair => pair.First.Value != pair.Second.Value)
            .Select(pair => pair.First.Path).Order(StringComparer.Ordinal).ToArray();
        Assert.Equal<string>(["container/word2", "inventory/ArrowNormal/quantity", "inventory/ArrowNormal/quantity-copy",
            "progress/play-seconds"], changed);
        Assert.Equal("123.25", after.Values.Single(value => value.Path == "progress/play-seconds").Value);
    }

    private sealed class RejectingSink : ISaveDecodeSink
    {
        public int MaxValues => 1;
        public int MaxValueLength => 64;
        public int MaxDepth => 3;
        public long MaxFileBytes { get; init; } = 1_000_000;
        public int AddCalls { get; private set; }
        public bool Add(string path, string kind, string value, string? label = null)
        {
            AddCalls++;
            return false;
        }
        public void Warn(string message) => Assert.Fail("No traversal should follow a declined header value.");
        public void MarkTruncated(string reason) => Assert.Fail("This sink allows all native path depths.");
    }
}
