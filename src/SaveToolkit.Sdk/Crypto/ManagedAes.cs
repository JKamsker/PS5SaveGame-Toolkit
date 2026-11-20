using System.Buffers.Binary;
using System.Numerics;

namespace SaveToolkit.Sdk.Crypto;

/// <summary>
/// Dependency-free AES block cipher written from FIPS-197 ("Advanced Encryption Standard", NIST, 2001,
/// updated 2023) for 128-, 192- and 256-bit keys. Encryption follows the cipher of section 5.1 and
/// decryption the equivalent inverse cipher of section 5.3.5, both with the usual table formulation that
/// merges SubBytes, ShiftRows and MixColumns per round. Tables are derived at start-up from the GF(2^8)
/// definitions in sections 4 and 5.1.1 rather than copied. Table lookups are not constant-time; this
/// exists for save-format compatibility with public keys, not for protecting secrets.
/// </summary>
internal sealed class ManagedAes
{
    internal const int BlockSize = 16;

    private static readonly byte[] SBox = BuildSBox();
    private static readonly byte[] InverseSBox = Invert(SBox);
    private static readonly uint[] Encrypt0 = BuildRoundTable(SBox, 2, 1, 1, 3);
    private static readonly uint[] Encrypt1 = Rotate(Encrypt0, 8);
    private static readonly uint[] Encrypt2 = Rotate(Encrypt0, 16);
    private static readonly uint[] Encrypt3 = Rotate(Encrypt0, 24);
    private static readonly uint[] Decrypt0 = BuildRoundTable(InverseSBox, 14, 9, 13, 11);
    private static readonly uint[] Decrypt1 = Rotate(Decrypt0, 8);
    private static readonly uint[] Decrypt2 = Rotate(Decrypt0, 16);
    private static readonly uint[] Decrypt3 = Rotate(Decrypt0, 24);

    private readonly uint[] _encryptKeys;
    private readonly uint[] _decryptKeys;
    private readonly int _rounds;

    /// <summary>Expands <paramref name="key"/> (16, 24 or 32 bytes) into the encryption and decryption schedules.</summary>
    internal ManagedAes(ReadOnlySpan<byte> key)
    {
        if (key.Length is not (16 or 24 or 32))
        {
            throw new ArgumentException("AES keys must be 16, 24 or 32 bytes.", nameof(key));
        }

        _rounds = key.Length / sizeof(uint) + 6;
        _encryptKeys = ExpandKey(key, _rounds);
        _decryptKeys = InverseSchedule(_encryptKeys, _rounds);
    }

    /// <summary>Encrypts one 16-byte block. <paramref name="source"/> and <paramref name="destination"/> may be the same block.</summary>
    internal void EncryptBlock(ReadOnlySpan<byte> source, Span<byte> destination)
    {
        var k = _encryptKeys;
        var s0 = BinaryPrimitives.ReadUInt32BigEndian(source) ^ k[0];
        var s1 = BinaryPrimitives.ReadUInt32BigEndian(source[4..]) ^ k[1];
        var s2 = BinaryPrimitives.ReadUInt32BigEndian(source[8..]) ^ k[2];
        var s3 = BinaryPrimitives.ReadUInt32BigEndian(source[12..]) ^ k[3];
        var offset = 4;
        for (var round = 1; round < _rounds; round++, offset += 4)
        {
            // SubBytes, ShiftRows (column c takes row r from column c + r) and MixColumns, then AddRoundKey.
            var t0 = Encrypt0[s0 >> 24] ^ Encrypt1[(byte)(s1 >> 16)] ^ Encrypt2[(byte)(s2 >> 8)] ^ Encrypt3[(byte)s3] ^ k[offset];
            var t1 = Encrypt0[s1 >> 24] ^ Encrypt1[(byte)(s2 >> 16)] ^ Encrypt2[(byte)(s3 >> 8)] ^ Encrypt3[(byte)s0] ^ k[offset + 1];
            var t2 = Encrypt0[s2 >> 24] ^ Encrypt1[(byte)(s3 >> 16)] ^ Encrypt2[(byte)(s0 >> 8)] ^ Encrypt3[(byte)s1] ^ k[offset + 2];
            var t3 = Encrypt0[s3 >> 24] ^ Encrypt1[(byte)(s0 >> 16)] ^ Encrypt2[(byte)(s1 >> 8)] ^ Encrypt3[(byte)s2] ^ k[offset + 3];
            (s0, s1, s2, s3) = (t0, t1, t2, t3);
        }

        // Final round has no MixColumns.
        BinaryPrimitives.WriteUInt32BigEndian(destination, Substitute(SBox, s0, s1, s2, s3) ^ k[offset]);
        BinaryPrimitives.WriteUInt32BigEndian(destination[4..], Substitute(SBox, s1, s2, s3, s0) ^ k[offset + 1]);
        BinaryPrimitives.WriteUInt32BigEndian(destination[8..], Substitute(SBox, s2, s3, s0, s1) ^ k[offset + 2]);
        BinaryPrimitives.WriteUInt32BigEndian(destination[12..], Substitute(SBox, s3, s0, s1, s2) ^ k[offset + 3]);
    }

