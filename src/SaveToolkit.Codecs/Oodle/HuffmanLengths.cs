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

using static SaveToolkit.Codecs.Oodle.BitReaders;
using static SaveToolkit.Codecs.Oodle.DecoderState;
using static SaveToolkit.Codecs.Oodle.MemoryOps;

namespace SaveToolkit.Codecs.Oodle;

internal static class HuffmanLengths
{
    internal static int Huff_ReadCodeLengthsOld(BitReader bits, BufferView<byte> syms, BufferView<uint> code_prefix)
    {
        if (BitReader_ReadBitNoRefill(bits) != 0)
        {
            int n, sym = 0, codelen, num_symbols = 0;
            int avg_bits_x4 = 32;
            int forced_bits = BitReader_ReadBitsNoRefill(bits, 2);

            uint thres_for_valid_gamma_bits = 1u << (31 - (int)(20u >> forced_bits));
            bool skip = BitReader_ReadBit(bits) != 0;
            do
            {
                if (!skip)
                {
                    // Run of zeros
                    if ((bits.bits & 0xff000000) == 0)
                        return -1;
                    sym += BitReader_ReadBitsNoRefill(bits, 2 * (CountLeadingZeros(bits.bits) + 1)) - 2 + 1;
                    if (sym >= 256)
                        break;
                }
                skip = false;
                BitReader_Refill(bits);
                // Read out the gamma value for the # of symbols
                if ((bits.bits & 0xff000000) == 0)
                    return -1;
                n = BitReader_ReadBitsNoRefill(bits, 2 * (CountLeadingZeros(bits.bits) + 1)) - 2 + 1;
                // Overflow?
                if (sym + n > 256)
                    return -1;
                BitReader_Refill(bits);
                num_symbols += n;
                do
                {
                    if (bits.bits < thres_for_valid_gamma_bits)
                        return -1; // too big gamma value?

                    int lz = CountLeadingZeros(bits.bits);
                    int v = BitReader_ReadBitsNoRefill(bits, lz + forced_bits + 1) + ((lz - 1) << forced_bits);
                    codelen = (-(v & 1) ^ (v >> 1)) + ((avg_bits_x4 + 2) >> 2);
                    if (codelen < 1 || codelen > 11)
                        return -1;
                    avg_bits_x4 = codelen + ((3 * avg_bits_x4 + 2) >> 2);
                    BitReader_Refill(bits);
                    syms[code_prefix[codelen]++] = (byte)sym++;
                } while (--n != 0);
            } while (sym != 256);
            return (sym == 256) && (num_symbols >= 2) ? num_symbols : -1;
        }
        else
        {
            // Sparse symbol encoding
            int num_symbols = BitReader_ReadBitsNoRefill(bits, 8);
            if (num_symbols == 0)
                return -1;
            if (num_symbols == 1)
                syms[0] = (byte)BitReader_ReadBitsNoRefill(bits, 8);
            else
            {
                int codelen_bits = BitReader_ReadBitsNoRefill(bits, 3);
                if (codelen_bits > 4)
                    return -1;
                for (int i = 0; i < num_symbols; i++)
                {
                    BitReader_Refill(bits);
                    int sym = BitReader_ReadBitsNoRefill(bits, 8);
                    int codelen = BitReader_ReadBitsNoRefillZero(bits, codelen_bits) + 1;
                    if (codelen > 11)
                        return -1;
                    syms[code_prefix[codelen]++] = (byte)sym;
                }
            }
            return num_symbols;
        }
    }

    internal static int BitReader_ReadFluff(BitReader bits, int num_symbols)
    {
        if (num_symbols == 256)
            return 0;

        int x = 257 - num_symbols;
        if (x > num_symbols)
            x = num_symbols;

        x *= 2;

        int y = (int)BSR((uint)(x - 1)) + 1;

        uint v = bits.bits >> (32 - y);
        uint z = (1u << y) - (uint)x;

        if ((v >> 1) >= z)
        {
            bits.bits <<= y;
            bits.bitpos += y;
            return (int)(v - z);
        }
        bits.bits <<= (y - 1);
        bits.bitpos += (y - 1);
        return (int)(v >> 1);
    }
}
