using System.Buffers.Binary;
using SaveToolkit.Plugins.Pragmata.Crypto;
using SaveToolkit.Plugins.Pragmata.Hashing;

namespace SaveToolkit.Plugins.Pragmata.Tests;

/// <summary>Authored Mandarin framing; contains no private save bytes or RSA claims.</summary>
internal static class SyntheticDsss
{
    internal const int TrailerSize = 140;
    internal const int CapacityUnit = 0x4000;
    private const ulong EncryptionSeed = 0x7da24a9e1479f3d7;
    private const uint Version = 2;
    private const uint Flags = 0x10;

    public static byte[] EmptyEnvelope(ulong declaredLength)
    {
        var bytes = new byte[DsssContainer.HeaderSize + TrailerSize];
        "DSSS"u8.CopyTo(bytes);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), Version);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(8), Flags);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(bytes.Length - 12), declaredLength);
        SampleEvidenceTests.RepairOuterHash(bytes);
        return bytes;
    }

    public static byte[] Create(byte[] plaintext)
    {
        var random = new MandarinPrng(EncryptionSeed);
        var capacities = new List<int>();
        for (var index = 0; index < (plaintext.Length + CapacityUnit - 1) / CapacityUnit; index++)
        {
            capacities.Add(((int)(random.State & 7) + 1) * CapacityUnit);
            random.Next();
        }
        random.Add(~DsssContainer.Ps5Key);
        using var stream = new MemoryStream();
        stream.Write(EmptyEnvelope((ulong)plaintext.Length).AsSpan(0, DsssContainer.HeaderSize));
        var plainOffset = 0;
        foreach (var capacity in capacities)
        {
            var key = new byte[16];
            var iv = new byte[16];
            for (var index = 0; index < key.Length; index++)
            {
                var value = random.Next();
                key[index] = (byte)value;
                iv[index] = (byte)(value >> 8);
            }
            var metadata = new byte[DsssContainer.MetadataSize];
            // x=1 gives divisor 1; the paired integer then holds each expected key/IV word.
            // These are synthetic consistency metadata, not signed/authentic game metadata.
            var recovered = key.Concat(iv).ToArray();
            for (var pair = 0; pair < 4; pair++)
            {
                metadata[pair * 128] = 1;
                recovered.AsSpan(pair * sizeof(ulong), sizeof(ulong)).CopyTo(metadata.AsSpan(pair * 128 + 64));
            }
            var used = Math.Min(capacity, plaintext.Length - plainOffset);
            var chunk = plaintext.AsSpan(plainOffset, used);
            BinaryPrimitives.WriteUInt64LittleEndian(metadata.AsSpan(DsssContainer.ChecksumOffset), CityHash64.Hash(chunk));
            for (var index = 0; index < metadata.Length; index++) metadata[index] ^= (byte)random.Next();
            stream.Write(metadata);
            var ciphertext = new byte[capacity];
            AesOfb.Transform(chunk, ciphertext.AsSpan(0, used), key, iv);
            stream.Write(ciphertext);
            plainOffset += used;
            if (plainOffset == plaintext.Length) break;
        }
        stream.Write(EmptyEnvelope((ulong)plaintext.Length).AsSpan(DsssContainer.HeaderSize));
        var bytes = stream.ToArray();
        SampleEvidenceTests.RepairOuterHash(bytes);
        return bytes;
    }
}
