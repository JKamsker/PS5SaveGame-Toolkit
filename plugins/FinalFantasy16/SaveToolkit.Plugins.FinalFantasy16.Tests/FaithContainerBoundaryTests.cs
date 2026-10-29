using System.Buffers.Binary;
using SaveToolkit.Sdk;

namespace SaveToolkit.Plugins.FinalFantasy16.Tests;

public sealed class FaithContainerBoundaryTests
{
    private const int MaximumBlocks = 4096;
    private readonly FinalFantasy16Plugin _plugin = new();
    private readonly FaithEvidenceDecoder _decoder = new();

    [Theory]
    [InlineData("PPSA10665", false)]
    [InlineData("PPSA10665", true)]
    [InlineData("PPSA13803", false)]
    [InlineData("PPSA13803", true)]
    public void EveryTruncatedPrefixIsRejectedEvenWithRepairedCrc(string title, bool system)
    {
        var bytes = FaithTestData.Container((99, 1, new byte[8]), (100, 2, new byte[16]));
        if (system) BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(FaithContainer.VersionOffset), FaithContainer.SystemVersion);
        var name = system ? "system-save-data" : "game-save-data";
        using (var valid = _plugin.Open(FaithTestData.File(bytes, title, name)))
            Assert.Equal(bytes, Assert.Single(valid.Serialize()).Content.ToArray());

        for (var length = 0; length < bytes.Length; length++)
        {
            var truncated = bytes[..length];
            if (length >= FaithContainer.HeaderSize) FaithTestData.FixCrc(truncated);
            var file = FaithTestData.File(truncated, title, name);
            if (length < FaithContainer.HeaderSize)
            {
                Assert.False(_plugin.Probe(file).IsMatch);
                Assert.False(_decoder.CanDecode(file));
            }
            var openError = Assert.Throws<SaveFormatException>(() => _plugin.Open(file));
            var sink = new FaithDecodeTestSink();
            var decodeError = Assert.Throws<SaveFormatException>(() => _decoder.Decode(file, sink));
            Assert.False(string.IsNullOrWhiteSpace(openError.Message));
            Assert.False(string.IsNullOrWhiteSpace(decodeError.Message));
            Assert.Equal(0, sink.AddCalls);
        }
    }

    [Theory]
    [InlineData(0U)]
    [InlineData(7U)]
    [InlineData(8U)]
    [InlineData(16U)]
    [InlineData(23U)]
    [InlineData(25U)]
    [InlineData(0x7FFFFFF8U)]
    [InlineData(0x80000000U)]
    [InlineData(0xFFFFFFF8U)]
    [InlineData(uint.MaxValue)]
    public void InvalidSizesInFirstMiddleAndLastBlocksAreRejected(uint size)
    {
        var original = FaithTestData.Container((99, 1, new byte[8]), (100, 1, new byte[8]), (101, 1, new byte[8]));
        foreach (var block in FaithContainer.Parse(original).Blocks)
        {
            var bytes = original.ToArray();
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(block.Offset + FaithBlock.SizeOffset), size);
            FaithTestData.FixCrc(bytes);
            var file = FaithTestData.File(bytes);
            var error = Assert.Throws<SaveFormatException>(() => _plugin.Open(file));
            Assert.Equal($"Invalid Faith block size at 0x{block.Offset:X}.", error.Message);
            Assert.Throws<SaveFormatException>(() => _decoder.Decode(file, new FaithDecodeTestSink()));
        }
    }

    [Theory]
    [InlineData(MaximumBlocks - 1)]
    [InlineData(MaximumBlocks)]
    [InlineData(MaximumBlocks + 1)]
    public void BlockCountBoundaryMakesProgressAndEnforcesTheLimit(int count)
    {
        var bytes = FaithTestData.Container(Enumerable.Repeat((99U, 1UL, Array.Empty<byte>()), count).ToArray());
        var file = FaithTestData.File(bytes);
        if (count > MaximumBlocks)
        {
            var error = Assert.Throws<SaveFormatException>(() => _plugin.Open(file));
            Assert.Contains("excessive Faith block headers", error.Message);
            Assert.Throws<SaveFormatException>(() => _decoder.Decode(file, new FaithDecodeTestSink()));
            return;
        }

        Assert.Equal(count, FaithContainer.Parse(bytes).Blocks.Count);
        using var document = _plugin.Open(file);
        _ = document.BuildView();
        Assert.False(document.CanEdit);
        Assert.Equal(bytes, Assert.Single(document.Serialize()).Content.ToArray());
        var sink = new FaithDecodeTestSink();
        _decoder.Decode(file, sink);
        Assert.Equal(0, sink.AddCalls);
    }

    [Fact]
    public void EmptyContainerIsRejectedButOpaqueTypesAndTrailerArePreserved()
    {
        var error = Assert.Throws<SaveFormatException>(() => _plugin.Open(FaithTestData.File(FaithTestData.Container())));
        Assert.Contains("no data blocks", error.Message);
        var bytes = FaithTestData.Container((uint.MaxValue, ulong.MaxValue, []));
        bytes = [.. bytes, 0xFF, 0xFF, 0xFF, 0xFF, 1];
        FaithTestData.FixCrc(bytes);
        using var document = _plugin.Open(FaithTestData.File(bytes));
        _ = document.BuildView();
        _ = document.ExpandNode("block/0");
        _ = document.ExpandNode("words/0/0/0");
        Assert.False(document.CanEdit);
        Assert.Equal(bytes, Assert.Single(document.Serialize()).Content.ToArray());
    }

    [Theory]
    [InlineData("PPSA10665")]
    [InlineData("PPSA13803")]
    public void SampleBlockHeaderPayloadAndTerminatorTruncationsAreRejected(string title)
    {
        var original = FaithSamples.Snapshot(title);
        var bytes = original.ReadAllBytes();
        var container = FaithContainer.Parse(bytes);
        var cutPoints = container.Blocks.SelectMany(block => new[]
        {
            block.Offset, block.Offset + 1, block.Offset + sizeof(uint), block.PayloadOffset - 1,
            block.PayloadOffset, block.PayloadOffset + block.PayloadLength - 1,
        }).Append(container.TrailerOffset).Append(container.TrailerOffset + sizeof(uint) - 1).Distinct();
        foreach (var length in cutPoints)
        {
            var truncated = bytes[..length];
            FaithTestData.FixCrc(truncated);
            var file = FaithSamples.WithBytes(original, truncated);
            Assert.Throws<SaveFormatException>(() => _plugin.Open(file));
            Assert.Throws<SaveFormatException>(() => _decoder.Decode(file, new FaithDecodeTestSink()));
        }
        Assert.Equal(bytes, original.ReadAllBytes());
    }
}