    /// <summary>Decrypts one 16-byte block. <paramref name="source"/> and <paramref name="destination"/> may be the same block.</summary>
    internal void DecryptBlock(ReadOnlySpan<byte> source, Span<byte> destination)
    {
        var k = _decryptKeys;
        var s0 = BinaryPrimitives.ReadUInt32BigEndian(source) ^ k[0];
        var s1 = BinaryPrimitives.ReadUInt32BigEndian(source[4..]) ^ k[1];
        var s2 = BinaryPrimitives.ReadUInt32BigEndian(source[8..]) ^ k[2];
        var s3 = BinaryPrimitives.ReadUInt32BigEndian(source[12..]) ^ k[3];
        var offset = 4;
        for (var round = 1; round < _rounds; round++, offset += 4)
        {
            // InvSubBytes, InvShiftRows (column c takes row r from column c - r) and InvMixColumns.
            var t0 = Decrypt0[s0 >> 24] ^ Decrypt1[(byte)(s3 >> 16)] ^ Decrypt2[(byte)(s2 >> 8)] ^ Decrypt3[(byte)s1] ^ k[offset];
            var t1 = Decrypt0[s1 >> 24] ^ Decrypt1[(byte)(s0 >> 16)] ^ Decrypt2[(byte)(s3 >> 8)] ^ Decrypt3[(byte)s2] ^ k[offset + 1];
            var t2 = Decrypt0[s2 >> 24] ^ Decrypt1[(byte)(s1 >> 16)] ^ Decrypt2[(byte)(s0 >> 8)] ^ Decrypt3[(byte)s3] ^ k[offset + 2];
            var t3 = Decrypt0[s3 >> 24] ^ Decrypt1[(byte)(s2 >> 16)] ^ Decrypt2[(byte)(s1 >> 8)] ^ Decrypt3[(byte)s0] ^ k[offset + 3];
            (s0, s1, s2, s3) = (t0, t1, t2, t3);
        }

        BinaryPrimitives.WriteUInt32BigEndian(destination, Substitute(InverseSBox, s0, s3, s2, s1) ^ k[offset]);
        BinaryPrimitives.WriteUInt32BigEndian(destination[4..], Substitute(InverseSBox, s1, s0, s3, s2) ^ k[offset + 1]);
        BinaryPrimitives.WriteUInt32BigEndian(destination[8..], Substitute(InverseSBox, s2, s1, s0, s3) ^ k[offset + 2]);
        BinaryPrimitives.WriteUInt32BigEndian(destination[12..], Substitute(InverseSBox, s3, s2, s1, s0) ^ k[offset + 3]);
    }

    /// <summary>Takes row 0 from <paramref name="r0"/>, row 1 from <paramref name="r1"/> and so on, then substitutes each byte.</summary>
    private static uint Substitute(byte[] box, uint r0, uint r1, uint r2, uint r3) =>
        (uint)box[r0 >> 24] << 24 | (uint)box[(byte)(r1 >> 16)] << 16 | (uint)box[(byte)(r2 >> 8)] << 8 | box[(byte)r3];

