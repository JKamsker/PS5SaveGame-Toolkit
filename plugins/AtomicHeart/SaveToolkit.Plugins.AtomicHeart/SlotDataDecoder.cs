using SaveToolkit.Codecs;
using SaveToolkit.Sdk;

namespace SaveToolkit.Plugins.AtomicHeart;

internal static class SlotDataDecoder
{
    internal const int FrameHeaderBytes = 6 * sizeof(ulong);

    internal static RecordIndex Decode(byte[] bytes, SaveExtensionFile file)
    {
        var total = checked((int)file.Chunks.Sum(c => (long)c.UncompressedBytes));
        // Bound the aggregate before allocating, not only the individual decoder calls.
        if (total > SaveExtensionFile.MaxBytes) throw new SaveFormatException("Decoded SlotData exceeds 128 MiB.");
        var plain = new byte[total];
        var position = 0;
        foreach (var chunk in file.Chunks)
        {
            byte[] decoded;
            try
            {
                decoded = OodleCodec.Decompress(bytes.AsSpan(chunk.Offset + FrameHeaderBytes, chunk.CompressedBytes),
                    chunk.UncompressedBytes, maxBytes: SaveExtensionFile.MaxBytes);
            }
            catch (InvalidDataException ex)
            {
                throw new SaveFormatException($"Invalid Kraken chunk at file offset 0x{chunk.Offset:X}: {ex.Message}", ex);
            }
            if (decoded.Length != chunk.UncompressedBytes) throw new SaveFormatException("Kraken chunk length mismatch.");
            decoded.CopyTo(plain, position);
            position += decoded.Length;
        }
        if (position != total) throw new SaveFormatException("Kraken aggregate length mismatch.");
        return new RecordReader(plain).Read();
    }
}
