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
using static SaveToolkit.Codecs.Oodle.TansLut;
using static SaveToolkit.Codecs.Oodle.TansTables;

namespace SaveToolkit.Codecs.Oodle;

internal static class TansDecoder
{
    internal static bool Tans_Decode(TansDecoderParams prm)
    {
        BufferView<TansLutEnt> lut = prm.lut, e;
        BufferView<byte> dst = prm.dst, dst_end = prm.dst_end;
        BufferView<byte> ptr_f = prm.ptr_f, ptr_b = prm.ptr_b;
        uint bits_f = prm.bits_f, bits_b = prm.bits_b;
        int bitpos_f = prm.bitpos_f, bitpos_b = prm.bitpos_b;
        uint state_0 = prm.state_0, state_1 = prm.state_1;
        uint state_2 = prm.state_2, state_3 = prm.state_3;
        uint state_4 = prm.state_4;

        if (ptr_f > ptr_b)
            return false;

        if (dst < dst_end)
        {
            for (; ; )
            {
                // TANS_FORWARD_BITS
                bits_f |= ReadU32(ptr_f) << bitpos_f; ptr_f += (31 - bitpos_f) >> 3; bitpos_f |= 24;
                e = (lut + (state_0));
                (dst++).Value = e.Value.symbol;
                bitpos_f -= e.Value.bits_x;
                state_0 = (bits_f & e.Value.x) + e.Value.w;
                bits_f >>= e.Value.bits_x;
                if (dst >= dst_end) break;
                e = (lut + (state_1));
                (dst++).Value = e.Value.symbol;
                bitpos_f -= e.Value.bits_x;
                state_1 = (bits_f & e.Value.x) + e.Value.w;
                bits_f >>= e.Value.bits_x;
                if (dst >= dst_end) break;
                bits_f |= ReadU32(ptr_f) << bitpos_f; ptr_f += (31 - bitpos_f) >> 3; bitpos_f |= 24;
                e = (lut + (state_2));
                (dst++).Value = e.Value.symbol;
                bitpos_f -= e.Value.bits_x;
                state_2 = (bits_f & e.Value.x) + e.Value.w;
                bits_f >>= e.Value.bits_x;
                if (dst >= dst_end) break;
                e = (lut + (state_3));
                (dst++).Value = e.Value.symbol;
                bitpos_f -= e.Value.bits_x;
                state_3 = (bits_f & e.Value.x) + e.Value.w;
                bits_f >>= e.Value.bits_x;
                if (dst >= dst_end) break;
                bits_f |= ReadU32(ptr_f) << bitpos_f; ptr_f += (31 - bitpos_f) >> 3; bitpos_f |= 24;
                e = (lut + (state_4));
                (dst++).Value = e.Value.symbol;
                bitpos_f -= e.Value.bits_x;
                state_4 = (bits_f & e.Value.x) + e.Value.w;
                bits_f >>= e.Value.bits_x;
                if (dst >= dst_end) break;
                // TANS_BACKWARD_BITS
                bits_b |= ByteSwap(((ptr_b).As<uint>())[-1]) << bitpos_b; ptr_b -= (31 - bitpos_b) >> 3; bitpos_b |= 24;
                e = (lut + (state_0));
                (dst++).Value = e.Value.symbol;
                bitpos_b -= e.Value.bits_x;
                state_0 = (bits_b & e.Value.x) + e.Value.w;
                bits_b >>= e.Value.bits_x;
                if (dst >= dst_end) break;
                e = (lut + (state_1));
                (dst++).Value = e.Value.symbol;
                bitpos_b -= e.Value.bits_x;
                state_1 = (bits_b & e.Value.x) + e.Value.w;
                bits_b >>= e.Value.bits_x;
                if (dst >= dst_end) break;
                bits_b |= ByteSwap(((ptr_b).As<uint>())[-1]) << bitpos_b; ptr_b -= (31 - bitpos_b) >> 3; bitpos_b |= 24;
                e = (lut + (state_2));
                (dst++).Value = e.Value.symbol;
                bitpos_b -= e.Value.bits_x;
                state_2 = (bits_b & e.Value.x) + e.Value.w;
                bits_b >>= e.Value.bits_x;
                if (dst >= dst_end) break;
                e = (lut + (state_3));
                (dst++).Value = e.Value.symbol;
                bitpos_b -= e.Value.bits_x;
                state_3 = (bits_b & e.Value.x) + e.Value.w;
                bits_b >>= e.Value.bits_x;
                if (dst >= dst_end) break;
                bits_b |= ByteSwap(((ptr_b).As<uint>())[-1]) << bitpos_b; ptr_b -= (31 - bitpos_b) >> 3; bitpos_b |= 24;
                e = (lut + (state_4));
                (dst++).Value = e.Value.symbol;
                bitpos_b -= e.Value.bits_x;
                state_4 = (bits_b & e.Value.x) + e.Value.w;
                bits_b >>= e.Value.bits_x;
                if (dst >= dst_end) break;
            }
        }

        if (ptr_b - ptr_f + (bitpos_f >> 3) + (bitpos_b >> 3) != 0)
            return false;

        uint states_or = state_0 | state_1 | state_2 | state_3 | state_4;
        if ((states_or & ~0xFFu) != 0)
            return false;

        dst_end[0] = (byte)state_0;
        dst_end[1] = (byte)state_1;
        dst_end[2] = (byte)state_2;
        dst_end[3] = (byte)state_3;
        dst_end[4] = (byte)state_4;
        return true;
    }

