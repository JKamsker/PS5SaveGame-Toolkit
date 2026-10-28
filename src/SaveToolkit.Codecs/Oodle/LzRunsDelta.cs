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

internal static class LzRunsDelta
{
    internal static bool Kraken_ProcessLzRuns_Type0(KrakenLzTable lzt, BufferView<byte> dst, BufferView<byte> dst_end, BufferView<byte> dst_start)
    {
        BufferView<byte> cmd_stream = lzt.cmd_stream, cmd_stream_end = cmd_stream + lzt.cmd_stream_size;
        BufferView<int> len_stream = lzt.len_stream;
        BufferView<int> len_stream_end = lzt.len_stream + lzt.len_stream_size;
        BufferView<byte> lit_stream = lzt.lit_stream;
        BufferView<byte> lit_stream_end = lzt.lit_stream + lzt.lit_stream_size;
        BufferView<int> offs_stream = lzt.offs_stream;
        BufferView<int> offs_stream_end = lzt.offs_stream + lzt.offs_stream_size;
        BufferView<byte> copyfrom;
        uint final_len = 0;
        int offset = 0;
        BufferView<int> recent_offs = BufferView<int>.Allocate(7, dst.Budget);
        int last_offset = 0;

        recent_offs[3] = -8;
        recent_offs[4] = -8;
        recent_offs[5] = -8;
        last_offset = -8;

        while (cmd_stream < cmd_stream_end)
        {
            uint f = (cmd_stream++).Value;
            uint litlen = f & 3;
            uint offs_index = f >> 6;
            uint matchlen = (f >> 2) & 0xF;

            uint next_long_length = (uint)len_stream.Value;
            BufferView<int> next_len_stream = len_stream + 1;

            len_stream = litlen == 3 ? next_len_stream : len_stream;
            litlen = litlen == 3 ? next_long_length : litlen;
            recent_offs[6] = (offs_stream).Value;

            Copy64Add(dst, lit_stream, (dst + (last_offset)));
            if (litlen > 8)
            {
                Copy64Add(dst + 8, lit_stream + 8, (dst + (last_offset + 8)));
                if (litlen > 16)
                {
                    Copy64Add(dst + 16, lit_stream + 16, (dst + (last_offset + 16)));
                    if (litlen > 24)
                    {
                        do
                        {
                            Copy64Add(dst + 24, lit_stream + 24, (dst + (last_offset + 24)));
                            litlen -= 8;
                            dst += 8;
                            lit_stream += 8;
                        } while (litlen > 24);
                    }
                }
            }
            dst += litlen;
            lit_stream += litlen;

            offset = recent_offs[offs_index + 3];
            recent_offs[offs_index + 3] = recent_offs[offs_index + 2];
            recent_offs[offs_index + 2] = recent_offs[offs_index + 1];
            recent_offs[offs_index + 1] = recent_offs[offs_index + 0];
            recent_offs[3] = offset;
            last_offset = offset;

            offs_stream = (((offs_stream).As<byte>() + ((offs_index + 1) & 4))).As<int>();

            if ((nuint)(nint)offset < (nuint)(dst_start - dst))
                return false; // offset out of bounds

            copyfrom = dst + offset;
            if (matchlen != 15)
            {
                Copy64(dst, copyfrom);
                Copy64(dst + 8, copyfrom + 8);
                dst += matchlen + 2;
            }
            else
            {
                matchlen = 14 + (uint)(len_stream++).Value;
                if ((nuint)matchlen > (nuint)(dst_end - dst))
                    return false; // copy length out of bounds
                Copy64(dst, copyfrom);
                Copy64(dst + 8, copyfrom + 8);
                Copy64(dst + 16, copyfrom + 16);
                do
                {
                    Copy64(dst + 24, copyfrom + 24);
                    matchlen -= 8;
                    dst += 8;
                    copyfrom += 8;
                } while (matchlen > 24);
                dst += matchlen;
            }
        }

        // check for incorrect input
        if (offs_stream != offs_stream_end || len_stream != len_stream_end)
            return false;

        final_len = (uint)(dst_end - dst);
        if (final_len != lit_stream_end - lit_stream)
            return false;

        if (final_len >= 8)
        {
            do
            {
                Copy64Add(dst, lit_stream, (dst + (last_offset)));
                dst += 8; lit_stream += 8; final_len -= 8;
            } while (final_len >= 8);
        }
        if (final_len > 0)
        {
            do
            {
                (dst).Value = (byte)((lit_stream++).Value + dst[last_offset]);
                dst++;
            } while (--final_len != 0);
        }
        return true;
    }
}
