using K4os.Compression.LZ4;
using SaveToolkit.Sdk;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.Witcher3.Tests;

[Collection(typeof(AllocationTestCollection))]
public sealed class WitcherLz4RobustnessTests
{
    [Fact]
    public void InvalidLz4LengthIsRejectedBeforeAllocatingDeclaredOutput()
    {
        const int declaredSize = 32 * 1024 * 1024;
        var bytes = RobustnessSave.StoredChunk([0xF0, 0xFF], declaredSize);
        _ = Assert.Throws<SaveFormatException>(() => WitcherContainer.Parse(bytes));
        var allocated = AllocationProbe.Measure(() =>
        {
            _ = Assert.Throws<SaveFormatException>(() => WitcherContainer.Parse(bytes));
        });
        allocated.AssertLessThan(128 * 1024, $"Malformed block allocated {allocated.MinimumBytes:N0} bytes before rejection.");
    }

    [Fact]
    public void LaterMalformedLz4ChunkIsCheckedBeforeEarlierOutputAllocation()
    {
        var bytes = SyntheticSave.Wrap(new byte[4 * 1024 * 1024]);
        var second = SyntheticSave.HeaderSize + BinaryData.Int32(bytes, 16);
        bytes.AsSpan(second, BinaryData.Int32(bytes, 28)).Fill(0xFF);
        _ = Assert.Throws<SaveFormatException>(() => WitcherContainer.Parse(bytes));
        var allocated = AllocationProbe.Measure(() =>
        {
            _ = Assert.Throws<SaveFormatException>(() => WitcherContainer.Parse(bytes));
        });
        allocated.AssertLessThan(128 * 1024);
    }

    public static TheoryData<byte[], int> MalformedBlocks => new()
    {
        { [], 1 }, // No final literal sequence.
        { [0xF0], 20 }, // Missing literal length extension.
        { [0xF0, 0xFF], 1024 }, // Unterminated literal length extension.
        { [0x30, 1, 2], 3 }, // Truncated literals.
        { [0x10, 1, 1], 20 }, // Truncated match offset.
        { [0x13, 1, 0, 0, 0x50, 1, 2, 3, 4, 5], 13 }, // Zero distance.
        { [0x13, 1, 2, 0, 0x50, 1, 2, 3, 4, 5], 13 }, // Reference before output.
        { [0x1F, 1, 1, 0], 40 }, // Missing match length extension.
        { [0x1F, 1, 1, 0, 0xFF], 1024 }, // Unterminated match length extension.
        { [0x13, 1, 1, 0], 8 }, // Ends with a match.
        { [0x18, 1, 1, 0, 0x40, 1, 2, 3, 4], 17 }, // Fewer than five final literals.
        { [0x11, 1, 1, 0, 0x50, 1, 2, 3, 4, 5], 11 }, // Last match starts too late.
        { [0x10, 1], 2 }, // Declared output too long.
        { [0x20, 1, 2], 1 }, // Declared output too short.
    };

    [Theory]
    [MemberData(nameof(MalformedBlocks))]
    public void MalformedTokenSequencesHaveClearFormatErrors(byte[] block, int expanded)
    {
        var error = Assert.Throws<SaveFormatException>(() => Lz4BlockValidation.Validate(block, expanded));
        Assert.Contains("LZ4", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void IndependentLz4VectorsKeepLiteralAndOverlappingMatchSemantics(bool extendedMatch)
    {
        byte[] block = extendedMatch ? [0x1F, 65, 1, 0, 0, 0x50, 66, 67, 68, 69, 70]
            : [0x13, 65, 1, 0, 0x5F, 66, 67, 68, 69, 70]; // Final low nibble is ignored.
        byte[] expected = [.. Enumerable.Repeat((byte)65, extendedMatch ? 20 : 8), 66, 67, 68, 69, 70];
        Lz4BlockValidation.Validate(block, expected.Length);
        Assert.Equal(expected, WitcherContainer.Parse(RobustnessSave.StoredChunk(block, expected.Length)).Payload);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(15)]
    [InlineData(270)]
    [InlineData(8192)]
    public void CodecGeneratedBlocksValidateAndDecodeWithExactBytes(int length)
    {
        var expected = Enumerable.Range(0, length).Select(i => (byte)(i % 251)).ToArray();
        var buffer = new byte[LZ4Codec.MaximumOutputSize(length)];
        var count = LZ4Codec.Encode(expected, buffer);
        var block = buffer[..count];
        Lz4BlockValidation.Validate(block, length);
        var actual = new byte[length];
        Assert.Equal(length, LZ4Codec.Decode(block, actual));
        Assert.Equal(expected, actual);
    }
}
