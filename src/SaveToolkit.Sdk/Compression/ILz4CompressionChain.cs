namespace SaveToolkit.Sdk.Compression;

/// <summary>
/// A linked-block LZ4 compressor (<see cref="Lz4FastCompressionChain"/> or
/// <see cref="Lz4HighCompressionChain"/>) driven by a frame writer's input ring buffer.
/// </summary>
public interface ILz4CompressionChain
{
    /// <summary>
    /// Compresses <paramref name="length"/> bytes at <paramref name="offset"/> of <paramref name="window"/>,
    /// using earlier blocks of the same window as history. Returns the encoded length, or 0 or less when
    /// <paramref name="target"/> cannot hold the block.
    /// </summary>
    /// <param name="window">The same buffer on every call.</param>
    /// <param name="offset">Start of the block in the window.</param>
    /// <param name="length">Block length.</param>
    /// <param name="target">Destination for the encoded block.</param>
    int Compress(ReadOnlySpan<byte> window, int offset, int length, Span<byte> target);

    /// <summary>
    /// Moves up to <paramref name="length"/> (at most 64 KiB) bytes of history to the start of the window
    /// so the next block can follow it; returns the bytes kept.
    /// </summary>
    /// <param name="window">The same buffer passed to <see cref="Compress"/>.</param>
    /// <param name="length">Requested history length.</param>
    int SaveDictionary(Span<byte> window, int length);
}
