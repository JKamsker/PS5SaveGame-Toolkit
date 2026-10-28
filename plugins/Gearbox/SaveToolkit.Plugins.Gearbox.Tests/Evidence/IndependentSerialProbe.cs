using System.Buffers.Binary;
using System.Numerics;

namespace SaveToolkit.Plugins.Gearbox.Tests;

/// <summary>Independent integer/bit probe, also creates synthetic envelopes without production code.</summary>
internal static class IndependentSerialProbe
{
    public static byte[] Decode(byte[] serial)
    {
        if (serial.Length is < 8 or > 4096) throw new InvalidDataException("Probe serial length.");
        var seed = BinaryPrimitives.ReadInt32BigEndian(serial.AsSpan(1, 4));
        var decoded = Mix(serial[5..], seed);
        var rotation = (seed & 31) % decoded.Length;
        var plain = rotation == 0 ? decoded : decoded[^rotation..].Concat(decoded[..^rotation]).ToArray();
        var computed = Crc32([.. serial[..5], 255, 255, .. plain[2..]]);
        if (BinaryPrimitives.ReadUInt16BigEndian(plain) != (ushort)((computed >> 16) ^ computed))
            throw new InvalidDataException("Probe CRC failure.");
        return plain[2..];
    }

    public static byte[] Encode(byte[] payload, int version, int seed)
    {
        var header = new byte[5];
        header[0] = (byte)version;
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(1), seed);
        var crc = Crc32([.. header, 255, 255, .. payload]);
        var plain = new byte[2 + payload.Length];
        BinaryPrimitives.WriteUInt16BigEndian(plain, (ushort)((crc >> 16) ^ crc));
        payload.CopyTo(plain, 2);
        var rotation = (seed & 31) % plain.Length;
        return [.. header, .. Mix([.. plain[rotation..], .. plain[..rotation]], seed)];
    }

    private static byte[] Mix(byte[] data, int seed)
    {
        var state = new BigInteger(unchecked((uint)(seed >> 5)));
        return data.Select(b =>
        {
            if (seed == 0) return b;
            state = state * 0x10A860C1 % 0xFFFFFFFB;
            return (byte)(b ^ (byte)(state & 255));
        }).ToArray();
    }

    public static uint Crc32(byte[] bytes)
    {
        // MSB-first polynomial long division with reflected input/output, unlike the runtime loop.
        uint remainder = uint.MaxValue;
        foreach (var value in bytes)
            for (var bit = 0; bit < 8; bit++)
            {
                var carry = ((remainder >> 31) ^ (uint)(value >> bit)) & 1;
                remainder <<= 1;
                if (carry != 0) remainder ^= 0x04C11DB7;
            }
        var reflected = 0u;
        for (var i = 0; i < 32; i++) { reflected = (reflected << 1) | (remainder & 1); remainder >>= 1; }
        return ~reflected;
    }
}

internal sealed class ProbeBits(byte[] payload)
{
    private BigInteger _bits = new(payload, isUnsigned: true, isBigEndian: false);
    private int _remaining = payload.Length * 8;
    public int Take(int count)
    {
        if (count < 0 || count > _remaining) throw new InvalidDataException("Independent bit probe truncated.");
        var value = (int)(_bits & ((BigInteger.One << count) - 1));
        _bits >>= count;
        _remaining -= count;
        return value;
    }
}
