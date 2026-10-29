using System.Security.Cryptography;

namespace SaveToolkit.Core.Presets;

/// <summary>Bounded durable-file identity for a reviewed import, including concurrent file growth.</summary>
internal static class PresetStorageIdentity
{
    private const int BufferSize = 8192;

    internal static string Read(string path, int maxBytes)
    {
        if (!File.Exists(path)) return "missing";
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        if (stream.Length > maxBytes) throw Changed();
        var buffer = new byte[BufferSize];
        long total = 0;
        while (true)
        {
            var count = stream.Read(buffer, 0, (int)Math.Min(buffer.Length, (long)maxBytes + 1 - total));
            if (count == 0) return Convert.ToHexString(hash.GetHashAndReset());
            total += count;
            if (total > maxBytes) throw Changed();
            hash.AppendData(buffer.AsSpan(0, count));
        }
    }

    private static PresetTransferException Changed() => new("stalePresetImport",
        "Preset storage changed or exceeds its limit. Reload the toolkit before importing.", 409);
}
