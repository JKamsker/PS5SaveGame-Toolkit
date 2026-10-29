using K4os.Compression.LZ4;
using SaveToolkit.Sdk;

namespace SaveToolkit.Plugins.Witcher3;

internal sealed record WitcherChunk(int Offset, int StoredSize, int ExpandedSize, int EndOffset);

internal sealed record WitcherContainer(byte[] Original, byte[] Payload, int HeaderSize, IReadOnlyList<WitcherChunk> Chunks)
{
    public static WitcherContainer Parse(byte[] bytes)
    {
        const int tableOffset = 16;
        const int entrySize = 12;
        const int maxChunks = 4096;
        if (bytes.Length < tableOffset || bytes.Length > BinaryData.MaxFileSize || !bytes.AsSpan(0, 8).SequenceEqual("SNFHFZLC"u8))
        {
            throw new SaveFormatException("Not a supported Witcher 3 SNFH/FZLC save.");
        }

        var count = BinaryData.Int32(bytes, 8);
        var headerSize = BinaryData.Int32(bytes, 12);
        if (count is < 1 or > maxChunks || headerSize < tableOffset + count * entrySize || headerSize > bytes.Length)
        {
            throw new SaveFormatException("Invalid Witcher 3 chunk count or data offset.");
        }

        var chunks = new WitcherChunk[count];
        var position = headerSize;
        var expandedTotal = 0;
        for (var index = 0; index < count; index++)
        {
            var entry = tableOffset + index * entrySize;
            var stored = BinaryData.Int32(bytes, entry);
            var expanded = BinaryData.Int32(bytes, entry + 4);
            var end = BinaryData.Int32(bytes, entry + 8);
            if (stored < 1 || stored > bytes.Length - position || expanded < 1 ||
                expanded > BinaryData.MaxExpandedSize - expandedTotal || end != 0 && end != position + stored)
            {
                throw new SaveFormatException($"Invalid Witcher 3 chunk {index} sizes or end offset.");
            }

            chunks[index] = new WitcherChunk(position, stored, expanded, end);
            position += stored;
            expandedTotal += expanded;
        }

        // Validate every compressed stream before trusting any declared output size for allocation.
        foreach (var chunk in chunks)
        {
            if (chunk.StoredSize != chunk.ExpandedSize)
            {
                Lz4BlockValidation.Validate(bytes.AsSpan(chunk.Offset, chunk.StoredSize), chunk.ExpandedSize);
            }
        }

        var payload = new byte[expandedTotal];
        var targetOffset = 0;
        foreach (var chunk in chunks)
        {
            if (chunk.StoredSize == chunk.ExpandedSize)
            {
                bytes.AsSpan(chunk.Offset, chunk.StoredSize).CopyTo(payload.AsSpan(targetOffset));
            }
            else if (LZ4Codec.Decode(bytes.AsSpan(chunk.Offset, chunk.StoredSize), payload.AsSpan(targetOffset, chunk.ExpandedSize)) != chunk.ExpandedSize)
            {
                throw new SaveFormatException("Invalid Witcher 3 LZ4 block or expanded length mismatch.");
            }

            targetOffset += chunk.ExpandedSize;
        }

        return new WitcherContainer(bytes, payload, headerSize, chunks);
    }
}
