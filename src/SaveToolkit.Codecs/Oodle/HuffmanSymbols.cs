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

internal static class HuffmanSymbols
{
    /// <summary>output[i] = input[reverse of the 11 bits of i]: the scalar form of ReverseBitsArray2048.</summary>
    internal static void ReverseBitsArray2048(BufferView<byte> input, BufferView<byte> output)
    {
        for (int i = 0; i < 2048; i++)
        {
            int r = 0;
            for (int b = 0; b < 11; b++) r |= ((i >> b) & 1) << (10 - b);
            output[i] = input[r];
        }
    }

    internal static bool Kraken_DecodeBytesCore(HuffReader hr, BufferView<byte> bits2len, BufferView<byte> bits2sym)
    {
        BufferView<byte> src = hr.src;
        uint src_bits = hr.src_bits;
        int src_bitpos = hr.src_bitpos;

        BufferView<byte> src_mid = hr.src_mid;
        uint src_mid_bits = hr.src_mid_bits;
        int src_mid_bitpos = hr.src_mid_bitpos;

        BufferView<byte> src_end = hr.src_end;
        uint src_end_bits = hr.src_end_bits;
        int src_end_bitpos = hr.src_end_bitpos;

        int k = 0, n = 0;
        BufferView<byte> dst = hr.output;
        BufferView<byte> dst_end = hr.output_end;

        if (src > src_mid)
            return false;

        if (hr.src_end - src_mid >= 4 && dst_end - dst >= 6)
        {
            dst_end -= 5;
            src_end -= 4;

            while (dst < dst_end && src <= src_mid && src_mid <= src_end)
            {
                src_bits |= ReadU32(src) << src_bitpos;
                src += (31 - src_bitpos) >> 3;

                src_end_bits |= ByteSwap(ReadU32(src_end)) << src_end_bitpos;
                src_end -= (31 - src_end_bitpos) >> 3;

                src_mid_bits |= ReadU32(src_mid) << src_mid_bitpos;
                src_mid += (31 - src_mid_bitpos) >> 3;

                src_bitpos |= 0x18;
                src_end_bitpos |= 0x18;
                src_mid_bitpos |= 0x18;

                k = (int)(src_bits & 0x7FF); n = bits2len[k]; src_bits >>= n; src_bitpos -= n; dst[0] = bits2sym[k];
                k = (int)(src_end_bits & 0x7FF); n = bits2len[k]; src_end_bits >>= n; src_end_bitpos -= n; dst[1] = bits2sym[k];
                k = (int)(src_mid_bits & 0x7FF); n = bits2len[k]; src_mid_bits >>= n; src_mid_bitpos -= n; dst[2] = bits2sym[k];
                k = (int)(src_bits & 0x7FF); n = bits2len[k]; src_bits >>= n; src_bitpos -= n; dst[3] = bits2sym[k];
                k = (int)(src_end_bits & 0x7FF); n = bits2len[k]; src_end_bits >>= n; src_end_bitpos -= n; dst[4] = bits2sym[k];
                k = (int)(src_mid_bits & 0x7FF); n = bits2len[k]; src_mid_bits >>= n; src_mid_bitpos -= n; dst[5] = bits2sym[k];
                dst += 6;
            }
            dst_end += 5;

            src -= src_bitpos >> 3;
            src_bitpos &= 7;

            src_end += 4 + (src_end_bitpos >> 3);
            src_end_bitpos &= 7;

            src_mid -= src_mid_bitpos >> 3;
            src_mid_bitpos &= 7;
        }
        for (; ; )
        {
            if (dst >= dst_end)
                break;

            if (src_mid - src <= 1)
            {
                if (src_mid - src == 1)
                    src_bits |= (uint)src.Value << src_bitpos;
            }
            else
                src_bits |= (uint)ReadU16(src) << src_bitpos;
            k = (int)(src_bits & 0x7FF);
            n = bits2len[k];
            src_bitpos -= n;
            src_bits >>= n;
            (dst++).Value = bits2sym[k];
            src += (7 - src_bitpos) >> 3;
            src_bitpos &= 7;

            if (dst < dst_end)
            {
                if (src_end - src_mid <= 1)
                {
                    if (src_end - src_mid == 1)
                    {
                        src_end_bits |= (uint)src_mid.Value << src_end_bitpos;
                        src_mid_bits |= (uint)src_mid.Value << src_mid_bitpos;
                    }
                }
                else
                {
                    uint v = ReadU16(src_end - 2);
                    src_end_bits |= (((v >> 8) | (v << 8)) & 0xffff) << src_end_bitpos;
                    src_mid_bits |= (uint)ReadU16(src_mid) << src_mid_bitpos;
                }
                n = bits2len[src_end_bits & 0x7FF];
                (dst++).Value = bits2sym[src_end_bits & 0x7FF];
                src_end_bitpos -= n;
                src_end_bits >>= n;
                src_end -= (7 - src_end_bitpos) >> 3;
                src_end_bitpos &= 7;
                if (dst < dst_end)
                {
                    n = bits2len[src_mid_bits & 0x7FF];
                    (dst++).Value = bits2sym[src_mid_bits & 0x7FF];
                    src_mid_bitpos -= n;
                    src_mid_bits >>= n;
                    src_mid += (7 - src_mid_bitpos) >> 3;
                    src_mid_bitpos &= 7;
                }
            }
            if (src > src_mid || src_mid > src_end)
                return false;
        }
        if (src != hr.src_mid_org || src_end != src_mid)
            return false;
        return true;
    }
}
