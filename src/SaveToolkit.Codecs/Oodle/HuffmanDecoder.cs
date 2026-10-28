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
using static SaveToolkit.Codecs.Oodle.HuffmanSymbols;
using static SaveToolkit.Codecs.Oodle.HuffmanTables;
using static SaveToolkit.Codecs.Oodle.MemoryOps;

namespace SaveToolkit.Codecs.Oodle;

internal static class HuffmanDecoder
{
    internal static readonly uint[] CodePrefixOrg = { 0x0, 0x0, 0x2, 0x6, 0xE, 0x1E, 0x3E, 0x7E, 0xFE, 0x1FE, 0x2FE, 0x3FE };

    internal static int Kraken_DecodeBytes_Type12(BufferView<byte> src, long src_size, BufferView<byte> output, int output_size, int type)
    {
        BitReader bits = new();
        BufferView<byte> src_end = src + src_size;

        bits.bitpos = 24;
        bits.bits = 0;
        bits.p = src;
        bits.p_end = src_end;
        BitReader_Refill(bits);

        BufferView<uint> code_prefix_org = BufferView<uint>.Allocate(12, src.Budget);
        BufferView<uint> code_prefix = BufferView<uint>.Allocate(12, src.Budget);
        for (int i = 0; i < 12; i++) code_prefix_org[i] = code_prefix[i] = CodePrefixOrg[i];
        BufferView<byte> syms = BufferView<byte>.Allocate(1280, src.Budget);
        int num_syms = 0;
        if (BitReader_ReadBitNoRefill(bits) == 0)
            num_syms = Huff_ReadCodeLengthsOld(bits, syms, code_prefix);
        else if (BitReader_ReadBitNoRefill(bits) == 0)
            num_syms = Huff_ReadCodeLengthsNew(bits, syms, code_prefix);
        else
            return -1;

        if (num_syms < 1)
            return -1;
        src = bits.p - ((24 - bits.bitpos) / 8);

        if (num_syms == 1)
        {
            MemSet(output, syms[0], output_size);
            // Upstream returns src-src_end here (zero for a valid single-symbol table),
            // but its caller requires the payload byte count. Require exact consumption.
            return src == src_end ? (int)src_size : -1;
        }

        BufferView<byte> lut_len = BufferView<byte>.Allocate(2048 + 16, src.Budget);
        BufferView<byte> lut_sym = BufferView<byte>.Allocate(2048 + 16, src.Budget);
        if (!Huff_MakeLut(code_prefix_org, code_prefix, lut_len, lut_sym, syms))
            return -1;

        BufferView<byte> rev_len = BufferView<byte>.Allocate(2048, src.Budget);
        BufferView<byte> rev_sym = BufferView<byte>.Allocate(2048, src.Budget);
        ReverseBitsArray2048(lut_len, rev_len);
        ReverseBitsArray2048(lut_sym, rev_sym);

        HuffReader hr = new();
        if (type == 1)
        {
            if (src + 3 > src_end)
                return -1;
            uint split_mid = ReadU16(src);
            src += 2;
            hr.output = output;
            hr.output_end = output + output_size;
            hr.src = src;
            hr.src_end = src_end;
            hr.src_mid_org = hr.src_mid = src + split_mid;
            hr.src_bitpos = 0; hr.src_bits = 0;
            hr.src_mid_bitpos = 0; hr.src_mid_bits = 0;
            hr.src_end_bitpos = 0; hr.src_end_bits = 0;
            if (!Kraken_DecodeBytesCore(hr, rev_len, rev_sym))
                return -1;
        }
        else
        {
            if (src + 6 > src_end)
                return -1;

            int half_output_size = (output_size + 1) >> 1;
            uint split_mid = ReadU32(src) & 0xFFFFFF;
            src += 3;
            if (split_mid > src_end - src)
                return -1;
            BufferView<byte> src_mid = src + split_mid;
            uint split_left = ReadU16(src);
            src += 2;
            if (src_mid - src < split_left + 2 || src_end - src_mid < 3)
                return -1;
            uint split_right = ReadU16(src_mid);
            if (src_end - (src_mid + 2) < split_right + 2)
                return -1;

            hr.output = output;
            hr.output_end = output + half_output_size;
            hr.src = src;
            hr.src_end = src_mid;
            hr.src_mid_org = hr.src_mid = src + split_left;
            hr.src_bitpos = 0; hr.src_bits = 0;
            hr.src_mid_bitpos = 0; hr.src_mid_bits = 0;
            hr.src_end_bitpos = 0; hr.src_end_bits = 0;
            if (!Kraken_DecodeBytesCore(hr, rev_len, rev_sym))
                return -1;

            hr.output = output + half_output_size;
            hr.output_end = output + output_size;
            hr.src = src_mid + 2;
            hr.src_end = src_end;
            hr.src_mid_org = hr.src_mid = src_mid + 2 + split_right;
            hr.src_bitpos = 0; hr.src_bits = 0;
            hr.src_mid_bitpos = 0; hr.src_mid_bits = 0;
            hr.src_end_bitpos = 0; hr.src_end_bits = 0;
            if (!Kraken_DecodeBytesCore(hr, rev_len, rev_sym))
                return -1;
        }
        return (int)src_size;
    }

}
