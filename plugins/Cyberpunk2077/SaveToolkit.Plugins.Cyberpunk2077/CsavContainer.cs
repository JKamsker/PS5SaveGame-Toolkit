using System.Buffers.Binary;
using K4os.Compression.LZ4;
using SaveToolkit.Sdk;

namespace SaveToolkit.Plugins.Cyberpunk2077;

internal sealed record CsavChunk(int Offset, int StoredSize, int ExpandedSize, int PayloadOffset, bool Compressed);

internal sealed class CsavContainer
{
    private const int CountOffset = 29;
    private const int TableOffset = 33;
    private const int EntrySize = 12;
    private const int WrapperSize = 8;

    private CsavContainer(byte[] original, byte[] payload, CsavChunk[] chunks, int directoryOffset)
    {
        Original = original;
        Payload = payload;
        Chunks = chunks;
        DirectoryOffset = directoryOffset;
    }

    public byte[] Original { get; }
    public byte[] Payload { get; }
    public IReadOnlyList<CsavChunk> Chunks { get; }
    public int DirectoryOffset { get; }
    public int DataOffset => Chunks[0].Offset;
    public int SaveVersion => BinaryData.Int32(Original, 4);
    public int GameVersion => BinaryData.Int32(Original, 8);
    public int ArchiveVersion => BinaryData.Int32(Original, 21);

    public static CsavContainer Parse(byte[] bytes)
    {
        if (bytes.Length < TableOffset + EntrySize + WrapperSize + 8 || bytes.Length > BinaryData.MaxFileSize ||
            !bytes.AsSpan(0, 4).SequenceEqual("VASC"u8) || !bytes.AsSpan(25, 4).SequenceEqual("FZLC"u8) ||
            !bytes.AsSpan(bytes.Length - 4).SequenceEqual("ENOD"u8))
        {
            throw new SaveFormatException("Not a supported Cyberpunk CSAV/LZ4 save.");
        }

        var count = BinaryData.Int32(bytes, CountOffset);
        const int maxChunks = 4096;
        if (count is < 1 or > maxChunks)
        {
            throw new SaveFormatException("Invalid CSAV chunk count.");
        }

        var directoryOffset = BinaryData.Int32(bytes, bytes.Length - 8);
        if (directoryOffset < TableOffset + count * EntrySize || directoryOffset > bytes.Length - 12 ||
            !BinaryData.Slice(bytes, directoryOffset, 4).SequenceEqual("EDON"u8))
        {
            throw new SaveFormatException("Invalid CSAV node directory/footer offset.");
        }

        var chunks = new CsavChunk[count];
        var previousEnd = TableOffset + count * EntrySize;
        var total = 0;
        for (var index = 0; index < count; index++)
        {
            var entry = TableOffset + index * EntrySize;
            var offset = BinaryData.Int32(bytes, entry);
            var stored = BinaryData.Int32(bytes, entry + 4);
            var expanded = BinaryData.Int32(bytes, entry + 8);
            if (offset < previousEnd || stored < 1 || offset > directoryOffset - stored || expanded < 1 ||
                expanded > BinaryData.MaxExpandedSize - total)
            {
                throw new SaveFormatException($"Invalid CSAV chunk {index} bounds or expanded size.");
            }

            var raw = BinaryData.Slice(bytes, offset, stored);
            var compressed = raw.StartsWith("4ZLX"u8);
            if (compressed && (stored <= WrapperSize || BinaryData.Int32(raw, 4) != expanded) ||
                !compressed && stored != expanded)
            {
                throw new SaveFormatException($"CSAV chunk {index} length declarations disagree.");
            }

            chunks[index] = new CsavChunk(offset, stored, expanded, total, compressed);
            previousEnd = offset + stored;
            total += expanded;
        }

        var payload = new byte[total];
        foreach (var chunk in chunks)
        {
            var source = bytes.AsSpan(chunk.Offset, chunk.StoredSize);
            var target = payload.AsSpan(chunk.PayloadOffset, chunk.ExpandedSize);
            if (chunk.Compressed)
            {
                if (LZ4Codec.Decode(source[WrapperSize..], target) != chunk.ExpandedSize)
                {
                    throw new SaveFormatException("Invalid CSAV LZ4 block or expanded length mismatch.");
                }
            }
            else
            {
                source.CopyTo(target);
            }
        }

        return new CsavContainer(bytes, payload, chunks, directoryOffset);
    }

    public byte[] Rebuild(ReadOnlySpan<byte> editedPayload)
    {
        if (editedPayload.Length != Payload.Length)
        {
            throw new SaveFormatException("CSAV editing cannot change the expanded payload size.");
        }

        if (editedPayload.SequenceEqual(Payload))
        {
            return Original.ToArray();
        }

        using var output = new MemoryStream();
        output.Write(Original.AsSpan(0, DataOffset));
        var offsets = new int[Chunks.Count];
        var sizes = new int[Chunks.Count];
        Span<byte> size = stackalloc byte[sizeof(int)];
        for (var index = 0; index < Chunks.Count; index++)
        {
            var chunk = Chunks[index];
            offsets[index] = checked((int)output.Position);
            var edited = editedPayload.Slice(chunk.PayloadOffset, chunk.ExpandedSize);
            if (edited.SequenceEqual(Payload.AsSpan(chunk.PayloadOffset, chunk.ExpandedSize)))
            {
                output.Write(Original.AsSpan(chunk.Offset, chunk.StoredSize));
            }
            else if (!chunk.Compressed)
            {
                output.Write(edited);
            }
            else
            {
                var buffer = new byte[LZ4Codec.MaximumOutputSize(edited.Length)];
                var length = LZ4Codec.Encode(edited, buffer);
                if (length <= 0)
                {
                    throw new SaveFormatException("Could not encode the edited CSAV LZ4 block.");
                }

                output.Write("4ZLX"u8);
                BinaryPrimitives.WriteInt32LittleEndian(size, chunk.ExpandedSize);
                output.Write(size);
                output.Write(buffer.AsSpan(0, length));
            }

            sizes[index] = checked((int)output.Position) - offsets[index];
            var originalEnd = chunk.Offset + chunk.StoredSize;
            var gapEnd = index + 1 < Chunks.Count ? Chunks[index + 1].Offset : DirectoryOffset;
            output.Write(Original.AsSpan(originalEnd, gapEnd - originalEnd));
        }

        var directoryOffset = checked((int)output.Position);
        output.Write(Original.AsSpan(DirectoryOffset));
        var result = output.ToArray();
        for (var index = 0; index < Chunks.Count; index++)
        {
            BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(TableOffset + index * EntrySize), offsets[index]);
            BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(TableOffset + index * EntrySize + 4), sizes[index]);
        }

        BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(result.Length - 8), directoryOffset);
        var reopened = Parse(result);
        if (!reopened.Payload.AsSpan().SequenceEqual(editedPayload))
        {
            throw new SaveFormatException("Edited CSAV failed decompression verification.");
        }

        var tree = CsavTree.Parse(reopened);
        _ = InventoryReader.Read(reopened, tree);
        foreach (var node in tree.Nodes.Where(node => node.Name == "FactsTable"))
        {
            _ = FactsReader.Read(BinaryData.Slice(reopened.Payload, node.Offset, node.Size));
        }
        return result;
    }
}
