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
        using var workspace = new OodleWorkspace();
        foreach (var chunk in file.Chunks)
        {
            try
            {
                OodleCodec.Decompress(bytes.AsSpan(chunk.Offset + FrameHeaderBytes, chunk.CompressedBytes),
                    plain.AsSpan(position, chunk.UncompressedBytes), workspace, maxBytes: SaveExtensionFile.MaxBytes);
            }
            catch (InvalidDataException ex)
            {
                throw new SaveFormatException($"Invalid Kraken chunk at file offset 0x{chunk.Offset:X}: {ex.Message}", ex);
            }
            position += chunk.UncompressedBytes;
        }
        if (position != total) throw new SaveFormatException("Kraken aggregate length mismatch.");
        return new RecordReader(plain).Read();
    }
}
