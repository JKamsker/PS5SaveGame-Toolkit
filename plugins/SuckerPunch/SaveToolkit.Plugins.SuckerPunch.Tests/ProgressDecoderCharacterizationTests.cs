using System.Buffers.Binary;
using System.Text;
using SaveToolkit.Sdk.Diff;

namespace SaveToolkit.Plugins.SuckerPunch.Tests;

public sealed class ProgressDecoderCharacterizationTests
{
    private const int PrefixLength = 1024;
    private const int CounterTableStart = 128;
    private const int SecondCounterTableStart = 512;
    private const int DifficultyLookBehind = 64;
    private const int TimerRelativeOffset = 231;
    private const float PlaySeconds = 123.25f;

    [Theory]
    [InlineData(47, false)]
    [InlineData(48, true)]
    [InlineData(49, true)]
    [InlineData(50, false)]
    public void CompleteDifficultySequenceRetainsVersionSpecificTimer(int version, bool hasTimer)
    {
        var save = new SyntheticSave(false, prefixLength: PrefixLength);
        AddProgress(save, CounterTableStart);
        save.Write(12, (uint)version);
        save.Repair();
        using var document = save.Open();
        Assert.Equal("Medium", document.GetValue("progress/difficulty").AsString());
        if (hasTimer) Assert.Equal(PlaySeconds, document.GetValue("progress/play-seconds").AsDouble());
        else Assert.Throws<KeyNotFoundException>(() => document.GetValue("progress/play-seconds"));
        Assert.Equal(save.Bytes, Assert.Single(document.Serialize()).Content.ToArray());
    }

    [Theory]
    [InlineData("duplicate")]
    [InlineData("name")]
    [InlineData("prefix")]
    public void AmbiguousOrIncompleteProgressRetainsInspectionFallback(string mutation)
    {
        var save = new SyntheticSave(false, prefixLength: PrefixLength);
        AddProgress(save, CounterTableStart);
        if (mutation == "duplicate") AddProgress(save, SecondCounterTableStart);
        else if (mutation == "name") save.Bytes[CounterTableStart + "\x04\0\0\0Easy".Length + sizeof(float)] ^= 1;
        else save.Bytes[CounterTableStart - DifficultyLookBehind] ^= 1;
        save.Repair();
        using var document = save.Open();
        Assert.Throws<KeyNotFoundException>(() => document.GetValue("progress/difficulty"));
        Assert.Throws<KeyNotFoundException>(() => document.GetValue("progress/play-seconds"));
        Assert.True(document.CanEdit);
        Assert.Equal(save.Bytes, Assert.Single(document.Serialize()).Content.ToArray());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NativeDecoderStopsAtEveryDeclinedValueWithoutLaterWarningsOrTraversal(bool yotei)
    {
        var save = new SyntheticSave(yotei, prefixLength: PrefixLength);
        if (!yotei) AddProgress(save, CounterTableStart);
        save.Repair();
        var hook = Assert.Single(((ISaveDecoderProvider)new SuckerPunchPlugin()).Decoders);
        var complete = new RecordingSink(int.MaxValue);
        hook.Decode(save.File(), complete);
        string[] headerPaths = ["container/word0", "container/word1", "container/word2", "container/word3"];
        Assert.Equal(headerPaths, complete.Paths.Take(4));
        if (!yotei)
        {
            Assert.Equal("progress/difficulty", complete.Paths[4]);
            Assert.Equal("progress/play-seconds", complete.Paths[5]);
            Assert.Equal(save.Inventory.Keys.Order(StringComparer.Ordinal), complete.Paths.Skip(6)
                .Where(path => path.EndsWith("/quantity", StringComparison.Ordinal)).Select(path => path.Split('/')[1]));
        }
        // Check every emitted value in Tsushima and every Yōtei section transition, including its final registry word.
        foreach (var accepted in StopPositions(complete.Paths, yotei))
        {
            var sink = new RecordingSink(accepted);
            hook.Decode(save.File(), sink);
            var lastEvent = complete.Events.FindIndex(item => item.StartsWith(complete.Paths[accepted] + "=", StringComparison.Ordinal));
            Assert.Equal(complete.Events.Take(lastEvent + 1), sink.Events);
            Assert.Equal(accepted + 1, sink.Paths.Count);
        }
    }

    private static IEnumerable<int> StopPositions(List<string> paths, bool yotei)
    {
        if (!yotei) return Enumerable.Range(0, paths.Count);
        var statistics = paths.FindIndex(path => path.StartsWith("yotei/statistics/", StringComparison.Ordinal));
        var gear = paths.FindIndex(path => path.StartsWith("yotei/gear/", StringComparison.Ordinal));
        var registry = paths.FindIndex(path => path.StartsWith("yotei/registry/", StringComparison.Ordinal));
        const int lastHeader = 3;
        return [0, lastHeader, lastHeader + 1, statistics - 1, statistics, gear - 1, gear,
            registry - 1, registry, paths.Count - 1];
    }

    private static void AddProgress(SyntheticSave save, int start)
    {
        using var stream = new MemoryStream(save.Bytes, writable: true);
        using var writer = new BinaryWriter(stream, Encoding.ASCII);
        stream.Position = start - DifficultyLookBehind;
        WriteName(writer, "Medium");
        WriteName(writer, "Ship");
        stream.Position = start;
        foreach (var name in new[] { "Easy", "Medium", "Hard", "Nightmare" })
        {
            WriteName(writer, name);
            writer.Write(1.5f);
        }
        BinaryPrimitives.WriteSingleLittleEndian(save.Bytes.AsSpan(start + TimerRelativeOffset), PlaySeconds);
    }

    private static void WriteName(BinaryWriter writer, string name)
    {
        writer.Write((uint)name.Length);
        writer.Write(Encoding.ASCII.GetBytes(name));
    }

    private sealed class RecordingSink(int accepted) : ISaveDecodeSink
    {
        public int MaxValues => int.MaxValue;
        public int MaxValueLength => 256;
        public int MaxDepth => 4;
        public long MaxFileBytes => 1_000_000;
        public List<string> Events { get; } = [];
        public List<string> Paths { get; } = [];
        public bool Add(string path, string kind, string value, string? label = null)
        {
            Paths.Add(path);
            Events.Add(path + "=" + kind + ":" + value + ":" + label);
            return Paths.Count <= accepted;
        }
        public void Warn(string message) => Events.Add("warning:" + message);
        public void MarkTruncated(string reason) => Assert.Fail(reason);
    }
}
