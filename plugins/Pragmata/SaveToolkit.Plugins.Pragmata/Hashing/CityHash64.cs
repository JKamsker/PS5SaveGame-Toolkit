// Algorithm adapted from Google CityHash (MIT); see the plugin's THIRD_PARTY_NOTICES.txt.
using System.Buffers.Binary;
using System.Numerics;

namespace SaveToolkit.Plugins.Pragmata.Hashing;

internal static class CityHash64
{
    private const ulong K0 = 0xc3a5c85c97cb3127;
    private const ulong K1 = 0xb492b66fbe98f273;
    private const ulong K2 = 0x9ae16a3b2f90404f;

    public static ulong Hash(ReadOnlySpan<byte> data)
    {
        unchecked
        {
            var length = data.Length;
            if (length <= 16) return Short(data);
            if (length <= 32) return Medium(data);
            if (length <= 64) return Long(data);

            var x = Fetch(data, length - 40);
            var y = Fetch(data, length - 16) + Fetch(data, length - 56);
            var z = Hash16(Fetch(data, length - 48) + (ulong)length, Fetch(data, length - 24));
            var v = Weak(data[(length - 64)..], (ulong)length, z);
            var w = Weak(data[(length - 32)..], y + K1, x);
            x = x * K1 + Fetch(data, 0);
            for (var i = 0; i < ((length - 1) & ~63); i += 64)
            {
                x = Rotate(x + y + v.First + Fetch(data, i + 8), 37) * K1;
                y = Rotate(y + v.Second + Fetch(data, i + 48), 42) * K1;
                x ^= w.Second;
                y += v.First + Fetch(data, i + 40);
                z = Rotate(z + w.First, 33) * K1;
                v = Weak(data[i..], v.Second * K1, x + w.First);
                w = Weak(data[(i + 32)..], z + w.Second, y + Fetch(data, i + 16));
                (z, x) = (x, z);
            }

            return Hash16(Hash16(v.First, w.First) + ShiftMix(y) * K1 + z, Hash16(v.Second, w.Second) + x);
        }
    }

    private static ulong Short(ReadOnlySpan<byte> data)
    {
        unchecked
        {
            var length = (ulong)data.Length;
            var multiplier = K2 + length * 2;
            if (length >= 8)
            {
                var a = Fetch(data, 0) + K2;
                var b = Fetch(data, data.Length - 8);
                return Hash16(Rotate(b, 37) * multiplier + a, (Rotate(a, 25) + b) * multiplier, multiplier);
            }
            if (length >= 4)
            {
                var a = (ulong)BinaryPrimitives.ReadUInt32LittleEndian(data);
                return Hash16(length + (a << 3), BinaryPrimitives.ReadUInt32LittleEndian(data[^4..]), multiplier);
            }
            if (length == 0) return K2;
            var y = (ulong)(data[0] + (data[data.Length >> 1] << 8));
            var z = length + ((ulong)data[^1] << 2);
            return ShiftMix(y * K2 ^ z * K0) * K2;
        }
    }

    private static ulong Medium(ReadOnlySpan<byte> data)
    {
        unchecked
        {
            var multiplier = K2 + (ulong)data.Length * 2;
            var a = Fetch(data, 0) * K1;
            var b = Fetch(data, 8);
            var c = Fetch(data, data.Length - 8) * multiplier;
            var d = Fetch(data, data.Length - 16) * K2;
            return Hash16(Rotate(a + b, 43) + Rotate(c, 30) + d, a + Rotate(b + K2, 18) + c, multiplier);
        }
    }

    private static ulong Long(ReadOnlySpan<byte> data)
    {
        unchecked
        {
            var multiplier = K2 + (ulong)data.Length * 2;
            var a = Fetch(data, 0) * K2;
            var b = Fetch(data, 8);
            var c = Fetch(data, data.Length - 24);
            var d = Fetch(data, data.Length - 32);
            var e = Fetch(data, 16) * K2;
            var f = Fetch(data, 24) * 9;
            var g = Fetch(data, data.Length - 8);
            var h = Fetch(data, data.Length - 16) * multiplier;
            var u = Rotate(a + g, 43) + (Rotate(b, 30) + c) * 9;
            var v = ((a + g) ^ d) + f + 1;
            var w = BinaryPrimitives.ReverseEndianness((u + v) * multiplier) + h;
            var x = Rotate(e + f, 42) + c;
            var y = (BinaryPrimitives.ReverseEndianness((v + w) * multiplier) + g) * multiplier;
            var z = e + f + c;
            a = BinaryPrimitives.ReverseEndianness((x + z) * multiplier + y) + b;
            b = ShiftMix((z + a) * multiplier + d + h) * multiplier;
            return b + x;
        }
    }

    private static (ulong First, ulong Second) Weak(ReadOnlySpan<byte> data, ulong a, ulong b)
    {
        unchecked
        {
            var w = Fetch(data, 0);
            var x = Fetch(data, 8);
            var y = Fetch(data, 16);
            var z = Fetch(data, 24);
            a += w;
            b = Rotate(b + a + z, 21);
            var c = a;
            a += x + y;
            b += Rotate(a, 44);
            return (a + z, b + c);
        }
    }

    private static ulong Hash16(ulong u, ulong v, ulong multiplier = 0x9ddfea08eb382d69)
    {
        unchecked
        {
            var a = (u ^ v) * multiplier;
            a ^= a >> 47;
            var b = (v ^ a) * multiplier;
            b ^= b >> 47;
            return b * multiplier;
        }
    }

    private static ulong Fetch(ReadOnlySpan<byte> data, int offset) => BinaryPrimitives.ReadUInt64LittleEndian(data[offset..]);
    private static ulong Rotate(ulong value, int shift) => BitOperations.RotateRight(value, shift);
    private static ulong ShiftMix(ulong value) => value ^ (value >> 47);
}
