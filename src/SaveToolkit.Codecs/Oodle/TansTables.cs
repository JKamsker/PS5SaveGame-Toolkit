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
using static SaveToolkit.Codecs.Oodle.HuffmanLengths;
using static SaveToolkit.Codecs.Oodle.HuffmanTables;
using static SaveToolkit.Codecs.Oodle.MemoryOps;
using static SaveToolkit.Codecs.Oodle.RiceCodes;

namespace SaveToolkit.Codecs.Oodle;

internal static class TansTables
{
    internal sealed class TansData
    {
        public uint A_used;
        public uint B_used;
        public BufferView<byte> A;
        public BufferView<uint> B;
    }

    internal static void SimpleSortBytes(BufferView<byte> p, BufferView<byte> pend)
    {
        if (p == pend) return;
        for (BufferView<byte> lp = p + 1; lp != pend; lp++)
        {
            byte t = lp[0];
            BufferView<byte> rp;
            for (rp = lp; rp > p && t < rp[-1]; rp--)
                rp[0] = rp[-1];
            rp[0] = t;
        }
    }

    internal static void SimpleSortUInts(BufferView<uint> p, BufferView<uint> pend)
    {
        if (p == pend) return;
        for (BufferView<uint> lp = p + 1; lp != pend; lp++)
        {
            uint t = lp[0];
            BufferView<uint> rp;
            for (rp = lp; rp > p && t < rp[-1]; rp--)
                rp[0] = rp[-1];
            rp[0] = t;
        }
    }

    internal static bool Tans_DecodeTable(BitReader bits, int L_bits, TansData tans_data)
    {
        BitReader_Refill(bits);
        if (BitReader_ReadBitNoRefill(bits) != 0)
        {
            int Q = BitReader_ReadBitsNoRefill(bits, 3);
            int num_symbols = BitReader_ReadBitsNoRefill(bits, 8) + 1;
            if (num_symbols < 2)
                return false;
            int fluff = BitReader_ReadFluff(bits, num_symbols);
            int total_rice_values = fluff + num_symbols;
            BufferView<byte> rice = BufferView<byte>.Allocate(512 + 64, bits.p.Budget);
            BitReader2 br2 = new();

            // another bit reader...
            br2.p = bits.p - (uint)((24 - bits.bitpos + 7) >> 3);
            br2.p_end = bits.p_end;
            br2.bitpos = (uint)((bits.bitpos - 24) & 7);

            if (!DecodeGolombRiceLengths(rice, total_rice_values, br2))
                return false;
            MemSet(rice + total_rice_values, 0, 16);

            // Switch back to other bitreader impl
            bits.bitpos = 24;
            bits.p = br2.p;
            bits.bits = 0;
            BitReader_Refill(bits);
            bits.bits <<= (int)br2.bitpos;
            bits.bitpos += (int)br2.bitpos;

            BufferView<HuffRange> range = BufferView<HuffRange>.Allocate(133, bits.p.Budget);
            fluff = Huff_ConvertToRanges(range, num_symbols, fluff, (rice + (num_symbols)), bits);
            if (fluff < 0)
                return false;

            BitReader_Refill(bits);

            uint L = 1u << L_bits;
            BufferView<byte> cur_rice_ptr = rice;
            int average = 6;
            int somesum = 0;
            BufferView<byte> tanstable_A = tans_data.A;
            BufferView<uint> tanstable_B = tans_data.B;

            for (int ri = 0; ri < fluff; ri++)
            {
                int symbol = range[ri].symbol;
                int num = range[ri].num;
                do
                {
                    BitReader_Refill(bits);

                    int nextra = Q + (cur_rice_ptr++).Value;
                    if (nextra > 15)
                        return false;
                    int v = BitReader_ReadBitsNoRefillZero(bits, nextra) + (1 << nextra) - (1 << Q);

                    int average_div4 = average >> 2;
                    int limit = 2 * average_div4;
                    if (v <= limit)
                        v = average_div4 + (-(v & 1) ^ (int)((uint)v >> 1));
                    if (limit > v)
                        limit = v;
                    v += 1;
                    average += limit - average_div4;
                    (tanstable_A).Value = (byte)symbol;
                    (tanstable_B).Value = (uint)((symbol << 16) + v);
                    tanstable_A += v == 1 ? 1 : 0;
                    tanstable_B += v >= 2 ? 1 : 0;
                    somesum += v;
                    symbol += 1;
                } while (--num != 0);
            }
            tans_data.A_used = (uint)(tanstable_A - tans_data.A);
            tans_data.B_used = (uint)(tanstable_B - tans_data.B);
            if (somesum != L)
                return false;

            return true;
        }
        else
        {
            BufferView<bool> seen = BufferView<bool>.Allocate(256, bits.p.Budget);
            for (int i = 0; i < 256; i++) seen[i] = false;
            uint L = 1u << L_bits;

            int count = BitReader_ReadBitsNoRefill(bits, 3) + 1;

            int bits_per_sym = (int)BSR((uint)L_bits) + 1;
            int max_delta_bits = BitReader_ReadBitsNoRefill(bits, bits_per_sym);

            if (max_delta_bits == 0 || max_delta_bits > L_bits)
                return false;

            BufferView<byte> tanstable_A = tans_data.A;
            BufferView<uint> tanstable_B = tans_data.B;

            int weight = 0;
            int total_weights = 0;

            do
            {
                BitReader_Refill(bits);

                int sym = BitReader_ReadBitsNoRefill(bits, 8);
                if (seen[sym])
                    return false;

                int delta = BitReader_ReadBitsNoRefill(bits, max_delta_bits);

                weight += delta;

                if (weight == 0)
                    return false;

                seen[sym] = true;
                if (weight == 1)
                    (tanstable_A++).Value = (byte)sym;
                else
                    (tanstable_B++).Value = (uint)((sym << 16) + weight);

                total_weights += weight;
            } while (--count != 0);

            BitReader_Refill(bits);

            int sym2 = BitReader_ReadBitsNoRefill(bits, 8);
            if (seen[sym2])
                return false;

            if (L - total_weights < weight || L - total_weights <= 1)
                return false;

            (tanstable_B++).Value = (uint)((sym2 << 16) + (L - total_weights));

            tans_data.A_used = (uint)(tanstable_A - tans_data.A);
            tans_data.B_used = (uint)(tanstable_B - tans_data.B);

            SimpleSortBytes(tans_data.A, tanstable_A);
            SimpleSortUInts(tans_data.B, tanstable_B);
            return true;
        }
    }
}
