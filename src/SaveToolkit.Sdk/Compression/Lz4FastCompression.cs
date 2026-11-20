namespace SaveToolkit.Sdk.Compression;

/// <summary>
/// Managed LZ4 fast-level block encoder that produces exactly the bytes of K4os.Compression.LZ4 1.3.8
/// <c>LZ4Codec.Encode</c> at <c>L00_FAST</c> (also <c>L01</c>/<c>L02</c>) in a 64-bit process, on every
/// platform including the browser runtime.
/// </summary>
/// <remarks>
/// K4os selects its 32-bit engine when pointers are 4 bytes, so in 32-bit WebAssembly <c>LZ4Codec.Encode</c>
/// writes valid LZ4 that differs from the native host's bytes for blocks of 64 KiB + 11 bytes and more
/// (and <c>LZ4Stream.Encode</c> frames differ at any size). Use this encoder (and
/// <see cref="Lz4FastCompressionChain"/> for frames) wherever re-encoded output must match native bytes.
/// Ported from K4os (MIT) and lz4 (BSD-2-Clause); see THIRD-PARTY-NOTICES.
/// </remarks>
public static class Lz4FastCompression
{
    /// <summary>Worst-case encoded size of <paramref name="length"/> input bytes (LZ4 compress bound), or 0 when too large.</summary>
    public static int MaximumOutputSize(int length) => Lz4HcContext.CompressBound(length);

    /// <summary>
    /// Encodes one independent LZ4 block. Returns the encoded length, 0 for an empty source, or -1 when
    /// <paramref name="target"/> is too small (the same contract as K4os <c>LZ4Codec.Encode</c>).
    /// </summary>
    /// <param name="source">Bytes to compress.</param>
    /// <param name="target">Destination; <see cref="MaximumOutputSize"/> bytes always suffice.</param>
    public static int Encode(ReadOnlySpan<byte> source, Span<byte> target)
    {
        if (source.IsEmpty) return 0;
        var encoded = new Lz4FastContext().CompressBlock(source, target);
        return encoded <= 0 ? -1 : encoded;
    }
}

/// <summary>
/// Linked-block LZ4 fast-level state for frame encoders (K4os <c>LZ4FastChainEncoder</c>'s engine,
/// 64-bit variant): blocks are compressed in order inside one caller-owned window buffer and may
/// reference up to 64 KiB of history.
/// </summary>
public sealed class Lz4FastCompressionChain : ILz4CompressionChain
{
    private readonly Lz4FastContext _context = new();

    /// <inheritdoc />
    public int Compress(ReadOnlySpan<byte> window, int offset, int length, Span<byte> target)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(offset, window.Length - length);
        return _context.Continue(window, offset, length, target);
    }

    /// <inheritdoc />
    public int SaveDictionary(Span<byte> window, int length) => _context.SaveDict(window, length);
}
