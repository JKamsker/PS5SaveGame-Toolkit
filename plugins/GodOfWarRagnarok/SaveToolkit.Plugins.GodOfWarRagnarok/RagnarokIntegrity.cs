using System.Buffers.Binary;

namespace SaveToolkit.Plugins.GodOfWarRagnarok;

internal sealed record RagnarokChecksumProbe(string Algorithm, string Coverage, int CoveredBytes, int ComparedWords, int Matches);

internal sealed record RagnarokIntegrityEvidence(IReadOnlyList<RagnarokChecksumProbe> Probes)
{
    public bool HasCandidateMatch => Probes.Any(probe => probe.Matches > 0);
}

/// <summary>Finite, read-only checksum probes. A negative result is evidence for the tested candidates only.</summary>
internal static class RagnarokIntegrity
{
    public static RagnarokIntegrityEvidence Probe(ReadOnlySpan<byte> bytes, RagnarokGrammarEvidence grammar)
    {
        // The final FFFFFFFF word is a structural sentinel, not a checksum target. Including it would
        // manufacture repeated CRC matches because CRC32(FF FF FF FF) is itself a common word.
        var ranges = grammar.Spans.Where(span => span.Length > 0 && span.Kind is not RagnarokSpanKind.Opaque and not RagnarokSpanKind.TrailingSentinel).ToArray();
        var probes = new List<RagnarokChecksumProbe>();
        foreach (var range in Coverage(ranges, bytes.Length))
        {
            var covered = bytes[range.Offset..(range.Offset + range.Length)];
            probes.Add(Compare("CRC32-IEEE", range.Name, covered, bytes, Crc32));
            probes.Add(Compare("Adler32", range.Name, covered, bytes, Adler32));
        }

        return new RagnarokIntegrityEvidence(probes);
    }

    private static IEnumerable<(string Name, int Offset, int Length)> Coverage(IReadOnlyList<RagnarokSpan> spans, int fileLength)
    {
        yield return ("whole-file", 0, fileLength);
        foreach (var span in spans)
        {
            yield return ($"{span.Kind}@0x{span.Offset:X}", span.Offset, span.Length);
        }
    }

    private static RagnarokChecksumProbe Compare(string algorithm, string coverage, ReadOnlySpan<byte> covered,
        ReadOnlySpan<byte> whole, Func<ReadOnlySpan<byte>, uint> calculate)
    {
        var expected = calculate(covered);
        // Do not accidentally compare an aligned EOF sentinel as a checksum target.
        var comparedWords = (whole.Length - sizeof(uint)) / sizeof(uint);
        var matches = 0;
        for (var offset = 0; offset <= whole.Length - 2 * sizeof(uint); offset += sizeof(uint))
        {
            if (BinaryPrimitives.ReadUInt32LittleEndian(whole[offset..]) == expected)
            {
                matches++;
            }
        }

        return new RagnarokChecksumProbe(algorithm, coverage, covered.Length, comparedWords, matches);
    }

    private static uint Crc32(ReadOnlySpan<byte> bytes)
    {
        var crc = uint.MaxValue;
        foreach (var value in bytes)
        {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++)
            {
                crc = (crc >> 1) ^ (0xEDB88320u & (uint)-(int)(crc & 1));
            }
        }

        return ~crc;
    }

    private static uint Adler32(ReadOnlySpan<byte> bytes)
    {
        const uint Modulus = 65_521;
        uint a = 1;
        uint b = 0;
        foreach (var value in bytes)
        {
            a = (a + value) % Modulus;
            b = (b + a) % Modulus;
        }

        return (b << 16) | a;
    }
}
