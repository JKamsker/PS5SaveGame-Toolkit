using System.Runtime.Versioning;
using System.Security.Cryptography;

namespace SaveToolkit.Sdk.Crypto;

/// <summary>
/// AES (FIPS-197) with one key, for save formats that need raw ECB or CBC without padding, on every
/// runtime the toolkit ships on. Natively it uses the platform <see cref="Aes"/>; on browser-wasm,
/// where <c>Aes.Create</c> throws <see cref="PlatformNotSupportedException"/>, it uses a managed
/// implementation written from FIPS-197. Modes follow NIST SP 800-38A. Both paths produce identical
/// output. Lengths must be whole 16-byte blocks; <c>source</c> and <c>destination</c> may be the same
/// buffer but must not otherwise overlap. Not thread-safe; dispose it when done.
/// </summary>
public sealed class PortableAes : IDisposable
{
    /// <summary>The AES block size in bytes.</summary>
    public const int BlockSizeInBytes = ManagedAes.BlockSize;

    // Bounds the platform path's staging buffer; whole blocks only.
    private const int PlatformChunkSize = 4096;

    private readonly ManagedAes? _managed;
    private readonly Aes? _platform;
    private ICryptoTransform? _ecbEncryptor;
    private ICryptoTransform? _ecbDecryptor;
    private byte[]? _staging;
    private bool _disposed;

    private PortableAes(ManagedAes? managed, Aes? platform)
    {
        _managed = managed;
        _platform = platform;
    }

    /// <summary>True when <see cref="Create"/> returns the managed implementation (browser-wasm).</summary>
    public static bool UsesManagedImplementation => OperatingSystem.IsBrowser();

    /// <summary>True when this instance uses the managed implementation.</summary>
    public bool IsManaged => _managed is not null;

    /// <summary>Creates a cipher for <paramref name="key"/> (16, 24 or 32 bytes), using the platform AES where available.</summary>
    /// <exception cref="ArgumentException">The key length is not 16, 24 or 32 bytes.</exception>
    public static PortableAes Create(ReadOnlySpan<byte> key)
    {
        ValidateKey(key);
        return OperatingSystem.IsBrowser() ? CreateManaged(key) : new PortableAes(null, CreatePlatform(key));
    }

    /// <summary>
    /// Creates a cipher that always uses the managed FIPS-197 implementation. Exists so the browser code
    /// path can be verified natively; callers should use <see cref="Create"/>.
    /// </summary>
    /// <exception cref="ArgumentException">The key length is not 16, 24 or 32 bytes.</exception>
    public static PortableAes CreateManaged(ReadOnlySpan<byte> key)
    {
        ValidateKey(key);
        return new PortableAes(new ManagedAes(key), null);
    }

    /// <summary>Encrypts whole blocks independently (ECB, no padding).</summary>
    public void EncryptEcb(ReadOnlySpan<byte> source, Span<byte> destination) => Ecb(source, destination, encrypt: true);

    /// <summary>Decrypts whole blocks independently (ECB, no padding).</summary>
    public void DecryptEcb(ReadOnlySpan<byte> source, Span<byte> destination) => Ecb(source, destination, encrypt: false);

    /// <summary>Encrypts whole blocks in CBC mode with a 16-byte <paramref name="iv"/>, no padding.</summary>
    public void EncryptCbc(ReadOnlySpan<byte> iv, ReadOnlySpan<byte> source, Span<byte> destination)
    {
        Validate(source, destination);
        ValidateIv(iv);
        if (_managed is null)
        {
            PlatformCbc(iv, source, destination, encrypt: true);
            return;
        }

        Span<byte> chain = stackalloc byte[BlockSizeInBytes];
        iv.CopyTo(chain);
        for (var offset = 0; offset < source.Length; offset += BlockSizeInBytes)
        {
            Xor(chain, source.Slice(offset, BlockSizeInBytes));
            var output = destination.Slice(offset, BlockSizeInBytes);
            _managed.EncryptBlock(chain, output);
            output.CopyTo(chain);
        }
    }

    /// <summary>Decrypts whole blocks in CBC mode with a 16-byte <paramref name="iv"/>, no padding.</summary>
    public void DecryptCbc(ReadOnlySpan<byte> iv, ReadOnlySpan<byte> source, Span<byte> destination)
    {
        Validate(source, destination);
        ValidateIv(iv);
        if (_managed is null)
        {
            PlatformCbc(iv, source, destination, encrypt: false);
            return;
        }

        Span<byte> chain = stackalloc byte[BlockSizeInBytes];
        Span<byte> ciphertext = stackalloc byte[BlockSizeInBytes];
        iv.CopyTo(chain);
        for (var offset = 0; offset < source.Length; offset += BlockSizeInBytes)
        {
            // Keep the ciphertext block before an in-place write overwrites it.
            source.Slice(offset, BlockSizeInBytes).CopyTo(ciphertext);
            var output = destination.Slice(offset, BlockSizeInBytes);
            _managed.DecryptBlock(ciphertext, output);
            Xor(output, chain);
            ciphertext.CopyTo(chain);
        }
    }

