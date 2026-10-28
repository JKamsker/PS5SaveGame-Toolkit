using System.Buffers.Binary;
using System.Security.Cryptography;

namespace SaveToolkit.Plugins.GodOfWarRagnarok;

internal sealed record RagnarokDigestProbe(string Algorithm, string Coverage, int Offset, int Length,
    string Digest, IReadOnlyList<int> MatchingOffsets);

/// <summary>Finite unkeyed digest hypotheses. No key is available, so this cannot test or exclude a MAC.</summary>
internal static class RagnarokAuthentication
{
    private const uint CastagnoliReflectedPolynomial = 0x82F63B78;
    private const int MaximumReportedMatches = 16;
    private static readonly uint[] CrcTable = BuildCrcTable();

    public static IReadOnlyList<RagnarokDigestProbe> Probe(ReadOnlySpan<byte> bytes, RagnarokRecordIndex index)
    {
        var probes = new List<RagnarokDigestProbe>();
        foreach (var (name, offset, length) in Coverage(bytes.Length, index))
        {
            if (length <= 0)
            {
                continue;
            }

            var covered = bytes.Slice(offset, length);
            foreach (var algorithm in new[] { "CRC32C", "MD5", "SHA-1", "SHA-256", "SHA-512" })
            {
                var digest = Calculate(algorithm, covered);
                probes.Add(new(algorithm, name, offset, length, Convert.ToHexString(digest), Find(bytes, digest)));
                if (algorithm == "CRC32C")
                {
                    Array.Reverse(digest);
                    probes.Add(new("CRC32C-LE", name, offset, length, Convert.ToHexString(digest), Find(bytes, digest)));
                }
            }
        }

        return probes;
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "CA5351", Justification = "Read-only legacy digest identification; never used to authenticate or protect data.")]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "CA5350", Justification = "Read-only legacy digest identification; never used to authenticate or protect data.")]
    internal static byte[] Calculate(string algorithm, ReadOnlySpan<byte> bytes) => algorithm switch
    {
        "CRC32C" => CrcBytes(bytes),
        "MD5" => MD5.HashData(bytes),
        "SHA-1" => SHA1.HashData(bytes),
        "SHA-256" => SHA256.HashData(bytes),
        "SHA-512" => SHA512.HashData(bytes),
        _ => throw new ArgumentException("Unsupported digest candidate.", nameof(algorithm)),
    };

    internal static IReadOnlyList<int> Find(ReadOnlySpan<byte> bytes, ReadOnlySpan<byte> digest)
    {
        var result = new List<int>();
        // Check every byte alignment, including unaligned trailing fields and the final fitting byte.
        var cursor = 0;
        while (cursor <= bytes.Length - digest.Length && result.Count < MaximumReportedMatches)
        {
            var relative = bytes[cursor..].IndexOf(digest);
            if (relative < 0)
            {
                break;
            }

            cursor += relative;
            result.Add(cursor);
            cursor++;
        }

        return result;
    }

    private static IEnumerable<(string Name, int Offset, int Length)> Coverage(int length, RagnarokRecordIndex index)
    {
        yield return ("whole-file", 0, length);
        yield return ("without-EOF-sentinel", 0, length - sizeof(uint));
        yield return ("after-timestamp", sizeof(ulong), length - sizeof(ulong) - sizeof(uint));
        yield return ("after-outer-header", RagnarokFormat.HeaderSize, length - RagnarokFormat.HeaderSize - sizeof(uint));
        yield return ("after-metadata", RagnarokFormat.DirectoryMarkerOffset, length - RagnarokFormat.DirectoryMarkerOffset - sizeof(uint));
        yield return ("metadata", RagnarokFormat.MetadataOffset, RagnarokFormat.MetadataSize);
        yield return ("directory-arrays", RagnarokFormat.DirectoryItemsOffset,
            index.SecondaryOffset + index.SecondaryCount * sizeof(ulong) - RagnarokFormat.DirectoryItemsOffset);
        if (index.Nested.Wallets.Count > 0)
        {
            yield return ("wallet-block-payload", index.WalletOffset, index.Nested.WalletBlockEnd - index.WalletOffset);
            yield return ("wallet-block-with-length", index.Nested.WalletBlockOffset, index.Nested.WalletBlockEnd - index.Nested.WalletBlockOffset);
            yield return ("flag-block-with-length", index.Nested.FlagsOffset, index.Nested.FlagsEnd - index.Nested.FlagsOffset);
        }

        yield return ("hero-resource-rows", index.WalletOffset + RagnarokFormat.WalletHeaderSize,
            index.WalletCount * RagnarokFormat.WalletRowSize);
    }

    private static byte[] CrcBytes(ReadOnlySpan<byte> bytes)
    {
        var crc = uint.MaxValue;
        foreach (var value in bytes)
        {
            crc = CrcTable[(crc ^ value) & byte.MaxValue] ^ (crc >> 8);
        }

        var digest = new byte[sizeof(uint)];
        BinaryPrimitives.WriteUInt32BigEndian(digest, ~crc);
        return digest;
    }

    private static uint[] BuildCrcTable()
    {
        var table = new uint[byte.MaxValue + 1];
        for (uint i = 0; i < table.Length; i++)
        {
            var crc = i;
            for (var bit = 0; bit < 8; bit++)
            {
                crc = (crc >> 1) ^ ((crc & 1) == 0 ? 0 : CastagnoliReflectedPolynomial);
            }

            table[i] = crc;
        }

        return table;
    }
}
