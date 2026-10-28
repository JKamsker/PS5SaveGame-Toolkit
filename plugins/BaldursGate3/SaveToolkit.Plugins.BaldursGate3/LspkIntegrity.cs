using System.Security.Cryptography;

namespace SaveToolkit.Plugins.BaldursGate3;

internal static class LspkIntegrity
{
    // LSLib's MD5 + 1, with expanded entries in physical write order (confirmed on PS5).
    // This legacy checksum detects accidental changes; it is not authentication.
    internal static byte[] Compute(IEnumerable<byte[]> payloads)
    {
#pragma warning disable CA5351
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.MD5);
#pragma warning restore CA5351
        foreach (var payload in payloads)
        {
            hash.AppendData(payload);
        }

        var digest = hash.GetHashAndReset();
        for (var i = 0; i < digest.Length; i++)
        {
            digest[i] = unchecked((byte)(digest[i] + 1));
        }

        return digest;
    }

    internal static void Validate(LspkPackage package)
    {
        var actual = Compute(package.Entries.OrderBy(e => e.Offset).Select(package.Extract));
        BinaryData.Require(Convert.ToHexString(actual) == package.HeaderDigest,
            "LSPK header digest mismatch: expanded payloads do not match their checksum.");
    }
}
