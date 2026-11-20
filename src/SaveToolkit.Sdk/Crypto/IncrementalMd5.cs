using System.Runtime.Versioning;
using System.Security.Cryptography;

namespace SaveToolkit.Sdk.Crypto;

/// <summary>
/// Incremental MD5 (RFC 1321) for data that arrives in pieces; the portable counterpart of
/// <c>IncrementalHash.CreateHash(HashAlgorithmName.MD5)</c>. Natively it wraps <see cref="IncrementalHash"/>;
/// on browser-wasm it uses the managed implementation. Not thread-safe.
/// </summary>
public sealed class IncrementalMd5 : IDisposable
{
    private readonly IncrementalHash? _platform;
    private readonly ManagedMd5? _managed;
    private bool _disposed;

    private IncrementalMd5(IncrementalHash? platform, ManagedMd5? managed)
    {
        _platform = platform;
        _managed = managed;
    }

    /// <summary>True when this instance uses the managed implementation.</summary>
    public bool IsManaged => _managed is not null;

    /// <summary>Creates an incremental MD5 using the platform implementation where one exists.</summary>
    public static IncrementalMd5 Create() =>
        OperatingSystem.IsBrowser() ? CreateManaged() : new IncrementalMd5(CreatePlatform(), null);

    /// <summary>
    /// Creates an incremental MD5 that always uses the managed RFC 1321 implementation. Exists so the
    /// browser code path can be verified natively; callers should use <see cref="Create"/>.
    /// </summary>
    public static IncrementalMd5 CreateManaged() => new(null, new ManagedMd5());

    /// <summary>Appends <paramref name="data"/> to the running digest.</summary>
    public void AppendData(ReadOnlySpan<byte> data)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_managed is not null)
        {
            _managed.Append(data);
        }
        else
        {
            _platform!.AppendData(data);
        }
    }

    /// <summary>Returns the digest of everything appended since creation or the last reset, then resets.</summary>
    public byte[] GetHashAndReset()
    {
        var digest = new byte[PortableMd5.HashSizeInBytes];
        GetHashAndReset(digest);
        return digest;
    }

    /// <summary>Writes the digest to <paramref name="destination"/> and resets.</summary>
    /// <returns>The number of bytes written (<see cref="PortableMd5.HashSizeInBytes"/>).</returns>
    /// <exception cref="ArgumentException"><paramref name="destination"/> is too short.</exception>
    public int GetHashAndReset(Span<byte> destination)
    {
        if (destination.Length < PortableMd5.HashSizeInBytes)
        {
            throw new ArgumentException("Destination is too short for an MD5 digest.", nameof(destination));
        }

        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_managed is not null)
        {
            _managed.Finish(destination);
            return PortableMd5.HashSizeInBytes;
        }

        return _platform!.GetHashAndReset(destination);
    }

    /// <summary>Releases the platform hash, if any. Both paths throw <see cref="ObjectDisposedException"/> afterwards.</summary>
    public void Dispose()
    {
        _disposed = true;
        _platform?.Dispose();
    }

    [UnsupportedOSPlatform("browser")]
    private static IncrementalHash CreatePlatform()
    {
#pragma warning disable CA5351 // MD5 is required by save formats for compatibility, never for security.
        return IncrementalHash.CreateHash(HashAlgorithmName.MD5);
#pragma warning restore CA5351
    }
}
