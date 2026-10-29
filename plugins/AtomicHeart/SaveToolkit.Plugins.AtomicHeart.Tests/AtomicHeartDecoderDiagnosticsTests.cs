using System.Buffers.Binary;
using SaveToolkit.Sdk;

namespace SaveToolkit.Plugins.AtomicHeart.Tests;

public sealed class AtomicHeartDecoderDiagnosticsTests
{
    [Theory]
    [InlineData(-1)]
    [InlineData(1)]
    public void IncorrectChunkOutputLengthKeepsTheContextualCodecError(int delta)
    {
        var bytes = SlotFixture.Create();
        var outer = SaveExtensionFile.Parse(bytes);
        var length = (ulong)(outer.Chunks[0].UncompressedBytes + delta);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(outer.DataOffset + 24), length);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(outer.DataOffset + 40), length);
        SlotFixture.RefreshDigest(bytes);

        AssertCodecError(bytes, SaveExtensionFile.Parse(bytes), outer.DataOffset);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void MalformedChunkKeepsItsOwnOffsetAndInnerException(int ordinal)
    {
        var bytes = SlotFixture.Create(chunks: 2);
        var outer = SaveExtensionFile.Parse(bytes);
        var chunk = outer.Chunks[ordinal];
        // Valid Kraken signature, but no quantum header or payload follows it.
        var chunks = outer.Chunks.ToArray();
        chunks[ordinal] = chunk with { CompressedBytes = 2 };

        AssertCodecError(bytes, outer with { Chunks = chunks }, chunk.Offset);
    }

    [Fact]
    public void AggregateLimitRejectsBeforeAllocatingOrReadingChunks()
    {
        var bytes = SlotFixture.Create();
        var outer = SaveExtensionFile.Parse(bytes);
        var chunk = outer.Chunks[0] with { UncompressedBytes = SaveExtensionFile.MaxBytes + 1 };
        var error = Assert.Throws<SaveFormatException>(() =>
            SlotDataDecoder.Decode(bytes, outer with { Chunks = [chunk] }));
        Assert.Equal("Decoded SlotData exceeds 128 MiB.", error.Message);
        Assert.Null(error.InnerException);
    }

    [Fact]
    public void InnerArrayMismatchKeepsItsUnwrappedGrammarError()
    {
        var plain = PlainFixture.Create();
        BinaryPrimitives.WriteInt32LittleEndian(plain, plain.Length);
        var bytes = SlotFixture.Create(plain: plain);
        var error = Assert.Throws<SaveFormatException>(() => SlotDataDecoder.Decode(bytes, SaveExtensionFile.Parse(bytes)));
        Assert.Equal("SlotData byte-array prefix does not match the decoded aggregate.", error.Message);
        Assert.Null(error.InnerException);
    }

    private static void AssertCodecError(byte[] bytes, SaveExtensionFile outer, int offset)
    {
        var error = Assert.Throws<SaveFormatException>(() => SlotDataDecoder.Decode(bytes, outer));
        Assert.Equal($"Invalid Kraken chunk at file offset 0x{offset:X}: Invalid or unsupported Kraken stream.", error.Message);
        var inner = Assert.IsType<InvalidDataException>(error.InnerException);
        Assert.Equal("Invalid or unsupported Kraken stream.", inner.Message);
    }
}
