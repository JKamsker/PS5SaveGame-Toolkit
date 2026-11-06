using SaveToolkit.Sdk.Crypto;

namespace SaveToolkit.Plugins.BaldursGate3;

internal static class LspkIntegrity
{
    // LSLib's MD5 + 1, with expanded entries in physical write order (confirmed on PS5).
    // This legacy checksum detects accidental changes; it is not authentication.
    internal static byte[] Compute(IEnumerable<byte[]> payloads)
    {
        // IncrementalMd5 also works on browser-wasm, where the platform MD5 is unsupported.
        using var hash = IncrementalMd5.Create();
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

    internal static void Validate(LspkPackage package, Dictionary<string, byte[]>? payloads = null)
    {
        var actual = Compute(package.Entries.OrderBy(e => e.Offset).Select(entry =>
        {
            var payload = package.Extract(entry);
            payloads?.Add(entry.Name, payload);
            return payload;
        }));
        BinaryData.Require(Convert.ToHexString(actual) == package.HeaderDigest,
            "LSPK header digest mismatch: expanded payloads do not match their checksum.");
    }
}
