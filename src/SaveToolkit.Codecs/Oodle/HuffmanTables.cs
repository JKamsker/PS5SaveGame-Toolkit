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
using static SaveToolkit.Codecs.Oodle.MemoryOps;
using static SaveToolkit.Codecs.Oodle.RiceCodes;

namespace SaveToolkit.Codecs.Oodle;

internal static class HuffmanTables
{
    internal static int Huff_ConvertToRanges(BufferView<HuffRange> range, int num_symbols, int P, BufferView<byte> symlen, BitReader bits)
    {
        int num_ranges = P >> 1, v, sym_idx = 0;

        // Start with space?
        if ((P & 1) != 0)
        {
            BitReader_Refill(bits);
            v = (symlen++).Value;
            if (v >= 8)
                return -1;
            sym_idx = BitReader_ReadBitsNoRefill(bits, v + 1) + (1 << (v + 1)) - 1;
        }
        int syms_used = 0;

        for (int i = 0; i < num_ranges; i++)
        {
            BitReader_Refill(bits);
            v = symlen[0];
            if (v >= 9)
                return -1;
            int num = BitReader_ReadBitsNoRefillZero(bits, v) + (1 << v);
            v = symlen[1];
            if (v >= 8)
                return -1;
            int space = BitReader_ReadBitsNoRefill(bits, v + 1) + (1 << (v + 1)) - 1;
            range[i].symbol = (ushort)sym_idx;
            range[i].num = (ushort)num;
            syms_used += num;
            sym_idx += num + space;
            symlen += 2;
        }

        if (sym_idx >= 256 || syms_used >= num_symbols || sym_idx + num_symbols - syms_used > 256)
            return -1;

        range[num_ranges].symbol = (ushort)sym_idx;
        range[num_ranges].num = (ushort)(num_symbols - syms_used);

        return num_ranges + 1;
    }

    internal static int Huff_ReadCodeLengthsNew(BitReader bits, BufferView<byte> syms, BufferView<uint> code_prefix)
    {
        int forced_bits = BitReader_ReadBitsNoRefill(bits, 2);

        int num_symbols = BitReader_ReadBitsNoRefill(bits, 8) + 1;

        int fluff = BitReader_ReadFluff(bits, num_symbols);

        BufferView<byte> code_len = BufferView<byte>.Allocate(512 + 64, bits.p.Budget);
        BitReader2 br2 = new();
        br2.bitpos = (uint)((bits.bitpos - 24) & 7);
        br2.p_end = bits.p_end;
        br2.p = bits.p - (uint)((24 - bits.bitpos + 7) >> 3);

        if (!DecodeGolombRiceLengths(code_len, num_symbols + fluff, br2))
            return -1;
        MemSet(code_len + (num_symbols + fluff), 0, 16);
        if (!DecodeGolombRiceBits(code_len, (uint)num_symbols, (uint)forced_bits, br2))
            return -1;

        // Reset the bits decoder.
        bits.bitpos = 24;
        bits.p = br2.p;
        bits.bits = 0;
        BitReader_Refill(bits);
        bits.bits <<= (int)br2.bitpos;
        bits.bitpos += (int)br2.bitpos;

        uint running_sum = 0x1e;
        for (int i = 0; i < num_symbols; i++)
        {
            int v = code_len[i];
            v = -(v & 1) ^ (v >> 1);
            code_len[i] = (byte)(v + (int)(running_sum >> 2) + 1);
            if (code_len[i] < 1 || code_len[i] > 11)
                return -1;
            running_sum = (uint)(running_sum + v);
        }

        BufferView<HuffRange> range = BufferView<HuffRange>.Allocate(128, bits.p.Budget);
        int ranges = Huff_ConvertToRanges(range, num_symbols, fluff, (code_len + (num_symbols)), bits);
        if (ranges <= 0)
            return -1;

        BufferView<byte> cp = code_len;
        for (int i = 0; i < ranges; i++)
        {
            int sym = range[i].symbol;
            int n = range[i].num;
            do
            {
                syms[code_prefix[(cp++).Value]++] = (byte)sym++;
            } while (--n != 0);
        }

        return num_symbols;
    }

    internal static bool Huff_MakeLut(BufferView<uint> prefix_org, BufferView<uint> prefix_cur, BufferView<byte> bits2len, BufferView<byte> bits2sym, BufferView<byte> syms)
    {
        uint currslot = 0;
        for (uint i = 1; i < 11; i++)
        {
            uint start = prefix_org[i];
            uint count = prefix_cur[i] - start;
            if (count != 0)
            {
                uint stepsize = 1u << (int)(11 - i);
                uint num_to_set = count << (int)(11 - i);
                if (currslot + num_to_set > 2048)
                    return false;
                MemSet((bits2len + (currslot)), (byte)i, num_to_set);

                BufferView<byte> p = (bits2sym + (currslot));
                for (uint j = 0; j != count; j++, p += stepsize)
                    MemSet(p, syms[start + j], stepsize);
                currslot += num_to_set;
            }
        }
        if (prefix_cur[11] - prefix_org[11] != 0)
        {
            uint num_to_set = prefix_cur[11] - prefix_org[11];
            if (currslot + num_to_set > 2048)
                return false;
            MemSet((bits2len + (currslot)), 11, num_to_set);
            MemMove((bits2sym + (currslot)), (syms + prefix_org[11]), num_to_set);
            currslot += num_to_set;
        }
        return currslot == 2048;
    }
}
