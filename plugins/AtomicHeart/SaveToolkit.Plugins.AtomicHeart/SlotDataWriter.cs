using System.Buffers.Binary;
using System.Security.Cryptography;
using SaveToolkit.Codecs;
using SaveToolkit.Sdk;

namespace SaveToolkit.Plugins.AtomicHeart;

internal static class SlotDataWriter
{
    private const int StoredHeaderBytes = 2; // Each container frame is smaller than a Kraken quantum.
    private sealed record Frame(int Position, int Length, CompressedChunk? Reused)
    {
        internal int EncodedLength => SlotDataDecoder.FrameHeaderBytes + (Reused?.CompressedBytes ?? Length + StoredHeaderBytes);
    }

    internal static byte[] Write(byte[] original, SaveExtensionFile file, byte[] originalPlain, byte[] plain)
    {
        if (originalPlain.AsSpan().SequenceEqual(plain)) return original.ToArray();
        var frames = Plan(file, originalPlain, plain);
        var framedLength = checked(frames.Sum(f => f.EncodedLength));
        var total = checked(file.DataOffset + framedLength + SaveExtensionFile.DigestBytes);
        if (total > SaveExtensionFile.MaxBytes) throw new SaveFormatException("Edited Atomic Heart save exceeds 128 MiB.");
        // Reuse decisions precede allocation: one exactly-sized output, no growing aggregate stream.
        var output = new byte[total];
        original.AsSpan(0, file.DataOffset).CopyTo(output);
        BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(file.DataOffset - sizeof(int)), framedLength);
        using (var stream = new MemoryStream(output, writable: true))
        using (var writer = new BinaryWriter(stream))
        {
            stream.Position = file.DataOffset;
            foreach (var frame in frames)
            {
                if (frame.Reused is { } chunk) writer.Write(original.AsSpan(chunk.Offset, frame.EncodedLength));
                else WriteStored(writer, plain.AsSpan(frame.Position, frame.Length));
            }
        }
#pragma warning disable CA5350 // Required on-disk integrity algorithm, not authentication.
        SHA1.HashData(output.AsSpan(0, total - SaveExtensionFile.DigestBytes)).CopyTo(output, total - SaveExtensionFile.DigestBytes);
#pragma warning restore CA5350
        return output;
    }

    private static IReadOnlyList<Frame> Plan(SaveExtensionFile file, byte[] originalPlain, byte[] plain)
    {
        var frames = new List<Frame>();
        var position = 0;
        foreach (var chunk in file.Chunks)
        {
            if (position >= plain.Length) break;
            var length = Math.Min(chunk.UncompressedBytes, plain.Length - position);
            var reuse = length == chunk.UncompressedBytes && position <= originalPlain.Length - length &&
                plain.AsSpan(position, length).SequenceEqual(originalPlain.AsSpan(position, length));
            frames.Add(new(position, length, reuse ? chunk : null));
            position += length;
        }
        while (position < plain.Length)
        {
            var length = Math.Min(SaveExtensionFile.ChunkSize, plain.Length - position);
            frames.Add(new(position, length, null));
            position += length;
        }
        return frames;
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
