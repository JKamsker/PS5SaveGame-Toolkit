namespace SaveToolkit.Sdk.Compression;

/// <summary>
/// Managed LZ4 high-compression (HC, levels 3 to 12) block encoder that produces exactly the bytes of
/// K4os.Compression.LZ4 1.3.8 <c>LZ4Codec.Encode</c> on every platform, including the browser runtime.
/// </summary>
/// <remarks>
/// K4os' own HC encoder corrupts memory under the Mono WebAssembly interpreter (its 256 KiB state struct
/// has field offsets the interpreter truncates to 16 bits; dotnet/runtime#46622), which traps the whole
/// runtime with "memory access out of bounds". Use this encoder for HC levels; K4os decoding and the
/// fast level are unaffected. Ported from K4os (MIT) and lz4 (BSD-2-Clause); see THIRD-PARTY-NOTICES.
/// </remarks>
public static class Lz4HighCompression
{
    /// <summary>Lowest HC level (K4os <c>LZ4Level.L03_HC</c>).</summary>
    public const int MinimumLevel = Lz4HcContext.MinLevel;

    /// <summary>Highest HC level (K4os <c>LZ4Level.L12_MAX</c>).</summary>
    public const int MaximumLevel = Lz4HcContext.MaxLevel;

    /// <summary>Worst-case encoded size of <paramref name="length"/> input bytes (LZ4 compress bound), or 0 when too large.</summary>
    public static int MaximumOutputSize(int length) => Lz4HcContext.CompressBound(length);

    /// <summary>
    /// Encodes one independent LZ4 block. Returns the encoded length, 0 for an empty source, or -1 when
    /// <paramref name="target"/> is too small (the same contract as K4os <c>LZ4Codec.Encode</c>).
    /// </summary>
    /// <param name="source">Bytes to compress.</param>
    /// <param name="target">Destination; <see cref="MaximumOutputSize"/> bytes always suffice.</param>
    /// <param name="level">HC level, <see cref="MinimumLevel"/> to <see cref="MaximumLevel"/>.</param>
    public static int Encode(ReadOnlySpan<byte> source, Span<byte> target, int level)
    {
        if (level is < MinimumLevel or > MaximumLevel)
            throw new ArgumentOutOfRangeException(nameof(level), level, "LZ4 HC levels are 3 to 12.");
        if (source.IsEmpty) return 0;
        var encoded = new Lz4HcContext(level).CompressBlock(source, target);
        return encoded <= 0 ? -1 : encoded;
    }
}

/// <summary>
/// Linked-block LZ4 HC state for frame encoders (K4os <c>LZ4HighChainEncoder</c>'s engine): blocks are
/// compressed in order inside one caller-owned window buffer and may reference up to 64 KiB of history.
/// </summary>
public sealed class Lz4HighCompressionChain
{
    private readonly Lz4HcContext _context;

    /// <summary>Creates a fresh chain; the level is clamped to the HC range as K4os does.</summary>
    /// <param name="level">Requested compression level.</param>
    public Lz4HighCompressionChain(int level) =>
        _context = new Lz4HcContext(Math.Clamp(level, Lz4HighCompression.MinimumLevel, Lz4HighCompression.MaximumLevel));

    /// <summary>
    /// Compresses <paramref name="length"/> bytes at <paramref name="offset"/> of <paramref name="window"/>,
    /// using earlier blocks of the same window as history. Returns the encoded length, or 0 or less on failure.
    /// </summary>
    /// <param name="window">The same buffer on every call.</param>
    /// <param name="offset">Start of the block in the window.</param>
    /// <param name="length">Block length.</param>
    /// <param name="target">Destination for the encoded block.</param>
    public int Compress(ReadOnlySpan<byte> window, int offset, int length, Span<byte> target)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(offset, window.Length - length);
        return _context.Continue(window, offset, length, target);
    }

    /// <summary>
    /// Moves up to <paramref name="length"/> (at most 64 KiB) bytes of history to the start of the window
    /// so the next block can follow it; returns the bytes kept (upstream <c>LZ4_saveDictHC</c>).
    /// </summary>
    /// <param name="window">The same buffer passed to <see cref="Compress"/>.</param>
    /// <param name="length">Requested history length.</param>
    public int SaveDictionary(Span<byte> window, int length) => _context.SaveDict(window, length);
}
