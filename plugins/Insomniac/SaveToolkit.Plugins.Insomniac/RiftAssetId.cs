using System.Text;

namespace SaveToolkit.Plugins.Insomniac;

/// <summary>Independent bitwise path identifier, checked against PS5 string metadata
/// and inventory config tokens. This is an identity hash, never an integrity hash.</summary>
internal static class RiftAssetId
{
    private const ulong Polynomial = 0xC96C_5795_D787_0F42;
    private const ulong AssetFlag = 0x8000_0000_0000_0000;

    internal static string Hash(string path)
    {
        var normalized = path.Replace('\\', '/').ToLowerInvariant();
        while (normalized.Contains("//", StringComparison.Ordinal))
            normalized = normalized.Replace("//", "/", StringComparison.Ordinal);
        var crc = Polynomial;
        foreach (var value in Encoding.UTF8.GetBytes(normalized))
        {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++)
                crc = (crc >> 1) ^ ((crc & 1) == 0 ? 0 : Polynomial);
        }
        return $"{(crc >> 2) | AssetFlag:x16}";
    }
}
