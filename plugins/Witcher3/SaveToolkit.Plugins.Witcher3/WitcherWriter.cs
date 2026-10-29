using System.Buffers.Binary;
using K4os.Compression.LZ4;
using SaveToolkit.Sdk;

namespace SaveToolkit.Plugins.Witcher3;

internal static class WitcherWriter
{
    private const int TableOffset = 16;
    private const int DescriptorSize = 12;
    private const int NativeChunkSize = 1024 * 1024;

    public static byte[] Write(WitcherContainer container, byte[] payload)
    {
        if (payload.AsSpan().SequenceEqual(container.Payload))
        {
            return container.Original.ToArray();
        }

        if (payload.Length > BinaryData.MaxExpandedSize)
        {
            throw new SaveFormatException("Edited Witcher body exceeds the supported size limit.");
        }

        var header = container.Original.AsSpan(0, container.HeaderSize).ToArray();
        var chunkSizes = ChunkSizes(container, payload.Length);
        ValidateHeaderCapacity(container, header, chunkSizes.Count);
        using var output = new MemoryStream();
        output.Write(header);
        var position = 0;
        for (var index = 0; index < chunkSizes.Count; index++)
        {
            var chunk = index < container.Chunks.Count ? container.Chunks[index] : null;
            var expanded = chunkSizes[index];
            var encoded = EncodeChunk(container, chunk, payload.AsSpan(position, expanded), position);
            output.Write(encoded);
            var descriptor = header.AsSpan(TableOffset + index * DescriptorSize, DescriptorSize);
            BinaryPrimitives.WriteInt32LittleEndian(descriptor, encoded.Length);
            BinaryPrimitives.WriteInt32LittleEndian(descriptor[4..], expanded);
            var zeroEnd = chunk is not null && chunk.EndOffset == 0 && index < container.Chunks.Count - 1 ||
                index == chunkSizes.Count - 1 && container.Chunks[^1].EndOffset == 0;
            BinaryPrimitives.WriteInt32LittleEndian(descriptor[8..], zeroEnd ? 0 : checked((int)output.Length));
            position += expanded;
        }

        var last = container.Chunks[^1];
        output.Write(container.Original.AsSpan(last.Offset + last.StoredSize));
        if (output.Length > BinaryData.MaxFileSize)
        {
            throw new SaveFormatException("Edited Witcher container exceeds the supported size limit.");
        }

        output.Position = 0;
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(8), chunkSizes.Count);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(12), container.HeaderSize);
        output.Write(header);
        var bytes = output.ToArray();
        Verify(bytes, payload);
        return bytes;
    }

    private static void ValidateHeaderCapacity(WitcherContainer container, byte[] header, int chunkCount)
    {
        var extraDescriptors = (chunkCount - container.Chunks.Count) * DescriptorSize;
        var oldTableEnd = TableOffset + container.Chunks.Count * DescriptorSize;
        if (extraDescriptors > header.Length - oldTableEnd ||
            header.AsSpan(oldTableEnd, extraDescriptors).ContainsAnyExcept((byte)0))
        {
            throw new SaveFormatException("Additional Witcher chunks would overwrite reserved header data.");
        }

    }

    private static byte[] EncodeChunk(WitcherContainer container, WitcherChunk? chunk, ReadOnlySpan<byte> raw, int position)
    {
        var expanded = raw.Length;
        if (chunk is not null && expanded == chunk.ExpandedSize && position <= container.Payload.Length - expanded &&
            raw.SequenceEqual(container.Payload.AsSpan(position, expanded)))
        {
            return container.Original.AsSpan(chunk.Offset, chunk.StoredSize).ToArray();
        }
        var buffer = new byte[LZ4Codec.MaximumOutputSize(expanded)];
        var stored = LZ4Codec.Encode(raw, buffer, LZ4Level.L12_MAX);
        if (stored <= 0)
        {
            throw new SaveFormatException("Could not LZ4-compress the edited Witcher chunk.");
        }

        // Native writers store incompressible blocks verbatim (equal stored/expanded sizes).
        return stored < expanded ? buffer.AsSpan(0, stored).ToArray() : raw.ToArray();
    }

    private static void Verify(byte[] bytes, byte[] payload)
    {
        var check = WitcherContainer.Parse(bytes);
        if (!check.Payload.AsSpan().SequenceEqual(payload))
        {
            throw new SaveFormatException("Edited Witcher chunks did not reproduce the SAV3 body.");
        }

        _ = Sav3Tree.Parse(check.Payload, Sav3Directory.Parse(check));
    }

    private static IReadOnlyList<int> ChunkSizes(WitcherContainer container, int payloadSize)
    {
        var sizes = container.Chunks.SkipLast(1).Select(c => c.ExpandedSize).ToList();
        var remaining = payloadSize - sizes.Sum();
        if (remaining < 1)
        {
            throw new SaveFormatException("Edited body cannot retain the original chunk boundaries.");
        }

        var lastSize = Math.Max(NativeChunkSize, container.Chunks[^1].ExpandedSize);
        sizes.Add(Math.Min(remaining, lastSize));
        remaining -= sizes[^1];
        while (remaining > 0)
        {
            sizes.Add(Math.Min(remaining, NativeChunkSize));
            remaining -= sizes[^1];
        }

        return sizes;
    }
}
