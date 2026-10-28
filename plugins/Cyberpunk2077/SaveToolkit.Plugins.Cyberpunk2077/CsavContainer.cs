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

    public byte[] Rebuild(ReadOnlySpan<byte> editedPayload, KeyValuePair<int, byte[]>? replacement = null)
    {
        if (editedPayload.Length != Payload.Length)
        {
            throw new SaveFormatException("CSAV patches must retain the original expanded size; use a node replacement for resizing.");
        }

        return CsavWriter.Rebuild(this, editedPayload.ToArray(), replacement);
    }
}
