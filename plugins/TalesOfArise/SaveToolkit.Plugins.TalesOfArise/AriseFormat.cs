using System.Buffers.Binary;
using System.Security.Cryptography;
using SaveToolkit.Sdk;

namespace SaveToolkit.Plugins.TalesOfArise;

internal static class AriseFormat
{
    public const int FileSize = 760856;
    public const int PayloadOffset = 0x32058;
    public const int HashOffset = PayloadOffset + 8;
    public const int ModeOffset = PayloadOffset + 0x1C;
    public const int CipherOffset = PayloadOffset + 0x20;
    public const int HeaderSize = 0x10;
    private const int OffsetTableSize = 0x40;
    private const int DirectoryPointer = 0x18;
    private const uint Magic = 0x10051630;
    private const uint DirectoryMarker = 100;

    public static bool HasHeader(ReadOnlySpan<byte> bytes) => bytes.Length >= CipherOffset
        && U32(bytes, PayloadOffset) == Magic
        && U32(bytes, PayloadOffset + 4) == FileSize - PayloadOffset
        && bytes[ModeOffset] == 3 && bytes[ModeOffset + 1] == 1;

    public static AriseSave Decode(byte[] bytes)
    {
        if (bytes.Length != FileSize || !HasHeader(bytes))
        {
            throw new SaveFormatException("Unsupported Tales of Arise save size, magic or PS5 encryption mode.");
        }

        if (!CryptographicOperations.FixedTimeEquals(Digest(bytes.AsSpan(CipherOffset)), bytes.AsSpan(HashOffset, 20)))
        {
            throw new SaveFormatException("Tales of Arise ciphertext SHA1 mismatch; the save is corrupt.");
        }

        var result = Transform(bytes.AsSpan(PayloadOffset).ToArray(), encrypt: false);
        return new AriseSave(bytes.ToArray(), result.Payload, result.Sections);
    }

    public static byte[] Encode(AriseSave save)
    {
        var result = Transform(save.Payload, encrypt: true);
        var bytes = save.Original.ToArray();
        result.Payload.CopyTo(bytes, PayloadOffset);
        Digest(bytes.AsSpan(CipherOffset)).CopyTo(bytes, HashOffset);
        var verified = Decode(bytes);
        // Compare all decrypted bytes, including unknown fields and unvisited gaps.
        if (!verified.Payload.AsSpan(0x20).SequenceEqual(save.Payload.AsSpan(0x20)))
        {
            throw new SaveFormatException("Tales of Arise encryption self-check did not preserve plaintext.");
        }

        return bytes;
    }

    private static TransformResult Transform(byte[] source, bool encrypt)
    {
        var destination = source.ToArray();
        var occupied = new List<(int Start, int End)>();
        var sections = new List<AriseSection>();
        using var cipher = new AriseCipher();
        Block(0x20, OffsetTableSize);
        var directory = Pointer(Plain(), 0x20 + DirectoryPointer);
        Block(directory, OffsetTableSize);
        if (U32(Plain(), directory) != DirectoryMarker || U32(Plain(), directory + 4) != AriseSchema.Sections.Count)
        {
            throw new SaveFormatException("Unsupported Tales of Arise section directory.");
        }

        var next = Pointer(Plain(), directory + 8);
        foreach (var schema in AriseSchema.Sections)
        {
            Block(next, HeaderSize);
            var plain = Plain();
            var version = U32(plain, next);
            if (U32(plain, next + 4) != schema.Id || U32(plain, next + 8) != schema.Size || version != schema.Version)
            {
                throw new SaveFormatException($"Unsupported Tales of Arise section {schema.Name} layout.");
            }

            var dataOffset = next + HeaderSize;
            var following = Pointer(plain, next + 12);
            Block(dataOffset, schema.Size);
            sections.Add(new AriseSection(schema.Name, schema.Id, version, dataOffset, schema.Size));
            next = following;
        }

        if (next != 0)
        {
            throw new SaveFormatException("Tales of Arise section chain has an unexpected tail.");
        }

        return new TransformResult(destination, sections.AsReadOnly());

        byte[] Plain() => encrypt ? source : destination;

        void Block(int offset, int length)
        {
            if (offset < 0x20 || offset % 16 != 0 || length <= 0 || length % 16 != 0 || offset > source.Length - length
                || occupied.Any(range => offset < range.End && offset + length > range.Start))
            {
                throw new SaveFormatException("Tales of Arise encrypted section is unaligned, overlapping or out of bounds.");
            }

            occupied.Add((offset, offset + length));
            cipher.Transform(source.AsSpan(offset, length), destination.AsSpan(offset, length), encrypt);
        }
    }

    public static uint U32(ReadOnlySpan<byte> data, int offset) => BinaryPrimitives.ReadUInt32LittleEndian(data[offset..]);

    private static int Pointer(ReadOnlySpan<byte> data, int offset)
    {
        var value = U32(data, offset);
        return value <= int.MaxValue ? (int)value : throw new SaveFormatException("Tales of Arise section pointer is out of bounds.");
    }

    private static byte[] Digest(ReadOnlySpan<byte> bytes)
    {
#pragma warning disable CA5350 // Required legacy file integrity algorithm.
        return SHA1.HashData(bytes);
#pragma warning restore CA5350
    }

    private sealed record TransformResult(byte[] Payload, IReadOnlyList<AriseSection> Sections);
}

internal sealed record AriseSave(byte[] Original, byte[] Payload, IReadOnlyList<AriseSection> Sections);
internal sealed record AriseSection(string Name, uint Id, uint Version, int Offset, int Size);