    internal static int Krak_DecodeTans(BufferView<byte> src, long src_size, BufferView<byte> dst, int dst_size, BufferView<byte> scratch, BufferView<byte> scratch_end)
    {
        if (src_size < 8 || dst_size < 5)
            return -1;

        BufferView<byte> src_end = src + src_size;

        BitReader br = new();
        TansData tans_data = new() { A = BufferView<byte>.Allocate(256, src.Budget), B = BufferView<uint>.Allocate(256, src.Budget) };

        br.bitpos = 24;
        br.bits = 0;
        br.p = src;
        br.p_end = src_end;
        BitReader_Refill(br);

        // reserved bit
        if (BitReader_ReadBitNoRefill(br) != 0)
            return -1;

        int L_bits = BitReader_ReadBitsNoRefill(br, 2) + 8;

        if (!Tans_DecodeTable(br, L_bits, tans_data))
            return -1;

        src = br.p - (24 - br.bitpos) / 8;

        if (src >= src_end)
            return -1;

        uint lut_space_required = (uint)(((8 << L_bits) + 15) & ~15);
        if (lut_space_required > scratch_end - scratch)
            return -1;

        TansDecoderParams prm = new();
        prm.dst = dst;
        prm.dst_end = dst + dst_size - 5;

        prm.lut = AlignPointer(scratch, 16).As<TansLutEnt>();
        Tans_InitLut(tans_data, L_bits, prm.lut);

        // Read out the initial state
        uint L_mask = (1u << L_bits) - 1;
        uint bits_f = ReadU32(src);
        src += 4;
        uint bits_b = ByteSwap(ReadU32(src_end - 4));
        src_end -= 4;
        uint bitpos_f = 32, bitpos_b = 32;

        // Read first two.
        prm.state_0 = bits_f & L_mask;
        prm.state_1 = bits_b & L_mask;
        bits_f >>= L_bits; bitpos_f -= (uint)L_bits;
        bits_b >>= L_bits; bitpos_b -= (uint)L_bits;

        // Read next two.
        prm.state_2 = bits_f & L_mask;
        prm.state_3 = bits_b & L_mask;
        bits_f >>= L_bits; bitpos_f -= (uint)L_bits;
        bits_b >>= L_bits; bitpos_b -= (uint)L_bits;

        // Refill more bits
        bits_f |= ReadU32(src) << (int)bitpos_f;
        src += (31 - bitpos_f) >> 3;
        bitpos_f |= 24;

        // Read final state variable
        prm.state_4 = bits_f & L_mask;
        bits_f >>= L_bits; bitpos_f -= (uint)L_bits;

        prm.bits_f = bits_f;
        prm.ptr_f = src - (bitpos_f >> 3);
        prm.bitpos_f = (int)(bitpos_f & 7);

        prm.bits_b = bits_b;
        prm.ptr_b = src_end + (bitpos_b >> 3);
        prm.bitpos_b = (int)(bitpos_b & 7);

        if (!Tans_Decode(prm))
            return -1;

        return (int)src_size;
    }

}
