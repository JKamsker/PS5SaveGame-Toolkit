using System.Buffers.Binary;
using SaveToolkit.Plugins.Pragmata.Hashing;
using SaveToolkit.Sdk;

namespace SaveToolkit.Plugins.Pragmata.Crypto;

internal sealed class DsssContainer
{
    internal const ulong Ps5Key = 526253637;
    internal const int MetadataSize = 0x210;
    internal const int ChecksumOffset = 0x200;
    internal const int HeaderSize = 16;
    private const int TrailerSize = 140;
    private const int CapacityUnit = 0x4000;
    private const int MaximumPlaintext = 64 * 1024 * 1024;
    private const ulong EncryptionSeed = 0x7da24a9e1479f3d7;
    private readonly byte[] _original;
    private readonly List<Block> _blocks = [];

    public byte[] Plaintext { get; }
    public int BlockCount => _blocks.Count;

    public DsssContainer(ReadOnlySpan<byte> data)
    {
        if (data.Length < HeaderSize + TrailerSize || !HasSupportedHeader(data))
            throw new SaveFormatException("Expected Pragmata DSSS version 2 with Mandarin flags 0x10.");
        if (BinaryPrimitives.ReadUInt32LittleEndian(data[^4..]) != Murmur3.Hash(data[..^4]))
            throw new SaveFormatException("DSSS outer MurmurHash3 mismatch.");
        var length = BinaryPrimitives.ReadUInt64LittleEndian(data[^12..]);
        if (length is 0 or > MaximumPlaintext)
            throw new SaveFormatException("Invalid or unsupported Mandarin plaintext length (maximum 64 MiB).");

        var random = new MandarinPrng(EncryptionSeed);
        var capacities = new int[((int)length + CapacityUnit - 1) / CapacityUnit];
        for (var i = 0; i < capacities.Length; i++)
        {
            capacities[i] = ((int)(random.State & 7) + 1) * CapacityUnit;
            random.Next();
        }
        ValidateBlockLayout(capacities, (int)length, data.Length);
        random.Add(~Ps5Key);

        Plaintext = new byte[(int)length];
        DecodeBlocks(data, capacities, random);
        _original = data.ToArray();
    }

    private void DecodeBlocks(ReadOnlySpan<byte> data, int[] capacities, MandarinPrng random)
    {
        var fileOffset = HeaderSize;
        var plainOffset = 0;
        foreach (var capacity in capacities)
        {
            var key = new byte[16];
            var iv = new byte[16];
            for (var i = 0; i < key.Length; i++)
            {
                var value = random.Next();
                key[i] = (byte)value;
                iv[i] = (byte)(value >> 8);
            }
            var mask = new byte[MetadataSize];
            var metadata = new byte[MetadataSize];
            for (var i = 0; i < MetadataSize; i++)
            {
                mask[i] = (byte)random.Next();
                metadata[i] = (byte)(data[fileOffset + i] ^ mask[i]);
            }
            MandarinMetadata.Validate(metadata, key, iv);
            var used = Math.Min(capacity, Plaintext.Length - plainOffset);
            var chunk = Plaintext.AsSpan(plainOffset, used);
            AesOfb.Transform(data.Slice(fileOffset + MetadataSize, used), chunk, key, iv);
            if (CityHash64.Hash(chunk) != BinaryPrimitives.ReadUInt64LittleEndian(metadata.AsSpan(ChecksumOffset)))
                throw new SaveFormatException($"Mandarin CityHash64 mismatch in block {_blocks.Count}.");
            _blocks.Add(new Block(fileOffset, plainOffset, used, mask.AsSpan(ChecksumOffset, sizeof(ulong)).ToArray()));
            fileOffset += MetadataSize + capacity;
            plainOffset += used;
            if (plainOffset == Plaintext.Length) break;
        }
        if (plainOffset != Plaintext.Length || fileOffset != data.Length - TrailerSize)
            throw new SaveFormatException("Mandarin block layout does not match the declared payload length.");
    }

    private static void ValidateBlockLayout(int[] capacities, int plaintextLength, int fileLength)
    {
        // All potential capacities affect the PRNG, but only those covering plaintext are stored.
        // Check every physical extent before allocating plaintext or decoding any metadata.
        var remainingPlaintext = plaintextLength;
        var remainingFile = fileLength - HeaderSize - TrailerSize;
        foreach (var capacity in capacities)
        {
            var blockSize = MetadataSize + capacity;
            if (blockSize > remainingFile)
                throw new SaveFormatException("Truncated Mandarin block.");
            remainingFile -= blockSize;
            remainingPlaintext -= Math.Min(capacity, remainingPlaintext);
            if (remainingPlaintext == 0) break;
        }
        if (remainingPlaintext != 0 || remainingFile != 0)
            throw new SaveFormatException("Mandarin block layout does not match the declared payload length.");
    }

    public static bool HasSupportedHeader(ReadOnlySpan<byte> header) =>
        header.Length >= HeaderSize && header[..4].SequenceEqual("DSSS"u8) &&
        BinaryPrimitives.ReadUInt32LittleEndian(header[4..]) == 2 &&
        BinaryPrimitives.ReadUInt32LittleEndian(header[8..]) == 0x10;

    public byte[] Rebuild(ReadOnlySpan<byte> plaintext)
    {
        if (plaintext.Length != Plaintext.Length)
            throw new SaveFormatException("Only fixed-size Pragmata edits are supported.");
        var output = _original.ToArray();
        Span<byte> checksum = stackalloc byte[sizeof(ulong)];
        foreach (var block in _blocks)
        {
            var oldChunk = Plaintext.AsSpan(block.PlainOffset, block.Used);
            var newChunk = plaintext.Slice(block.PlainOffset, block.Used);
            if (oldChunk.SequenceEqual(newChunk)) continue;
            var cipherOffset = block.FileOffset + MetadataSize;
            for (var i = 0; i < block.Used; i++)
                output[cipherOffset + i] ^= (byte)(oldChunk[i] ^ newChunk[i]);
            BinaryPrimitives.WriteUInt64LittleEndian(checksum, CityHash64.Hash(newChunk));
            for (var i = 0; i < checksum.Length; i++)
                output[block.FileOffset + ChecksumOffset + i] = (byte)(checksum[i] ^ block.ChecksumMask[i]);
        }
        BinaryPrimitives.WriteUInt32LittleEndian(output.AsSpan(output.Length - 4), Murmur3.Hash(output.AsSpan(0, output.Length - 4)));
        return output;
    }

    private sealed record Block(int FileOffset, int PlainOffset, int Used, byte[] ChecksumMask);
}
