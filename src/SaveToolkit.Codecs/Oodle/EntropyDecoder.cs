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

using static SaveToolkit.Codecs.Oodle.HuffmanDecoder;
using static SaveToolkit.Codecs.Oodle.MemoryOps;
using static SaveToolkit.Codecs.Oodle.RecursiveDecoder;
using static SaveToolkit.Codecs.Oodle.TansDecoder;

namespace SaveToolkit.Codecs.Oodle;

internal static class EntropyDecoder
{
    internal static int Kraken_GetBlockSize(BufferView<byte> src, BufferView<byte> src_end, ref int dest_size, int dest_capacity)
    {
        BufferView<byte> src_org = src;
        int src_size = 0, dst_size = 0;

        if (src_end - src < 2)
            return -1; // too few bytes

        int chunk_type = (src[0] >> 4) & 0x7;
        if (chunk_type == 0)
        {
            if (src[0] >= 0x80)
            {
                // In this mode, memcopy stores the length in the bottom 12 bits.
                src_size = ((src[0] << 8) | src[1]) & 0xFFF;
                src += 2;
            }
            else
            {
                if (src_end - src < 3)
                    return -1; // too few bytes
                src_size = (src[0] << 16) | (src[1] << 8) | src[2];
                if ((src_size & ~0x3ffff) != 0)
                    return -1; // reserved bits must not be set
                src += 3;
            }
            if (src_size > dest_capacity || src_end - src < src_size)
                return -1;
            dest_size = src_size;
            return (int)(src + src_size - src_org);
        }

        if (chunk_type >= 6)
            return -1;

        // In all the other modes, the initial bytes encode the src_size and the dst_size
        if (src[0] >= 0x80)
        {
            if (src_end - src < 3)
                return -1; // too few bytes

            // short mode, 10 bit sizes
            uint bits = (uint)((src[0] << 16) | (src[1] << 8) | src[2]);
            src_size = (int)(bits & 0x3ff);
            dst_size = src_size + (int)((bits >> 10) & 0x3ff) + 1;
            src += 3;
        }
        else
        {
            // long mode, 18 bit sizes
            if (src_end - src < 5)
                return -1; // too few bytes
            uint bits = (uint)((src[1] << 24) | (src[2] << 16) | (src[3] << 8) | src[4]);
            src_size = (int)(bits & 0x3ffff);
            dst_size = (int)(((bits >> 18) | ((uint)src[0] << 14)) & 0x3FFFF) + 1;
            if (src_size >= dst_size)
                return -1;
            src += 5;
        }
        if (src_end - src < src_size || dst_size > dest_capacity)
            return -1;
        dest_size = dst_size;
        return src_size;
    }

    internal static int Kraken_DecodeBytes(ref BufferView<byte> output, BufferView<byte> src, BufferView<byte> src_end, ref int decoded_size, long output_size,
                                          bool force_memmove, BufferView<byte> scratch, BufferView<byte> scratch_end)
    {
        using var nesting = src.Budget.Enter();
        BufferView<byte> src_org = src;
        int src_size = 0, dst_size = 0;

        if (src_end - src < 2)
            return -1; // too few bytes

        int chunk_type = (src[0] >> 4) & 0x7;
        if (chunk_type == 0)
        {
            if (src[0] >= 0x80)
            {
                // In this mode, memcopy stores the length in the bottom 12 bits.
                src_size = ((src[0] << 8) | src[1]) & 0xFFF;
                src += 2;
            }
            else
            {
                if (src_end - src < 3)
                    return -1; // too few bytes
                src_size = (src[0] << 16) | (src[1] << 8) | src[2];
                if ((src_size & ~0x3ffff) != 0)
                    return -1; // reserved bits must not be set
                src += 3;
            }
            if (src_size > output_size || src_end - src < src_size)
                return -1;
            decoded_size = src_size;
            if (force_memmove)
                MemMove(output, src, src_size);
            else
                output = src;
            return (int)(src + src_size - src_org);
        }

        // In all the other modes, the initial bytes encode the src_size and the dst_size
        if (src[0] >= 0x80)
        {
            if (src_end - src < 3)
                return -1; // too few bytes

            // short mode, 10 bit sizes
            uint bits = (uint)((src[0] << 16) | (src[1] << 8) | src[2]);
            src_size = (int)(bits & 0x3ff);
            dst_size = src_size + (int)((bits >> 10) & 0x3ff) + 1;
            src += 3;
        }
        else
        {
            // long mode, 18 bit sizes
            if (src_end - src < 5)
                return -1; // too few bytes
            uint bits = (uint)((src[1] << 24) | (src[2] << 16) | (src[3] << 8) | src[4]);
            src_size = (int)(bits & 0x3ffff);
            dst_size = (int)(((bits >> 18) | ((uint)src[0] << 14)) & 0x3FFFF) + 1;
            if (src_size >= dst_size)
                return -1;
            src += 5;
        }
        if (src_end - src < src_size || dst_size > output_size)
            return -1;

        BufferView<byte> dst = output;
        if (dst == scratch)
        {
            if (scratch_end - scratch < dst_size)
                return -1;
            scratch += dst_size;
        }

        int src_used = -1;
        switch (chunk_type)
        {
            case 2:
            case 4:
                src_used = Kraken_DecodeBytes_Type12(src, src_size, dst, dst_size, chunk_type >> 1);
                break;
            case 5:
                src_used = Krak_DecodeRecursive(src, src_size, dst, dst_size, scratch, scratch_end);
                break;
            case 3:
                src_used = Krak_DecodeRLE(src, src_size, dst, dst_size, scratch, scratch_end);
                break;
            case 1:
                src_used = Krak_DecodeTans(src, src_size, dst, dst_size, scratch, scratch_end);
                break;
        }
        if (src_used != src_size)
            return -1;
        decoded_size = dst_size;
        return (int)(src + src_size - src_org);
    }

}