    /// <summary>Releases the platform cipher, if any.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _ecbEncryptor?.Dispose();
        _ecbDecryptor?.Dispose();
        _platform?.Dispose();
    }

    private void Ecb(ReadOnlySpan<byte> source, Span<byte> destination, bool encrypt)
    {
        Validate(source, destination);
        if (_managed is null)
        {
            PlatformEcb(source, destination, encrypt);
            return;
        }

        for (var offset = 0; offset < source.Length; offset += BlockSizeInBytes)
        {
            var input = source.Slice(offset, BlockSizeInBytes);
            var output = destination.Slice(offset, BlockSizeInBytes);
            if (encrypt)
            {
                _managed.EncryptBlock(input, output);
            }
            else
            {
                _managed.DecryptBlock(input, output);
            }
        }
    }

    private void PlatformEcb(ReadOnlySpan<byte> source, Span<byte> destination, bool encrypt)
    {
        // One reusable ECB transform per direction: no per-call cipher setup, so many small calls stay cheap.
        var transform = encrypt
            ? _ecbEncryptor ??= _platform!.CreateEncryptor()
            : _ecbDecryptor ??= _platform!.CreateDecryptor();
        if (_staging is null || _staging.Length < Math.Min(source.Length, PlatformChunkSize))
        {
            _staging = new byte[Math.Min(source.Length, PlatformChunkSize)];
        }

        for (var offset = 0; offset < source.Length; offset += PlatformChunkSize)
        {
            var length = Math.Min(PlatformChunkSize, source.Length - offset);
            source.Slice(offset, length).CopyTo(_staging);
            if (transform.TransformBlock(_staging, 0, length, _staging, 0) != length)
            {
                throw new CryptographicException("The platform AES transform returned an unexpected length.");
            }

            _staging.AsSpan(0, length).CopyTo(destination[offset..]);
        }
    }

    private void PlatformCbc(ReadOnlySpan<byte> iv, ReadOnlySpan<byte> source, Span<byte> destination, bool encrypt)
    {
        // Stage an in-place call so the one-shot API never sees overlapping buffers.
        var input = source.Overlaps(destination) ? source.ToArray() : source;
        var written = encrypt
            ? _platform!.EncryptCbc(input, iv, destination, PaddingMode.None)
            : _platform!.DecryptCbc(input, iv, destination, PaddingMode.None);
        if (written != source.Length)
        {
            throw new CryptographicException("The platform AES transform returned an unexpected length.");
        }
    }

    private void Validate(ReadOnlySpan<byte> source, Span<byte> destination)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (source.Length % BlockSizeInBytes != 0)
        {
            throw new ArgumentException("AES input must be a whole number of 16-byte blocks.", nameof(source));
        }

        if (destination.Length < source.Length)
        {
            throw new ArgumentException("Destination is shorter than the input.", nameof(destination));
        }

        if (source.Overlaps(destination, out var elementOffset) && elementOffset != 0)
        {
            throw new ArgumentException("Source and destination must be the same buffer or must not overlap.", nameof(destination));
        }
    }

    private static void ValidateKey(ReadOnlySpan<byte> key)
    {
        if (key.Length is not (16 or 24 or 32))
        {
            throw new ArgumentException("AES keys must be 16, 24 or 32 bytes.", nameof(key));
        }
    }

    private static void ValidateIv(ReadOnlySpan<byte> iv)
    {
        if (iv.Length != BlockSizeInBytes)
        {
            throw new ArgumentException("The AES CBC IV must be 16 bytes.", nameof(iv));
        }
    }

    private static void Xor(Span<byte> target, ReadOnlySpan<byte> value)
    {
        for (var i = 0; i < target.Length; i++)
        {
            target[i] ^= value[i];
        }
    }

    [UnsupportedOSPlatform("browser")]
    private static Aes CreatePlatform(ReadOnlySpan<byte> key)
    {
        var aes = Aes.Create();
        aes.Key = key.ToArray();
        aes.Mode = CipherMode.ECB;
        aes.Padding = PaddingMode.None;
        return aes;
    }
}
