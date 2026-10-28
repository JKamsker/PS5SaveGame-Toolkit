using System.Buffers.Binary;
using System.Security.Cryptography;
using SaveToolkit.Codecs;
using SaveToolkit.Sdk;

namespace SaveToolkit.Plugins.AtomicHeart;

internal static class SlotDataWriter
{
    internal static byte[] Write(byte[] original, SaveExtensionFile file, byte[] originalPlain, byte[] plain)
    {
        if (originalPlain.AsSpan().SequenceEqual(plain)) return original.ToArray();
        using var frames = new MemoryStream();
        using (var writer = new BinaryWriter(frames, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            var position = 0;
            foreach (var chunk in file.Chunks)
            {
                if (position >= plain.Length) break;
                var length = Math.Min(chunk.UncompressedBytes, plain.Length - position);
                WriteChunk(writer, original, chunk, originalPlain, plain, position, length);
                position += length;
            }
            while (position < plain.Length)
            {
                var length = Math.Min(SaveExtensionFile.ChunkSize, plain.Length - position);
                WriteStored(writer, plain.AsSpan(position, length));
                position += length;
            }
        }
        var total = checked(file.DataOffset + (int)frames.Length + SaveExtensionFile.DigestBytes);
        if (total > SaveExtensionFile.MaxBytes) throw new SaveFormatException("Edited Atomic Heart save exceeds 128 MiB.");
        var output = new byte[total];
        original.AsSpan(0, file.DataOffset).CopyTo(output);
        BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(file.DataOffset - sizeof(int)), (int)frames.Length);
        frames.GetBuffer().AsSpan(0, (int)frames.Length).CopyTo(output.AsSpan(file.DataOffset));
#pragma warning disable CA5350 // Required on-disk integrity algorithm, not authentication.
        SHA1.HashData(output.AsSpan(0, total - SaveExtensionFile.DigestBytes)).CopyTo(output, total - SaveExtensionFile.DigestBytes);
#pragma warning restore CA5350
        return output;
    }

    private static void WriteChunk(BinaryWriter writer, byte[] original, CompressedChunk chunk,
        byte[] originalPlain, byte[] plain, int position, int length)
    {
        // Unchanged compressed chunks are reused exactly, even after an earlier edit.
        if (length == chunk.UncompressedBytes && position <= originalPlain.Length - length &&
            plain.AsSpan(position, length).SequenceEqual(originalPlain.AsSpan(position, length)))
            writer.Write(original.AsSpan(chunk.Offset, SlotDataDecoder.FrameHeaderBytes + chunk.CompressedBytes));
        else WriteStored(writer, plain.AsSpan(position, length));
    }

    private static void WriteStored(BinaryWriter writer, ReadOnlySpan<byte> plain)
    {
        var stored = OodleCodec.EncodeStored(plain, SaveExtensionFile.MaxBytes);
        writer.Write(SaveExtensionFile.ChunkMagic);
        writer.Write((ulong)SaveExtensionFile.ChunkSize);
        writer.Write((ulong)stored.Length);
        writer.Write((ulong)plain.Length);
        writer.Write((ulong)stored.Length);
        writer.Write((ulong)plain.Length);
        writer.Write(stored);
    }
}
