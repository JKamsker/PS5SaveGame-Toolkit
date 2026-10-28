using System.Buffers.Binary;
using SaveToolkit.Sdk.Diff;
using SaveToolkit.Sdk.Saves;
using SaveToolkit.Testing;
using SaveToolkit.Testing.Diff;

namespace SaveToolkit.Plugins.Insomniac.Tests;

public sealed class DecoderHookTests
{
    [Fact]
    public void ProductionPluginPublishesOneSdkOnlyHookForKnownRiftFiles()
    {
        var plugin = new InsomniacPlugin();
        var hook = Assert.Single(((ISaveDecoderProvider)plugin).Decoders);
        Assert.Equal("insomniac-evidence", hook.Id);
        Assert.IsNotAssignableFrom<ISaveDecoder>(hook);
        var file = PlayerCorpusTests.Find("i29slot0-s-autosave", 0);
        Assert.True(hook.CanDecode(file));
        Assert.False(hook.CanDecode(MemorySaveSet.SingleFile(file.FileName, file.ReadAllBytes(), "PPSA00000")));
        Assert.False(hook.CanDecode(MemorySaveSet.SingleFile("other.save", file.ReadAllBytes(), "PPSA01474")));
        Assert.False(hook.CanDecode(MemorySaveSet.SingleFile(file.FileName, new byte[32], "PPSA01474")));
    }

    [Fact]
    public void RegisteredHookDecodesEveryClaimedSampleDeterministicallyWithoutChangingBytes()
    {
        var decoder = new HookSaveDecoder(Assert.Single(new InsomniacPlugin().Decoders));
        var files = SampleSaves.Load("PPSA01474").AllDiskFiles
            .Where(file => file.FileName is "i29-s.save" or "i29-userprefs.save").ToArray();
        Assert.Equal(76, files.Length);
        foreach (var file in files)
        {
            Assert.True(decoder.CanDecode(file));
            var original = file.ReadAllBytes();
            var tree = decoder.Decode(file, new());
            Assert.False(tree.Truncated);
            Assert.NotEmpty(tree.Values);
            Assert.Equal(tree.Values, decoder.Decode(file, new()).Values);
            Assert.Equal(tree.Values.Count, tree.Values.Select(value => value.Path).Distinct().Count());
            Assert.DoesNotContain(tree.Values, value => value.Path.Contains("CampaignSaveID", StringComparison.Ordinal) ||
                value.Path.Contains("LastSaveTime", StringComparison.Ordinal));
            Assert.Equal(original, file.ReadAllBytes());
        }
    }

    [Fact]
    public void HookStopsImmediatelyWhenSinkRefusesAValue()
    {
        var hook = Assert.Single(new InsomniacPlugin().Decoders);
        var files = SampleSaves.Load("PPSA01474").AllDiskFiles;
        foreach (var name in new[] { "i29-s.save", "i29-userprefs.save" })
        {
            var sink = new RejectingSink();
            hook.Decode(files.First(file => file.FileName == name), sink);
            Assert.Equal(1, sink.AddCalls);
        }
    }

    [Fact]
    public void HookHonorsFileValueAndDepthBudgets()
    {
        var decoder = new HookSaveDecoder(Assert.Single(new InsomniacPlugin().Decoders));
        var file = PlayerCorpusTests.Find("i29slot0-s-autosave", 0);
        Assert.Throws<InvalidDataException>(() => decoder.Decode(file, new() { MaxFileBytes = file.Length - 1 }));
        var capped = decoder.Decode(file, new() { MaxValues = 1 });
        Assert.True(capped.Truncated);
        Assert.Single(capped.Values);
        var shallow = decoder.Decode(file, new() { MaxDepth = 0 });
        Assert.True(shallow.Truncated);
        Assert.Empty(shallow.Values);
        Assert.Contains(shallow.Warnings, warning => warning.Contains("depth limit", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnprovenOpaqueOrDuplicateRecordIdentitiesMarkEvidenceIncomplete(bool duplicateGuid)
    {
        const uint weaponType = 0xA89E9C3C;
        const int weaponMaskBytes = 2;
        const int recordHeaderBytes = 2 * sizeof(uint);
        var file = PlayerCorpusTests.Find("i29slot0-s-autosave", 0);
        var container = Dat1Container.Parse(file.ReadAllBytes());
        var records = InventoryReader.Read(container).First(block =>
            block.Records.Count(record => record.Type == weaponType) >= 2).Records
            .Where(record => record.Type == weaponType).Take(2).ToArray();
        if (duplicateGuid)
            container.Bytes.AsSpan(records[0].Offset + weaponMaskBytes, sizeof(ulong))
                .CopyTo(container.Bytes.AsSpan(records[1].Offset + weaponMaskBytes, sizeof(ulong)));
        else
            BinaryPrimitives.WriteUInt32LittleEndian(container.Bytes.AsSpan(records[0].Offset - recordHeaderBytes), uint.MaxValue);
        var changed = MemorySaveSet.SingleFile(file.FileName, container.Bytes, "PPSA01474");
        var decoder = new HookSaveDecoder(Assert.Single(new InsomniacPlugin().Decoders));
        var tree = decoder.Decode(changed, new());
        Assert.True(tree.Truncated);
        Assert.Contains(tree.Warnings, warning => warning.Contains("Inventory identity", StringComparison.Ordinal));
        Assert.Contains(tree.Values, value => value.Path.StartsWith("inventory/", StringComparison.Ordinal));
    }

    private sealed class RejectingSink : ISaveDecodeSink
    {
        public int MaxValues => 0;
        public int MaxValueLength => 256;
        public int MaxDepth => 64;
        public long MaxFileBytes => long.MaxValue;
        public int AddCalls { get; private set; }

        public bool Add(string path, string kind, string value, string? label = null)
        {
            Assert.Equal(0, AddCalls++);
            return false;
        }

        public void Warn(string message) { }
        public void MarkTruncated(string reason) { }
    }
}
