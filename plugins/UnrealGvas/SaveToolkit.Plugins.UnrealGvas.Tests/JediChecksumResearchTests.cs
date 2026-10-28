using System.Buffers.Binary;
using System.IO.Compression;
using SaveToolkit.Plugins.UnrealGvas.Gvas;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.UnrealGvas.Tests;

/// <summary>Reproducible rejected hypotheses; these tests do not establish the actual checksum algorithm.</summary>
public sealed class JediChecksumResearchTests
{
    private const uint IeeePolynomial = 0xEDB88320;
    private const uint CastagnoliPolynomial = 0x82F63B78;

    [Fact]
    public void CandidateCrcImplementationsMatchCheckVectors()
    {
        Assert.Equal(0xCBF43926U, Crc("123456789"u8, 0, IeeePolynomial));
        Assert.Equal(0xE3069283U, Crc("123456789"u8, 0, CastagnoliPolynomial));
    }

    [Theory]
    [InlineData("SaveGame00", 2580619, 0x253EB7D0U)]
    [InlineData("Backup00", 2471665, 0x3A3A9C96U)]
    public void ActorStreamsValidateButCommonChecksumHypothesesDoNotMatch(string slot, int expectedSize, uint expectedCrc)
    {
        var files = SampleSaves.Load("PPSA02199").AllDiskFiles
            .Where(f => f.Directory.Name == slot && f.FileName == "ue4savegame.dpx.sav").ToArray();
        Assert.Equal(2, files.Length);
        foreach (var file in files)
        {
            var bytes = file.ReadAllBytes();
            var parsed = GvasFile.Parse(bytes);
            var properties = parsed.Properties;
            var actor = properties.Single(n => n.Name == "CompressedActorRecords");
            var crc = properties.Single(n => n.Name == "Crc32");
            var stored = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(crc.Offset));
            Assert.Equal(expectedCrc, stored);
            var count = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(actor.Offset));
            Assert.Equal(actor.Length - 4, count);
            var compressed = bytes.AsSpan(actor.Offset + 4, count).ToArray();
            using var input = new MemoryStream(compressed, writable: false);
            using var zlib = new ZLibStream(input, CompressionMode.Decompress);
            using var output = new MemoryStream();
            zlib.CopyTo(output);
            var plain = output.ToArray();
            Assert.Equal(expectedSize, plain.Length);
            Assert.Equal((Int128)expectedSize, properties.Single(n => n.Name == "UncompressedActorRecordsSize").Scalar!.Value);
            Assert.True(plain.AsSpan(4).StartsWith("ActorRecords\0"u8));
            // The leading Int32 belongs to the FString, not a separate actor count.
            var headerless = GvasFile.ParseHeaderless(plain, parsed.Header);
            Assert.Contains(headerless.Properties, node => node.Name == "ActorRecords");
            Assert.True(GvasFile.Descendants(headerless.Properties).Count() > 1_000);

            var zeroed = bytes.ToArray();
            zeroed.AsSpan(crc.Offset, 4).Clear();
            var payloads = properties.Where(n => n != crc).SelectMany(n => bytes.AsSpan(n.Offset, n.Length).ToArray()).ToArray();
            byte[][] candidates = [bytes, zeroed, compressed, plain, bytes.AsSpan(actor.Offset, actor.Length).ToArray(),
                payloads, plain[..^4], plain[4..], plain[64..], zeroed[1219..], zeroed[..crc.Offset], zeroed[(crc.Offset + 4)..]];
            foreach (var candidate in candidates)
                foreach (var polynomial in new[] { IeeePolynomial, CastagnoliPolynomial })
                    foreach (var seed in new[] { 0U, uint.MaxValue })
                    {
                        var actual = Crc(candidate, seed, polynomial);
                        Assert.NotEqual(stored, actual);
                        Assert.NotEqual(stored, ~actual);
                    }
        }
    }

    private static uint Crc(ReadOnlySpan<byte> bytes, uint seed, uint polynomial)
    {
        Span<uint> table = stackalloc uint[256];
        for (uint i = 0; i < table.Length; i++)
        {
            var value = i;
            for (var bit = 0; bit < 8; bit++) value = (value >> 1) ^ ((value & 1) != 0 ? polynomial : 0);
            table[(int)i] = value;
        }
        var crc = ~seed;
        foreach (var value in bytes) crc = (crc >> 8) ^ table[(byte)(crc ^ value)];
        return ~crc;
    }
}
