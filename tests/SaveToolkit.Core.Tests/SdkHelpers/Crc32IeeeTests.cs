using System.Buffers.Binary;
using SaveToolkit.Sdk.Binary;

namespace SaveToolkit.Core.Tests.SdkHelpers;

public sealed class Crc32IeeeTests
{
    [Theory]
    [InlineData("", 0x00000000U)]
    [InlineData("123456789", 0xCBF43926U)]
    [InlineData("The quick brown fox jumps over the lazy dog", 0x414FA339U)]
    public void IndependentStandardVectors(string text, uint expected) =>
        Assert.Equal(expected, Crc32Ieee.Compute(System.Text.Encoding.ASCII.GetBytes(text)));

    [Fact]
    public void AppendSupportsEveryChunkBoundary_AndReadingDoesNotFinalizeState()
    {
        var bytes = "123456789"u8.ToArray();
        for (var split = 0; split <= bytes.Length; split++)
        {
            var crc = new Crc32Ieee();
            Assert.Equal(0U, crc.GetCurrentHash());
            crc.Append(bytes.AsSpan(0, split));
            _ = crc.GetCurrentHash();
            crc.Append([]);
            crc.Append(bytes.AsSpan(split));
            Assert.Equal(0xCBF43926U, crc.GetCurrentHash());
            Assert.Equal(0xCBF43926U, crc.GetCurrentHash());
            crc.Reset();
            Assert.Equal(0U, crc.GetCurrentHash());
            foreach (var value in bytes) crc.Append([value]);
            Assert.Equal(0xCBF43926U, crc.GetCurrentHash());
        }
    }

    [Fact]
    public void GearboxCoverageSentinelFoldingAndBigEndianStorageMatchIndependentFixture()
    {
        // Synthetic seed-zero v3 envelope: header 0300000000, checksum D4C1, payload 01020304.
        // Oracle: Python zlib.crc32(bytes.fromhex('0300000000ffff01020304')) == 0x4BBD9F7C.
        var envelope = Convert.FromHexString("0300000000D4C101020304");
        var crc = new Crc32Ieee();
        crc.Append(envelope.AsSpan(0, 5));
        crc.Append([0xFF, 0xFF]);
        crc.Append(envelope.AsSpan(7));
        var hash = crc.GetCurrentHash();
        Assert.Equal(0x4BBD9F7CU, hash);
        var folded = (ushort)((hash >> 16) ^ hash);
        Assert.Equal(0xD4C1, folded);
        Assert.Equal(BinaryPrimitives.ReadUInt16BigEndian(envelope.AsSpan(5, 2)), folded);
    }
}
