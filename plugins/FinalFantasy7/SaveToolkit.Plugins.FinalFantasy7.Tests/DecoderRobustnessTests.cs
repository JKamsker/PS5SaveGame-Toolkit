using System.Buffers.Binary;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Diff;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.FinalFantasy7.Tests;

public sealed class DecoderRobustnessTests
{
    [Theory]
    [InlineData("PPSA01911", false)]
    [InlineData("PPSA08664", false)]
    [InlineData("PPSA18936", false)]
    [InlineData("PPSA01911", true)]
    [InlineData("PPSA08664", true)]
    [InlineData("PPSA18936", true)]
    public void CorruptLaterHeadersRejectBeforeAnyEvidenceOrTruncation(string title, bool common)
    {
        var schema = Ff7Schema.ForTitle(title)!;
        var bytes = common ? ContainerTests.Common(title) : SyntheticGameplay.Create(title).Bytes;
        var records = LinkedRecords.Parse(bytes, schema, common);
        var offsets = common ? new[] { records[^1].Offset, LinkedRecords.DeclaredLengthOffset }
            : records.Skip(1).GroupBy(record => record.Kind).Select(group => group.Last())
                .Select(record => record.Offset + (record.IsTail ? LinkedRecords.FlagsOffset : LinkedRecords.DeclaredLengthOffset));
        foreach (var offset in offsets)
        {
            var original = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset));
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), uint.MaxValue);
            var file = MemorySaveSet.SingleFile(common ? schema.CommonName : schema.Prefix + "000", bytes, title);
            var hook = Assert.Single(new FinalFantasy7Plugin().Decoders);
            Assert.True(hook.CanDecode(file)); // First-header probing deliberately cannot validate later records.
            foreach (var (depth, acceptValues) in new[] { (0, true), (2, true), (3, true), (3, false) })
            {
                var sink = new RecordingSink(depth, acceptValues);
                var error = Assert.Throws<SaveFormatException>(() => hook.Decode(file, sink));
                Assert.Contains("FFVII record", error.Message);
                AssertNoOutput(sink);
            }
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), original);
        }
    }

    [Theory]
    [InlineData("PPSA01911")]
    [InlineData("PPSA08664")]
    [InlineData("PPSA18936")]
    public void MutatedSampleHeadersRejectBeforeEvidenceEvenWithShallowDepth(string title)
    {
        var schema = Ff7Schema.ForTitle(title)!;
        var source = SampleSaves.Load(title).AllDiskFiles.First(file => file.FileName == schema.Prefix + "000");
        var bytes = source.ReadAllBytes();
        var records = LinkedRecords.Parse(bytes, schema, common: false);
        foreach (var record in new[] { records[1], records[records.Count / 2], records[^1] })
        {
            var offset = record.Offset + LinkedRecords.NextPointerOffset;
            var original = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset));
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), uint.MaxValue);
            var file = MemorySaveSet.SingleFile(source.FileName, bytes, title);
            foreach (var depth in new[] { 0, 3 })
            {
                var sink = new RecordingSink(depth);
                var error = Assert.Throws<SaveFormatException>(() =>
                    Assert.Single(new FinalFantasy7Plugin().Decoders).Decode(file, sink));
                Assert.Contains($"0x{record.Offset:X}", error.Message);
                AssertNoOutput(sink);
            }
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), original);
        }
        Assert.Equal(source.ReadAllBytes(), bytes);
    }

    [Theory]
    [InlineData("PPSA01911")]
    [InlineData("PPSA08664")]
    [InlineData("PPSA18936")]
    public void DecoderRejectsShortBodiesDespiteMatchingProbeMetadata(string title)
    {
        var bytes = ContainerTests.Common(title);
        var schema = Ff7Schema.ForTitle(title)!;
        var identity = MemorySaveSet.SingleFile(schema.CommonName, [], title);
        var hook = Assert.Single(new FinalFantasy7Plugin().Decoders);
        foreach (var length in new[] { 0, 15, 39, 40, bytes.Length - 16, bytes.Length - 1 })
        {
            var file = new ObservedSaveFile(identity, bytes.Length, bytes.AsMemory(0, length));
            var sink = new RecordingSink(0);
            Assert.Throws<SaveFormatException>(() => hook.Decode(file, sink));
            Assert.Equal(length < LinkedRecords.ContentOffset ? 0 : 1, file.BodyReads);
            AssertNoOutput(sink);
        }
    }

    [Theory]
    [InlineData("PPSA01911")]
    [InlineData("PPSA08664")]
    [InlineData("PPSA18936")]
    public void HostByteLimitRejectsBeforeProbingOrReading(string title)
    {
        var schema = Ff7Schema.ForTitle(title)!;
        var identity = MemorySaveSet.SingleFile(schema.CommonName, [], title);
        foreach (var length in new long[] { Ff7Schema.CommonFileLength, int.MaxValue, long.MaxValue })
        {
            var file = new ObservedSaveFile(identity, length, ReadOnlyMemory<byte>.Empty) { RejectReads = true };
            var sink = new RecordingSink(3) { MaxFileBytes = Ff7Schema.CommonFileLength - 1 };
            var error = Assert.Throws<InvalidDataException>(() =>
                Assert.Single(new FinalFantasy7Plugin().Decoders).Decode(file, sink));
            Assert.Contains("input bound", error.Message);
            Assert.Equal(0, file.HeaderReads);
            Assert.Equal(0, file.BodyReads);
            AssertNoOutput(sink);
        }
    }

    [Theory]
    [InlineData("PPSA01911", 42)]
    [InlineData("PPSA08664", 49)]
    [InlineData("PPSA18936", 49)]
    public void ValidGameplayHonorsDepthAndFullSinkAfterCompleteValidation(string title, int values)
    {
        var schema = Ff7Schema.ForTitle(title)!;
        var fixture = SyntheticGameplay.Create(title);
        var file = MemorySaveSet.SingleFile(schema.Prefix + "000", fixture.Bytes, title);
        var hook = Assert.Single(new FinalFantasy7Plugin().Decoders);
        var shallow = new RecordingSink(2);
        hook.Decode(file, shallow);
        Assert.Equal(0, shallow.AddCalls);
        Assert.Single(shallow.Truncations);
        var full = new RecordingSink(3, acceptValues: false);
        hook.Decode(file, full);
        Assert.Equal(1, full.AddCalls);
        Assert.Empty(full.Paths);
        var complete = new RecordingSink(3) { MaxFileBytes = fixture.Bytes.Length };
        hook.Decode(file, complete);
        Assert.Equal(values, complete.AddCalls);
        Assert.Equal(values, complete.Paths.Distinct().Count());
        Assert.Empty(complete.Warnings);
        Assert.Empty(complete.Truncations);
        Assert.Equal(fixture.Bytes, file.ReadAllBytes());
    }

    private static void AssertNoOutput(RecordingSink sink)
    {
        Assert.Equal(0, sink.AddCalls);
        Assert.Empty(sink.Paths);
        Assert.Empty(sink.Warnings);
        Assert.Empty(sink.Truncations);
    }

    private sealed class RecordingSink(int depth, bool acceptValues = true) : ISaveDecodeSink
    {
        public int MaxValues => acceptValues ? 100 : 0;
        public int MaxValueLength => 256;
        public int MaxDepth => depth;
        public long MaxFileBytes { get; init; } = 32 * 1024 * 1024;
        public int AddCalls { get; private set; }
        public List<string> Paths { get; } = [];
        public List<string> Warnings { get; } = [];
        public List<string> Truncations { get; } = [];

        public bool Add(string path, string kind, string value, string? label = null)
        {
            AddCalls++;
            if (acceptValues) Paths.Add(path);
            return acceptValues;
        }

        public void Warn(string message) => Warnings.Add(message);
        public void MarkTruncated(string reason) => Truncations.Add(reason);
    }
}
