using SaveToolkit.Plugins.UnrealGvas.Gvas;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.UnrealGvas.Tests;

/// <summary>Candidate-vs-alternative vectors for Sackboy General integrity research.</summary>
public sealed class SackboyChecksumResearchTests
{
    [Fact]
    public void StandardVectorsRemainIndependentAndRejectTheStoredGeneralChecksums()
    {
        Assert.Equal(0xCBF43926U, Crc32("123456789"u8));
        Assert.Equal(0xE3069283U, Crc32C("123456789"u8));
        Assert.Equal(0xBB86B11CU, Fnv1A("123456789"u8));

        var files = SampleSaves.Load("PPSA01288").AllDiskFiles.Where(f => f.FileName == "General")
            .DistinctBy(f => f.Sha256).ToArray();
        Assert.Equal(2, files.Length);
        foreach (var file in files)
        {
            var bytes = file.ReadAllBytes();
            var parsed = GvasFile.Parse(bytes);
            var checksum = GvasFile.Descendants(parsed.Properties).Single(node => node.Name == "Checksum");
            var stored = (uint)(Int128)checksum.Scalar!.Value;
            var zeroed = bytes.ToArray();
            zeroed.AsSpan(checksum.Offset, sizeof(uint)).Clear();

            var candidates = new Dictionary<string, uint>
            {
                ["IEEE CRC32 whole file with stored field zeroed"] = Crc32(zeroed),
                ["CRC32C whole file with stored field zeroed"] = Crc32C(zeroed),
                ["FNV-1a whole file with stored field zeroed"] = Fnv1A(zeroed),
                ["Adler32 whole file with stored field zeroed"] = Adler32(zeroed),
                ["byte sum whole file with stored field zeroed"] = unchecked((uint)zeroed.Sum(value => value)),
            };
            Assert.All(candidates, candidate => Assert.NotEqual(stored, candidate.Value));
        }
    }

    private static uint Crc32(ReadOnlySpan<byte> bytes) => Crc(bytes, 0xEDB88320U);
    private static uint Crc32C(ReadOnlySpan<byte> bytes) => Crc(bytes, 0x82F63B78U);

    private static uint Crc(ReadOnlySpan<byte> bytes, uint polynomial)
    {
        Span<uint> table = stackalloc uint[256];
        for (uint i = 0; i < table.Length; i++)
        {
            var value = i;
            for (var bit = 0; bit < 8; bit++) value = (value >> 1) ^ ((value & 1) != 0 ? polynomial : 0);
            table[(int)i] = value;
        }
        var crc = uint.MaxValue;
        foreach (var value in bytes) crc = (crc >> 8) ^ table[(byte)(crc ^ value)];
        return ~crc;
    }

    private static uint Fnv1A(ReadOnlySpan<byte> bytes)
    {
        const uint offset = 2166136261;
        const uint prime = 16777619;
        var hash = offset;
        foreach (var value in bytes) hash = unchecked((hash ^ value) * prime);
        return hash;
    }

    private static uint Adler32(ReadOnlySpan<byte> bytes)
    {
        const uint modulus = 65521;
        uint low = 1, high = 0;
        foreach (var value in bytes)
        {
            low = (low + value) % modulus;
            high = (high + low) % modulus;
        }
        return (high << 16) | low;
    }
}