    /// <summary>KeyExpansion, FIPS-197 section 5.2.</summary>
    private static uint[] ExpandKey(ReadOnlySpan<byte> key, int rounds)
    {
        var nk = key.Length / sizeof(uint);
        var words = new uint[BlockSize / sizeof(uint) * (rounds + 1)];
        for (var i = 0; i < nk; i++)
        {
            words[i] = BinaryPrimitives.ReadUInt32BigEndian(key[(i * sizeof(uint))..]);
        }

        byte roundConstant = 1;
        for (var i = nk; i < words.Length; i++)
        {
            var temp = words[i - 1];
            if (i % nk == 0)
            {
                temp = SubWord(BitOperations.RotateLeft(temp, 8)) ^ ((uint)roundConstant << 24);
                roundConstant = Times(roundConstant, 2);
            }
            else if (nk > 6 && i % nk == 4)
            {
                temp = SubWord(temp);
            }

            words[i] = words[i - nk] ^ temp;
        }

        return words;
    }

    /// <summary>Decryption schedule of the equivalent inverse cipher, FIPS-197 section 5.3.5.</summary>
    private static uint[] InverseSchedule(uint[] encrypt, int rounds)
    {
        var decrypt = new uint[encrypt.Length];
        for (var round = 0; round <= rounds; round++)
        {
            for (var column = 0; column < 4; column++)
            {
                var word = encrypt[(rounds - round) * 4 + column];
                // Decrypt*[SBox[x]] is InvMixColumns applied to byte x, because InverseSBox[SBox[x]] == x.
                decrypt[round * 4 + column] = round == 0 || round == rounds ? word
                    : Decrypt0[SBox[word >> 24]] ^ Decrypt1[SBox[(byte)(word >> 16)]]
                      ^ Decrypt2[SBox[(byte)(word >> 8)]] ^ Decrypt3[SBox[(byte)word]];
            }
        }

        return decrypt;
    }

    private static uint SubWord(uint word) => Substitute(SBox, word, word, word, word);

    /// <summary>Multiplication in GF(2^8) modulo x^8 + x^4 + x^3 + x + 1 (FIPS-197 section 4.2).</summary>
    private static byte Times(byte a, byte b)
    {
        var product = 0;
        int value = a;
        for (; b != 0; b >>= 1)
        {
            if ((b & 1) != 0)
            {
                product ^= value;
            }

            // xtime(), FIPS-197 section 4.2.1.
            value = (value << 1) ^ ((value & 0x80) != 0 ? 0x11B : 0);
        }

        return (byte)product;
    }

    /// <summary>S-box of FIPS-197 section 5.1.1: multiplicative inverse in GF(2^8), then the affine transformation.</summary>
    private static byte[] BuildSBox()
    {
        // Powers of the generator 3 enumerate every non-zero element, giving inverses as exp[255 - log x].
        var exp = new byte[255];
        var log = new byte[256];
        byte power = 1;
        for (var i = 0; i < exp.Length; i++)
        {
            exp[i] = power;
            log[power] = (byte)i;
            power = Times(power, 3);
        }

        var box = new byte[256];
        for (var x = 0; x < box.Length; x++)
        {
            var inverse = x == 0 ? (byte)0 : exp[(255 - log[x]) % 255];
            box[x] = (byte)(inverse ^ Rotate8(inverse, 1) ^ Rotate8(inverse, 2) ^ Rotate8(inverse, 3) ^ Rotate8(inverse, 4) ^ 0x63);
        }

        return box;
    }

    private static byte Rotate8(byte value, int count) => (byte)((value << count) | (value >> (8 - count)));

    private static byte[] Invert(byte[] box)
    {
        var inverse = new byte[box.Length];
        for (var i = 0; i < box.Length; i++)
        {
            inverse[box[i]] = (byte)i;
        }

        return inverse;
    }

    /// <summary>One column of (Inv)MixColumns applied to a substituted byte, rows 0..3 from the most significant byte.</summary>
    private static uint[] BuildRoundTable(byte[] box, byte m0, byte m1, byte m2, byte m3)
    {
        var table = new uint[256];
        for (var x = 0; x < table.Length; x++)
        {
            var s = box[x];
            table[x] = (uint)Times(s, m0) << 24 | (uint)Times(s, m1) << 16 | (uint)Times(s, m2) << 8 | Times(s, m3);
        }

        return table;
    }

    private static uint[] Rotate(uint[] table, int bits) => [.. table.Select(value => BitOperations.RotateRight(value, bits))];
}
