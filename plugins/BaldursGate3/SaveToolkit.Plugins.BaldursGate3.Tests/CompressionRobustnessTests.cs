using System.Buffers.Binary;
using SaveToolkit.Sdk;

namespace SaveToolkit.Plugins.BaldursGate3.Tests;

public sealed class CompressionRobustnessTests
{
    private const int InflatedSize = 16 * 1024 * 1024;
    private const int MaximumRejectionAllocation = 1024 * 1024;

    [Theory]
    [InlineData((byte)1, false)]
    [InlineData((byte)2, false)]
    [InlineData((byte)2, true)]
    [InlineData((byte)3, false)]
    public void InflatedExpandedSizeIsRejectedBeforeAllocatingOutput(byte codec, bool framed)
    {
        var encoded = PackageFixture.Compress(new byte[64], codec, framed);
        _ = LarianCompression.Decode(encoded, 64, codec, framed);
        var allocated = GC.GetAllocatedBytesForCurrentThread();
        var error = Record.Exception(() => LarianCompression.Decode(encoded, InflatedSize, codec, framed));
        allocated = GC.GetAllocatedBytesForCurrentThread() - allocated;
        Assert.True(allocated < MaximumRejectionAllocation, $"Invalid expanded size allocated {allocated:N0} bytes.");
        Assert.IsType<SaveFormatException>(error);
    }

    [Theory]
    [InlineData(15u)]
    [InlineData(16u)]
    [InlineData(18u)]
    public void CorruptFileTableCountIsRejectedBeforeAllocatingTheTable(uint version)
    {
        var bytes = PackageFixture.Create(version, payloads: [("opaque.bin", new byte[64])]);
        var listOffset = (int)BinaryData.U64(bytes, LspkLayout.FileListOffset);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(listOffset), 60_000);
        var allocated = GC.GetAllocatedBytesForCurrentThread();
        var error = Record.Exception(() => LspkPackage.Read(bytes));
        allocated = GC.GetAllocatedBytesForCurrentThread() - allocated;
        Assert.True(allocated < MaximumRejectionAllocation, $"Invalid file count allocated {allocated:N0} bytes.");
        Assert.IsType<SaveFormatException>(error);
    }

    [Theory]
    [InlineData((byte)1, false)]
    [InlineData((byte)2, false)]
    [InlineData((byte)2, true)]
    [InlineData((byte)3, false)]
    public void EveryIncompleteCompressedPrefixIsRejected(byte codec, bool framed)
    {
        byte[] payload = [.. Enumerable.Range(0, 4096).Select(index => (byte)index)];
        var encoded = PackageFixture.Compress(payload, codec, framed);
        for (var length = 0; length < encoded.Length; length++)
        {
            var prefix = encoded[..length];
            var error = Record.Exception(() => LarianCompression.Decode(prefix, payload.Length, codec, framed));
            Assert.True(error is SaveFormatException, $"Codec {codec}, framed {framed}, prefix {length}/{encoded.Length}: {error}");
        }
        Assert.Equal(payload, LarianCompression.Decode(encoded, payload.Length, codec, framed));
    }

    [Fact]
    public void ZlibValidatesItsChecksumAndPreservesAcceptanceOfTrailingResidue()
    {
        byte[] payload = [1, 2, 3];
        var encoded = PackageFixture.Compress(payload, 1);
        Assert.Equal(payload, LarianCompression.Decode([.. encoded, 0xde, 0xad], payload.Length, 1));
        encoded[^1] ^= 1;
        Assert.Throws<SaveFormatException>(() => LarianCompression.Decode(encoded, payload.Length, 1));
        Assert.Empty(LarianCompression.Decode(Convert.FromHexString("789C030000000001"), 0, 1));
        Assert.Throws<SaveFormatException>(() => LarianCompression.Decode([], 0, 1));
    }
}
