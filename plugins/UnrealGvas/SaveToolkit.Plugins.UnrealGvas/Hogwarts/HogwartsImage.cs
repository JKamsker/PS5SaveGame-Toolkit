using System.Buffers.Binary;
using SaveToolkit.Codecs;
using SaveToolkit.Plugins.UnrealGvas.Gvas;
using SaveToolkit.Sdk;

namespace SaveToolkit.Plugins.UnrealGvas.Hogwarts;

/// <summary>Hogwarts-specific UE framing around shared Kraken streams and a prefixed SQLite image.</summary>
public static class HogwartsImage
{
    public const string SaveClass = "/Script/PersistentData.PersistentGameData";
    public const int MaxDecodedBytes = 32 * 1024 * 1024;
    public const int ChunkBytes = 128 * 1024;
    public const int FrameBytes = 6 * sizeof(ulong);
    public const int PrefixBytes = 2 * sizeof(int);
    private const ulong Magic = 0x9E2A83C1;

    public static byte[] ReadArray(GvasFile file, GvasNode node)
    {
        if (node.Type != "ArrayProperty" || node.Length < sizeof(int) || node.Offset < 0 ||
            node.Offset > file.Original.Length - node.Length)
            throw new SaveFormatException("Hogwarts database array has invalid bounds or type.");
        var payload = file.Original.Span.Slice(node.Offset, node.Length);
        int count = BinaryPrimitives.ReadInt32LittleEndian(payload);
        if (count != payload.Length - sizeof(int))
            throw new SaveFormatException("Hogwarts database array count disagrees with its property size.");
        return Decode(payload[sizeof(int)..]);
    }

    /// <summary>Decode all independent six-u64 frames, requiring exact consumption and both DB lengths.</summary>
    public static byte[] Decode(ReadOnlySpan<byte> frames)
    {
        using var output = new MemoryStream();
        while (!frames.IsEmpty)
        {
            if (frames.Length < FrameBytes || Read(frames, 0) != Magic || Read(frames, 8) != ChunkBytes)
                throw new SaveFormatException("Unsupported or truncated Hogwarts UE chunk header.");
            ulong compressed = Read(frames, 16), plain = Read(frames, 24);
            if (compressed == 0 || compressed > (ulong)(frames.Length - FrameBytes) ||
                plain is 0 or > ChunkBytes || compressed != Read(frames, 32) || plain != Read(frames, 40) ||
                output.Length + (long)plain > MaxDecodedBytes)
                throw new SaveFormatException("Hogwarts UE chunk lengths exceed bounds or disagree.");
            output.Write(OodleCodec.Decompress(frames.Slice(FrameBytes, (int)compressed), (int)plain, MaxDecodedBytes));
            frames = frames[(FrameBytes + (int)compressed)..];
        }
        byte[] decoded = output.ToArray();
        if (decoded.Length < PrefixBytes + 100 ||
            BinaryPrimitives.ReadInt32LittleEndian(decoded) != decoded.Length - sizeof(int) ||
            BinaryPrimitives.ReadInt32LittleEndian(decoded.AsSpan(sizeof(int))) != decoded.Length - PrefixBytes ||
            !decoded.AsSpan(PrefixBytes, 16).SequenceEqual("SQLite format 3\0"u8))
            throw new SaveFormatException("Hogwarts database prefix, length or SQLite magic is invalid.");
        return decoded[PrefixBytes..];
    }

    /// <summary>Emit bounded stored Kraken blocks for the validated Hogwarts write path.</summary>
    public static byte[] EncodeStored(ReadOnlySpan<byte> database)
    {
        if (database.Length < 100 || database.Length > MaxDecodedBytes - PrefixBytes ||
            !database[..16].SequenceEqual("SQLite format 3\0"u8))
            throw new SaveFormatException("Invalid Hogwarts SQLite image length or magic.");
        byte[] prefixed = new byte[database.Length + PrefixBytes];
        BinaryPrimitives.WriteInt32LittleEndian(prefixed, database.Length + sizeof(int));
        BinaryPrimitives.WriteInt32LittleEndian(prefixed.AsSpan(sizeof(int)), database.Length);
        database.CopyTo(prefixed.AsSpan(PrefixBytes));
        using var output = new MemoryStream();
        using var writer = new BinaryWriter(output);
        for (int offset = 0; offset < prefixed.Length; offset += ChunkBytes)
        {
            int length = Math.Min(ChunkBytes, prefixed.Length - offset);
            byte[] stored = OodleCodec.EncodeStored(prefixed.AsSpan(offset, length));
            writer.Write(Magic);
            writer.Write((ulong)ChunkBytes);
            writer.Write((ulong)stored.Length);
            writer.Write((ulong)length);
            writer.Write((ulong)stored.Length);
            writer.Write((ulong)length);
            writer.Write(stored);
        }
        return output.ToArray();
    }

    private static ulong Read(ReadOnlySpan<byte> bytes, int offset) =>
        BinaryPrimitives.ReadUInt64LittleEndian(bytes[offset..]);
}
