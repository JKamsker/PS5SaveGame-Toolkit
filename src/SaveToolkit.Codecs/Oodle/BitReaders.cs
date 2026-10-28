// Kraken decompressor: a C# port of the Kraken path of ooz (https://github.com/powzix/ooz).
//
// Original work: "Kraken Decompressor for Windows", Copyright (C) 2016, Powzix.
// This port: Copyright (C) 2026, the TekkenMusicModManager authors.
//
// This program is free software: you can redistribute it and/or modify it under the terms of the GNU
// General Public License as published by the Free Software Foundation, either version 3 of the License,
// or (at your option) any later version.
//
// This program is distributed in the hope that it will be useful, but WITHOUT ANY WARRANTY; without even
// the implied warranty of MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the GNU General Public
// License for more details. You should have received a copy of the GNU General Public License along with
// this program. If not, see <http://www.gnu.org/licenses/>.
//
// Adapted for SaveToolkit: checked managed views, stage composition, bounded work and recursion.

using static SaveToolkit.Codecs.Oodle.DecoderState;
using static SaveToolkit.Codecs.Oodle.MemoryOps;

namespace SaveToolkit.Codecs.Oodle;

internal static class BitReaders
{
    internal static readonly uint[] Bitmasks =
    {
        0x1, 0x3, 0x7, 0xf, 0x1f, 0x3f, 0x7f, 0xff,
        0x1ff, 0x3ff, 0x7ff, 0xfff, 0x1fff, 0x3fff, 0x7fff, 0xffff,
        0x1ffff, 0x3ffff, 0x7ffff, 0xfffff, 0x1fffff, 0x3fffff, 0x7fffff,
        0xffffff, 0x1ffffff, 0x3ffffff, 0x7ffffff, 0xfffffff, 0x1fffffff, 0x3fffffff, 0x7fffffff, 0xffffffff,
    };

    // ------------------------------------------------------------------ bit reader

    internal static void BitReader_Refill(BitReader bits)
    {
        while (bits.bitpos > 0)
        {
            bits.bits |= (uint)(bits.p < bits.p_end ? (bits.p).Value : 0) << bits.bitpos;
            bits.bitpos -= 8;
            bits.p++;
        }
    }

    internal static void BitReader_RefillBackwards(BitReader bits)
    {
        while (bits.bitpos > 0)
        {
            bits.p--;
            bits.bits |= (uint)(bits.p >= bits.p_end ? (bits.p).Value : 0) << bits.bitpos;
            bits.bitpos -= 8;
        }
    }

    internal static int BitReader_ReadBit(BitReader bits)
    {
        BitReader_Refill(bits);
        int r = (int)(bits.bits >> 31);
        bits.bits <<= 1;
        bits.bitpos += 1;
        return r;
    }

    internal static int BitReader_ReadBitNoRefill(BitReader bits)
    {
        int r = (int)(bits.bits >> 31);
        bits.bits <<= 1;
        bits.bitpos += 1;
        return r;
    }

    internal static int BitReader_ReadBitsNoRefill(BitReader bits, int n)
    {
        int r = (int)(bits.bits >> (32 - n));
        bits.bits <<= n;
        bits.bitpos += n;
        return r;
    }

    internal static int BitReader_ReadBitsNoRefillZero(BitReader bits, int n)
    {
        int r = (int)(bits.bits >> 1 >> (31 - n));
        bits.bits <<= n;
        bits.bitpos += n;
        return r;
    }

    internal static uint BitReader_ReadMoreThan24Bits(BitReader bits, int n)
    {
        uint rv = 0;
        if (n <= 24)
            rv = (uint)BitReader_ReadBitsNoRefillZero(bits, n);
        else
        {
            rv = (uint)BitReader_ReadBitsNoRefill(bits, 24) << (n - 24);
            BitReader_Refill(bits);
            rv += (uint)BitReader_ReadBitsNoRefill(bits, n - 24);
        }
        BitReader_Refill(bits);
        return rv;
    }

    internal static uint BitReader_ReadMoreThan24BitsB(BitReader bits, int n)
    {
        uint rv = 0;
        if (n <= 24)
            rv = (uint)BitReader_ReadBitsNoRefillZero(bits, n);
        else
        {
            rv = (uint)BitReader_ReadBitsNoRefill(bits, 24) << (n - 24);
            BitReader_RefillBackwards(bits);
            rv += (uint)BitReader_ReadBitsNoRefill(bits, n - 24);
        }
        BitReader_RefillBackwards(bits);
        return rv;
    }

    internal static uint BitReader_ReadDistance(BitReader bits, uint v)
    {
        uint w = 0, m = 0, n = 0, rv = 0;
        if (v < 0xF0)
        {
            n = (v >> 4) + 4;
            w = Rotl(bits.bits | 1, (int)n);
            bits.bitpos += (int)n;
            m = (2u << (int)n) - 1;
            bits.bits = w & ~m;
            rv = ((w & m) << 4) + (v & 0xF) - 248;
        }
        else
        {
            n = v - 0xF0 + 4;
            w = Rotl(bits.bits | 1, (int)n);
            bits.bitpos += (int)n;
            m = (2u << (int)n) - 1;
            bits.bits = w & ~m;
            rv = 8322816 + ((w & m) << 12);
            BitReader_Refill(bits);
            rv += bits.bits >> 20;
            bits.bitpos += 12;
            bits.bits <<= 12;
        }
        BitReader_Refill(bits);
        return rv;
    }

    internal static uint BitReader_ReadDistanceB(BitReader bits, uint v)
    {
        uint w = 0, m = 0, n = 0, rv = 0;
        if (v < 0xF0)
        {
            n = (v >> 4) + 4;
            w = Rotl(bits.bits | 1, (int)n);
            bits.bitpos += (int)n;
            m = (2u << (int)n) - 1;
            bits.bits = w & ~m;
            rv = ((w & m) << 4) + (v & 0xF) - 248;
        }
        else
        {
            n = v - 0xF0 + 4;
            w = Rotl(bits.bits | 1, (int)n);
            bits.bitpos += (int)n;
            m = (2u << (int)n) - 1;
            bits.bits = w & ~m;
            rv = 8322816 + ((w & m) << 12);
            BitReader_RefillBackwards(bits);
            rv += bits.bits >> (32 - 12);
            bits.bitpos += 12;
            bits.bits <<= 12;
        }
        BitReader_RefillBackwards(bits);
        return rv;
    }

    internal static bool BitReader_ReadLength(BitReader bits, ref uint v)
    {
        int n = CountLeadingZeros(bits.bits);
        if (n > 12) return false;
        bits.bitpos += n;
        bits.bits <<= n;
        BitReader_Refill(bits);
        n += 7;
        bits.bitpos += n;
        uint rv = (bits.bits >> (32 - n)) - 64;
        bits.bits <<= n;
        v = rv;
        BitReader_Refill(bits);
        return true;
    }

    internal static bool BitReader_ReadLengthB(BitReader bits, ref uint v)
    {
        int n = CountLeadingZeros(bits.bits);
        if (n > 12) return false;
        bits.bitpos += n;
        bits.bits <<= n;
        BitReader_RefillBackwards(bits);
        n += 7;
        bits.bitpos += n;
        uint rv = (bits.bits >> (32 - n)) - 64;
        bits.bits <<= n;
        v = rv;
        BitReader_RefillBackwards(bits);
        return true;
    }

}
