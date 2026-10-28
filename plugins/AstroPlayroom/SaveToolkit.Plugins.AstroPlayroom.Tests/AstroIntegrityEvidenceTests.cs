using System.Buffers.Binary;

namespace SaveToolkit.Plugins.AstroPlayroom.Tests;

/// <summary>Research checks only: these are candidate algorithms, not format integrity fields.</summary>
public sealed class AstroIntegrityEvidenceTests
{
    [Fact]
    public void CandidateAlgorithms_HaveStandardVectors()
    {
        Assert.Equal(0xCBF43926u, Crc32("123456789"u8));
        Assert.Equal(0x091E01DEu, Adler32("123456789"u8));
        Assert.Equal(0u, Crc32([]));
        Assert.Equal(1u, Adler32([]));
    }

    [Fact]
    public void BothGenerations_HaveNoHeaderLengthOrCandidateChecksum()
    {
        foreach (var file in AstroTestData.Samples())
        {
            var bytes = file.ReadAllBytes();
            Assert.True(bytes.AsSpan(0, AstroTestData.TextOffset).IndexOfAnyExcept((byte)0) < 0);
            Assert.True(bytes.AsSpan(AstroTestData.TextOffset, 2).SequenceEqual("\r\n"u8));
            const int firstTag = AstroTestData.TextOffset + 2;
            var terminator = bytes.AsSpan(AstroTestData.TextOffset).IndexOf((byte)0) + AstroTestData.TextOffset;
            var rootEnd = bytes.AsSpan().IndexOf("</PreInstall>"u8) + "</PreInstall>"u8.Length;
            Assert.Equal(0x7776, terminator);
            Assert.Equal(file.Directory.Generation == 0 ? 0x7776 : 0x7729, rootEnd);
            Assert.True(bytes.AsSpan(terminator).IndexOfAnyExcept((byte)0) < 0);

            var text = bytes.AsSpan(AstroTestData.TextOffset, terminator - AstroTestData.TextOffset);
            Assert.Equal(file.Directory.Generation == 0 ? 0xD15E8648u : 0x910A25C2u, Crc32(text));
            Assert.Equal(file.Directory.Generation == 0 ? 0xACF2293Bu : 0xC4242ACDu, Adler32(text));
            Assert.Equal(file.Directory.Generation == 0 ? 0x23272Du : 0x2328BFu, Sum(text));

            // Check both possible starts, both ends (root/opaque suffix), with and without NUL.
            foreach (var start in new[] { AstroTestData.TextOffset, firstTag })
            {
                foreach (var end in new[] { rootEnd, terminator, terminator + 1 })
                {
                    var candidate = bytes.AsSpan(start, end - start);
                    uint[] words = [(uint)candidate.Length, (uint)end, Crc32(candidate), Adler32(candidate), Sum(candidate)];
                    foreach (var word in words)
                    {
                        Assert.NotEqual(0u, word);
                        for (var offset = 0; offset + sizeof(uint) <= firstTag; offset++)
                        {
                            Assert.NotEqual(word, BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset)));
                            Assert.NotEqual(word, BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(offset)));
                        }
                    }

                    var length = checked((ushort)candidate.Length);
                    for (var offset = 0; offset + sizeof(ushort) <= firstTag; offset++)
                    {
                        Assert.NotEqual(length, BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(offset)));
                        Assert.NotEqual(length, BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(offset)));
                    }
                }
            }
        }
    }

    // Straightforward bitwise CRC-32/ISO-HDLC; polynomial specified by RFC 1952.
    private static uint Crc32(ReadOnlySpan<byte> bytes)
    {
        const uint reflectedPolynomial = 0xEDB88320;
        const int bitsPerByte = 8;
        var crc = uint.MaxValue;
        foreach (var value in bytes)
        {
            crc ^= value;
            for (var bit = 0; bit < bitsPerByte; bit++)
            {
                crc = (crc & 1) != 0 ? (crc >> 1) ^ reflectedPolynomial : crc >> 1;
            }
        }

        return ~crc;
    }

    private static uint Adler32(ReadOnlySpan<byte> bytes)
    {
        const uint modulus = 65521;
        uint a = 1;
        uint b = 0;
        foreach (var value in bytes)
        {
            a = (a + value) % modulus;
            b = (b + a) % modulus;
        }

        return (b << 16) | a;
    }

    private static uint Sum(ReadOnlySpan<byte> bytes)
    {
        uint sum = 0;
        foreach (var value in bytes)
        {
            sum += value;
        }

        return sum;
    }
}
