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

internal static class LzOffsets
{
    internal static void CombineScaledOffsetArrays(BufferView<int> offs_stream, long offs_stream_size, int scale, BufferView<byte> low_bits)
    {
        for (long i = 0; i != offs_stream_size; i++)
            offs_stream[i] = scale * offs_stream[i] - low_bits[i];
    }

    internal static bool Kraken_UnpackOffsets(BufferView<byte> src, BufferView<byte> src_end,
                                             BufferView<byte> packed_offs_stream, BufferView<byte> packed_offs_stream_extra, int packed_offs_stream_size,
                                             int multi_dist_scale,
                                             BufferView<byte> packed_litlen_stream, int packed_litlen_stream_size,
                                             BufferView<int> offs_stream, BufferView<int> len_stream,
                                             bool excess_flag, int excess_bytes)
    {
        BitReader bits_a = new(), bits_b = new();
        int n = 0, i = 0;
        int u32_len_stream_size = 0;

        bits_a.bitpos = 24;
        bits_a.bits = 0;
        bits_a.p = src;
        bits_a.p_end = src_end;
        BitReader_Refill(bits_a);

        bits_b.bitpos = 24;
        bits_b.bits = 0;
        bits_b.p = src_end;
        bits_b.p_end = src;
        BitReader_RefillBackwards(bits_b);

        if (!excess_flag)
        {
            if (bits_b.bits < 0x2000)
                return false;
            n = 31 - (int)BSR(bits_b.bits);
            bits_b.bitpos += n;
            bits_b.bits <<= n;
            BitReader_RefillBackwards(bits_b);
            n++;
            u32_len_stream_size = (int)(bits_b.bits >> (32 - n)) - 1;
            bits_b.bitpos += n;
            bits_b.bits <<= n;
            BitReader_RefillBackwards(bits_b);
        }

        if (multi_dist_scale == 0)
        {
            // Traditional way of coding offsets
            BufferView<byte> packed_offs_stream_end = packed_offs_stream + packed_offs_stream_size;
            while (packed_offs_stream != packed_offs_stream_end)
            {
                (offs_stream++).Value = -(int)BitReader_ReadDistance(bits_a, (packed_offs_stream++).Value);
                if (packed_offs_stream == packed_offs_stream_end)
                    break;
                (offs_stream++).Value = -(int)BitReader_ReadDistanceB(bits_b, (packed_offs_stream++).Value);
            }
        }
        else
        {
            // New way of coding offsets
            BufferView<int> offs_stream_org = offs_stream;
            BufferView<byte> packed_offs_stream_end = packed_offs_stream + packed_offs_stream_size;
            uint cmd = 0, offs = 0;
            while (packed_offs_stream != packed_offs_stream_end)
            {
                cmd = (packed_offs_stream++).Value;
                if ((cmd >> 3) > 26)
                    return false;
                offs = ((8 + (cmd & 7)) << (int)(cmd >> 3)) | BitReader_ReadMoreThan24Bits(bits_a, (int)(cmd >> 3));
                (offs_stream++).Value = 8 - (int)offs;
                if (packed_offs_stream == packed_offs_stream_end)
                    break;
                cmd = (packed_offs_stream++).Value;
                if ((cmd >> 3) > 26)
                    return false;
                offs = ((8 + (cmd & 7)) << (int)(cmd >> 3)) | BitReader_ReadMoreThan24BitsB(bits_b, (int)(cmd >> 3));
                (offs_stream++).Value = 8 - (int)offs;
            }
            if (multi_dist_scale != 1)
                CombineScaledOffsetArrays(offs_stream_org, offs_stream - offs_stream_org, multi_dist_scale, packed_offs_stream_extra);
        }
        BufferView<uint> u32_len_stream_buf = BufferView<uint>.Allocate(512, src.Budget); // max count is 128kb / 256 = 512
        if (u32_len_stream_size > 512)
            return false;

        BufferView<uint> u32_len_stream = u32_len_stream_buf, u32_len_stream_end = u32_len_stream_buf + u32_len_stream_size;
        for (i = 0; i + 1 < u32_len_stream_size; i += 2)
        {
            if (!BitReader_ReadLength(bits_a, ref u32_len_stream[i + 0]))
                return false;
            if (!BitReader_ReadLengthB(bits_b, ref u32_len_stream[i + 1]))
                return false;
        }
        if (i < u32_len_stream_size)
        {
            if (!BitReader_ReadLength(bits_a, ref u32_len_stream[i + 0]))
                return false;
        }

        bits_a.p -= (24 - bits_a.bitpos) >> 3;
        bits_b.p += (24 - bits_b.bitpos) >> 3;

        if (bits_a.p != bits_b.p)
            return false;

        for (i = 0; i < packed_litlen_stream_size; i++)
        {
            uint v = packed_litlen_stream[i];
            if (v == 255)
                v = (u32_len_stream++).Value + 255;
            len_stream[i] = (int)(v + 3);
        }
        if (u32_len_stream != u32_len_stream_end)
            return false;

        return true;
    }
}
