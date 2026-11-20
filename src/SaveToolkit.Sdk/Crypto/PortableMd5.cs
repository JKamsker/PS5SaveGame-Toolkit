using System.Runtime.Versioning;
using System.Security.Cryptography;

namespace SaveToolkit.Sdk.Crypto;

/// <summary>
/// MD5 (RFC 1321) that works on every runtime the toolkit ships on. Natively it calls the platform
/// <see cref="MD5"/>; on browser-wasm, where .NET has no MD5, it uses a managed implementation written
/// from RFC 1321. Both produce identical digests. Use it only where a save format requires MD5: it is
/// a legacy checksum, not a security primitive.
/// </summary>
public static class PortableMd5
{
    /// <summary>The size of an MD5 digest in bytes.</summary>
    public const int HashSizeInBytes = ManagedMd5.HashSize;

    /// <summary>True when <see cref="HashData(ReadOnlySpan{byte})"/> uses the managed implementation (browser-wasm).</summary>
    public static bool UsesManagedImplementation => OperatingSystem.IsBrowser();

    /// <summary>Computes the MD5 digest of <paramref name="source"/>.</summary>
    public static byte[] HashData(ReadOnlySpan<byte> source)
    {
        var digest = new byte[HashSizeInBytes];
        HashData(source, digest);
        return digest;
    }

    /// <summary>Writes the MD5 digest of <paramref name="source"/> to <paramref name="destination"/>.</summary>
    /// <returns>The number of bytes written (<see cref="HashSizeInBytes"/>).</returns>
    /// <exception cref="ArgumentException"><paramref name="destination"/> is shorter than <see cref="HashSizeInBytes"/>.</exception>
    public static int HashData(ReadOnlySpan<byte> source, Span<byte> destination)
    {
        if (destination.Length < HashSizeInBytes)
        {
            throw new ArgumentException("Destination is too short for an MD5 digest.", nameof(destination));
        }

        if (OperatingSystem.IsBrowser())
        {
            var hash = new ManagedMd5();
            hash.Append(source);
            hash.Finish(destination);
            return HashSizeInBytes;
        }

        return PlatformHash(source, destination);
    }

    /// <summary>
    /// Computes the digest with the managed RFC 1321 implementation on every runtime. Exists so the
    /// browser code path can be verified natively; callers should use <see cref="HashData(ReadOnlySpan{byte})"/>.
    /// </summary>
    public static byte[] HashDataManaged(ReadOnlySpan<byte> source) => ManagedMd5.HashData(source);

    [UnsupportedOSPlatform("browser")]
    private static int PlatformHash(ReadOnlySpan<byte> source, Span<byte> destination)
    {
#pragma warning disable CA5351 // MD5 is required by save formats for compatibility, never for security.
        return MD5.HashData(source, destination);
#pragma warning restore CA5351
    }
}
