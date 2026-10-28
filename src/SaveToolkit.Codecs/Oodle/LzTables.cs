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
using static SaveToolkit.Codecs.Oodle.EntropyDecoder;
using static SaveToolkit.Codecs.Oodle.LzOffsets;
using static SaveToolkit.Codecs.Oodle.MemoryOps;

namespace SaveToolkit.Codecs.Oodle;

internal static class LzTables
{
    internal static bool Kraken_ReadLzTable(int mode, BufferView<byte> src, BufferView<byte> src_end, BufferView<byte> dst, int dst_size, long offset,
                                           BufferView<byte> scratch, BufferView<byte> scratch_end, KrakenLzTable lztable)
    {
        BufferView<byte> outp = default;
        int decode_count = 0, n = 0;
        BufferView<byte> packed_offs_stream;
        BufferView<byte> packed_len_stream;

        if (mode > 1)
            return false;

        if (src_end - src < 13)
            return false;

        if (offset == 0)
        {
            Copy64(dst, src);
            dst += 8;
            src += 8;
        }

        if (((src).Value & 0x80) != 0)
        {
            byte flag = (src++).Value;
            if ((flag & 0xc0) != 0x80)
                return false; // reserved flag set

            return false; // excess bytes not supported
        }

        // Disable no copy optimization if source and dest overlap
        bool force_copy = dst <= src_end && src <= dst + dst_size;

        // Decode lit stream, bounded by dst_size
        outp = scratch;
        n = Kraken_DecodeBytes(ref outp, src, src_end, ref decode_count, Min(scratch_end - scratch, dst_size), force_copy, scratch, scratch_end);
        if (n < 0)
            return false;
        src += n;
        lztable.lit_stream = outp;
        lztable.lit_stream_size = decode_count;
        scratch += decode_count;

        // Decode command stream, bounded by dst_size
        outp = scratch;
        n = Kraken_DecodeBytes(ref outp, src, src_end, ref decode_count, Min(scratch_end - scratch, dst_size), force_copy, scratch, scratch_end);
        if (n < 0)
            return false;
        src += n;
        lztable.cmd_stream = outp;
        lztable.cmd_stream_size = decode_count;
        scratch += decode_count;

        // Check whether distances use separate scale and low-bit streams.
        if (src_end - src < 3)
            return false;

        int offs_scaling = 0;
        BufferView<byte> packed_offs_stream_extra = default;

        if ((src[0] & 0x80) != 0)
        {
            // uses the mode where distances are coded with 2 tables
            offs_scaling = src[0] - 127;
            src++;

            packed_offs_stream = scratch;
            n = Kraken_DecodeBytes(ref packed_offs_stream, src, src_end, ref lztable.offs_stream_size,
                                   Min(scratch_end - scratch, lztable.cmd_stream_size), false, scratch, scratch_end);
            if (n < 0)
                return false;
            src += n;
            scratch += lztable.offs_stream_size;

            if (offs_scaling != 1)
            {
                packed_offs_stream_extra = scratch;
                n = Kraken_DecodeBytes(ref packed_offs_stream_extra, src, src_end, ref decode_count,
                                       Min(scratch_end - scratch, lztable.offs_stream_size), false, scratch, scratch_end);
                if (n < 0 || decode_count != lztable.offs_stream_size)
                    return false;
                src += n;
                scratch += decode_count;
            }
        }
        else
        {
            // Decode packed offset stream, it's bounded by the command length.
            packed_offs_stream = scratch;
            n = Kraken_DecodeBytes(ref packed_offs_stream, src, src_end, ref lztable.offs_stream_size,
                                   Min(scratch_end - scratch, lztable.cmd_stream_size), false, scratch, scratch_end);
            if (n < 0)
                return false;
            src += n;
            scratch += lztable.offs_stream_size;
        }

        // Decode packed litlen stream. It's bounded by 1/4 of dst_size.
        packed_len_stream = scratch;
        n = Kraken_DecodeBytes(ref packed_len_stream, src, src_end, ref lztable.len_stream_size,
                               Min(scratch_end - scratch, dst_size >> 2), false, scratch, scratch_end);
        if (n < 0)
            return false;
        src += n;
        scratch += lztable.len_stream_size;

        // Reserve memory for final dist stream
        scratch = AlignPointer(scratch, 16);
        lztable.offs_stream = (scratch).As<int>();
        scratch += lztable.offs_stream_size * 4;

        // Reserve memory for final len stream
        scratch = AlignPointer(scratch, 16);
        lztable.len_stream = (scratch).As<int>();
        scratch += lztable.len_stream_size * 4;

        if (scratch + 64 > scratch_end)
            return false;

        return Kraken_UnpackOffsets(src, src_end, packed_offs_stream, packed_offs_stream_extra,
                                    lztable.offs_stream_size, offs_scaling,
                                    packed_len_stream, lztable.len_stream_size,
                                    lztable.offs_stream, lztable.len_stream, false, 0);
    }
}
