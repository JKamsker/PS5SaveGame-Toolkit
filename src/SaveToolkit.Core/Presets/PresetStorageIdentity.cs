using System.Security.Cryptography;

namespace SaveToolkit.Core.Presets;

/// <summary>Bounded durable-file identity for a reviewed import, including concurrent file growth.</summary>
internal sealed class PresetStorageIdentity(PresetStoreOptions options)
{
    internal const string Missing = "missing";
    private const int BufferSize = 8192;
    private string _generation = Guid.NewGuid().ToString("N");
    private string _publishedFingerprint = Missing;

    internal void Publish(string fingerprint)
    {
        _publishedFingerprint = fingerprint;
        _generation = Guid.NewGuid().ToString("N");
    }

    internal string Verify()
    {
        try
        {
            var path = Path.Combine(options.StorageRoot, PresetLibraryStorage.FileName);
            if (_publishedFingerprint != Read(path, options.MaxPayloadBytes)) throw Changed();
            return _generation + ":" + _publishedFingerprint;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new PresetStorageException("Preset storage is temporarily unavailable. Retry the operation.", ex);
        }
    }

    internal static string Fingerprint(ReadOnlySpan<byte> bytes) => Convert.ToHexString(SHA256.HashData(bytes));

    private static string Read(string path, int maxBytes)
    {
        try
        {
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
        catch (FileNotFoundException) { return Missing; }
        catch (DirectoryNotFoundException) { return Missing; }
    }

    private static PresetTransferException Changed() => new("presetLibraryReloadRequired",
        "The preset library changed on disk. Reload the library and preview the file again.", 409);
}
