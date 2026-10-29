using System.Buffers.Binary;
using SaveToolkit.Sdk;
using SaveToolkit.Testing.Diff;

namespace SaveToolkit.Plugins.FinalFantasy16.Tests;

public sealed class FaithDecoderCorruptionTests
{
    private readonly FaithEvidenceDecoder _decoder = new();

    [Theory]
    [InlineData(1, false)]
    [InlineData(2, false)]
    [InlineData(2, true)]
    public void InvalidAllocatorsAreRejectedBeforeAnyOutput(int depth, bool acceptsValues)
    {
        var items = FaithTestData.Items();
        const int MetadataOffset = FaithInventory.Capacity * 20;
        BinaryPrimitives.WriteUInt64LittleEndian(items.AsSpan(MetadataOffset), ulong.MaxValue);
        var bytes = FaithTestData.Container(
            (FaithSchema.PlayerType, FaithSchema.PlayerVersion, new byte[FaithSchema.LegacyPlayerLength]),
            (FaithSchema.ItemType, FaithSchema.ItemVersion, items));
        var sink = new FaithDecodeTestSink { MaxDepth = depth, AcceptsValues = acceptsValues };

        var error = Assert.Throws<SaveFormatException>(() => _decoder.Decode(FaithTestData.File(bytes), sink));

        Assert.Contains("allocator bounds", error.Message);
        Assert.Equal(0, sink.AddCalls);
        Assert.False(sink.Truncated);
    }

    [Theory]
    [InlineData("crc")]
    [InlineData("block-size")]
    [InlineData("terminator")]
    public void ShallowOutputStillValidatesTheContainer(string corruption)
    {
        var bytes = FaithTestData.Container((99, 1, new byte[8]));
        var expected = corruption switch
        {
            "crc" => "CRC32 mismatch",
            "block-size" => "block size",
            "terminator" => "terminator is missing",
            _ => throw new ArgumentException("Unknown corruption.", nameof(corruption)),
        };
        if (corruption == "crc") bytes[FaithContainer.CrcOffset] ^= 1;
        else
        {
            if (corruption == "block-size")
                BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(FaithContainer.HeaderSize + FaithBlock.SizeOffset), uint.MaxValue);
            else bytes = bytes[..^sizeof(uint)];
            FaithTestData.FixCrc(bytes);
        }
        var sink = new FaithDecodeTestSink { MaxDepth = 1 };

        var error = Assert.Throws<SaveFormatException>(() => _decoder.Decode(FaithTestData.File(bytes), sink));

        Assert.Contains(expected, error.Message);
        Assert.Equal(0, sink.AddCalls);
        Assert.False(sink.Truncated);
    }

    [Theory]
    [InlineData("PPSA10665")]
    [InlineData("PPSA13803")]
    public void OutputLimitsDoNotHideInvalidSampleAllocatorBounds(string title)
    {
        var original = FaithSamples.Snapshot(title);
        var originalBytes = original.ReadAllBytes();
        var bytes = originalBytes.ToArray();
        var block = Assert.Single(FaithContainer.Parse(bytes).Blocks, block => block.Type == FaithSchema.ItemType);
        const int MetadataOffset = FaithInventory.Capacity * 20;
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(block.PayloadOffset + MetadataOffset), ulong.MaxValue);
        FaithTestData.FixCrc(bytes);
        var file = FaithSamples.WithBytes(original, bytes);

        Assert.Throws<SaveFormatException>(() => new FinalFantasy16Plugin().Open(file));
        var error = Assert.Throws<SaveFormatException>(() => new HookSaveDecoder(_decoder).Decode(file,
            new DecodeLimits { MaxValues = 0 }));
        Assert.Contains("allocator bounds", error.Message);
        Assert.Equal(originalBytes, original.ReadAllBytes());
    }

    [Theory]
    [InlineData(1, true, 0, true)]
    [InlineData(2, false, 1, false)]
    [InlineData(2, true, 4, false)]
    public void ValidFilesStillRespectOutputLimits(int depth, bool acceptsValues, int calls, bool truncated)
    {
        var bytes = FaithTestData.Container(
            (FaithSchema.PlayerType, FaithSchema.PlayerVersion, new byte[FaithSchema.LegacyPlayerLength]),
            (FaithSchema.ItemType, FaithSchema.ItemVersion, new byte[FaithSchema.ItemLength]));
        var sink = new FaithDecodeTestSink { MaxDepth = depth, AcceptsValues = acceptsValues };
        _decoder.Decode(FaithTestData.File(bytes), sink);
        Assert.Equal(calls, sink.AddCalls);
        Assert.Equal(truncated, sink.Truncated);
    }

}
