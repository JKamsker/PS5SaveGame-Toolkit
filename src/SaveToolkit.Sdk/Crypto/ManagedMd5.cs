using System.Buffers.Binary;
using System.Numerics;

namespace SaveToolkit.Sdk.Crypto;

/// <summary>
/// Dependency-free MD5 message digest written from RFC 1321 ("The MD5 Message-Digest Algorithm",
/// R. Rivest, 1992). Used where the platform has no MD5 (the browser-wasm runtime). Like the platform
/// implementation it is for format compatibility only; MD5 provides no collision resistance.
/// </summary>
internal sealed class ManagedMd5
{
    internal const int HashSize = 16;
    private const int BlockSize = 64;
    private const int LengthOffset = BlockSize - sizeof(ulong);

    // RFC 1321 section 3.4: T[i] = floor(4294967296 * abs(sin(i))), i = 1..64.
    private static readonly uint[] SineTable =
    [
        0xD76AA478, 0xE8C7B756, 0x242070DB, 0xC1BDCEEE, 0xF57C0FAF, 0x4787C62A, 0xA8304613, 0xFD469501,
        0x698098D8, 0x8B44F7AF, 0xFFFF5BB1, 0x895CD7BE, 0x6B901122, 0xFD987193, 0xA679438E, 0x49B40821,
        0xF61E2562, 0xC040B340, 0x265E5A51, 0xE9B6C7AA, 0xD62F105D, 0x02441453, 0xD8A1E681, 0xE7D3FBC8,
        0x21E1CDE6, 0xC33707D6, 0xF4D50D87, 0x455A14ED, 0xA9E3E905, 0xFCEFA3F8, 0x676F02D9, 0x8D2A4C8A,
        0xFFFA3942, 0x8771F681, 0x6D9D6122, 0xFDE5380C, 0xA4BEEA44, 0x4BDECFA9, 0xF6BB4B60, 0xBEBFBC70,
        0x289B7EC6, 0xEAA127FA, 0xD4EF3085, 0x04881D05, 0xD9D4D039, 0xE6DB99E5, 0x1FA27CF8, 0xC4AC5665,
        0xF4292244, 0x432AFF97, 0xAB9423A7, 0xFC93A039, 0x655B59C3, 0x8F0CCC92, 0xFFEFF47D, 0x85845DD1,
        0x6FA87E4F, 0xFE2CE6E0, 0xA3014314, 0x4E0811A1, 0xF7537E82, 0xBD3AF235, 0x2AD7D2BB, 0xEB86D391,
    ];

    // RFC 1321 section 3.4: per-round left-rotation amounts S11..S44.
    private static readonly int[] Shifts =
    [
        7, 12, 17, 22, 7, 12, 17, 22, 7, 12, 17, 22, 7, 12, 17, 22,
        5, 9, 14, 20, 5, 9, 14, 20, 5, 9, 14, 20, 5, 9, 14, 20,
        4, 11, 16, 23, 4, 11, 16, 23, 4, 11, 16, 23, 4, 11, 16, 23,
        6, 10, 15, 21, 6, 10, 15, 21, 6, 10, 15, 21, 6, 10, 15, 21,
    ];

    private readonly byte[] _buffer = new byte[BlockSize];
    private readonly uint[] _words = new uint[BlockSize / sizeof(uint)];
    private uint _a, _b, _c, _d;
    private int _buffered;
    private ulong _length;

    internal ManagedMd5() => Reset();

    internal static byte[] HashData(ReadOnlySpan<byte> source)
    {
        var hash = new ManagedMd5();
        hash.Append(source);
        var digest = new byte[HashSize];
        hash.Finish(digest);
        return digest;
    }

    internal void Append(ReadOnlySpan<byte> data)
    {
        _length += (ulong)data.Length;
        if (_buffered > 0)
        {
            var take = Math.Min(BlockSize - _buffered, data.Length);
            data[..take].CopyTo(_buffer.AsSpan(_buffered));
            _buffered += take;
            data = data[take..];
            if (_buffered < BlockSize)
            {
                return;
            }

            Compress(_buffer);
            _buffered = 0;
        }

        while (data.Length >= BlockSize)
        {
            Compress(data[..BlockSize]);
            data = data[BlockSize..];
        }

        data.CopyTo(_buffer);
        _buffered = data.Length;
    }

    /// <summary>Writes the digest to <paramref name="destination"/> and resets the state (RFC 1321 sections 3.1-3.2, 3.5).</summary>
    internal void Finish(Span<byte> destination)
    {
        if (destination.Length < HashSize)
        {
            throw new ArgumentException("Destination is too short for an MD5 digest.", nameof(destination));
        }

        var bitLength = unchecked(_length * 8);
        _buffer[_buffered++] = 0x80;
        if (_buffered > LengthOffset)
        {
            _buffer.AsSpan(_buffered).Clear();
            Compress(_buffer);
            _buffered = 0;
        }

        _buffer.AsSpan(_buffered, LengthOffset - _buffered).Clear();
        BinaryPrimitives.WriteUInt64LittleEndian(_buffer.AsSpan(LengthOffset), bitLength);
        Compress(_buffer);
        BinaryPrimitives.WriteUInt32LittleEndian(destination, _a);
        BinaryPrimitives.WriteUInt32LittleEndian(destination[4..], _b);
        BinaryPrimitives.WriteUInt32LittleEndian(destination[8..], _c);
        BinaryPrimitives.WriteUInt32LittleEndian(destination[12..], _d);
        Reset();
    }

    private void Reset()
    {
        // RFC 1321 section 3.3: initial chaining values A, B, C, D.
        _a = 0x67452301;
        _b = 0xEFCDAB89;
        _c = 0x98BADCFE;
        _d = 0x10325476;
        _buffered = 0;
        _length = 0;
        Array.Clear(_buffer);
    }

    /// <summary>Processes one 64-byte block: the four rounds of RFC 1321 section 3.4.</summary>
    private void Compress(ReadOnlySpan<byte> block)
    {
        var x = _words;
        for (var i = 0; i < x.Length; i++)
        {
            x[i] = BinaryPrimitives.ReadUInt32LittleEndian(block[(i * sizeof(uint))..]);
        }

        uint a = _a, b = _b, c = _c, d = _d;
        for (var i = 0; i < BlockSize; i++)
        {
            uint f;
            int k;
            switch (i >> 4)
            {
                case 0: f = (b & c) | (~b & d); k = i; break;
                case 1: f = (b & d) | (c & ~d); k = (5 * i + 1) & 15; break;
                case 2: f = b ^ c ^ d; k = (3 * i + 5) & 15; break;
                default: f = c ^ (b | ~d); k = (7 * i) & 15; break;
            }

            var rotated = BitOperations.RotateLeft(unchecked(a + f + SineTable[i] + x[k]), Shifts[i]);
            a = d;
            d = c;
            c = b;
            b = unchecked(b + rotated);
        }

        unchecked
        {
            _a += a;
            _b += b;
            _c += c;
            _d += d;
        }
    }
}
