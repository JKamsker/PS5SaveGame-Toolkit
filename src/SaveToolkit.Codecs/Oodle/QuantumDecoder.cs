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
using static SaveToolkit.Codecs.Oodle.LzRunsDelta;
using static SaveToolkit.Codecs.Oodle.LzRunsRaw;
using static SaveToolkit.Codecs.Oodle.LzTables;
using static SaveToolkit.Codecs.Oodle.MemoryOps;

namespace SaveToolkit.Codecs.Oodle;

internal static class QuantumDecoder
{
    internal static bool Kraken_ProcessLzRuns(int mode, BufferView<byte> dst, int dst_size, long offset, KrakenLzTable lztable)
    {
        BufferView<byte> dst_end = dst + dst_size;
        if (mode == 1)
            return Kraken_ProcessLzRuns_Type1(lztable, dst + (offset == 0 ? 8 : 0), dst_end, dst - offset);
        if (mode == 0)
            return Kraken_ProcessLzRuns_Type0(lztable, dst + (offset == 0 ? 8 : 0), dst_end, dst - offset);
        return false;
    }

    /// <summary>Decode one 256 KiB quantum: two 128 KiB blocks compressed separately with shared history.</summary>
    internal static int Kraken_DecodeQuantum(BufferView<byte> dst, BufferView<byte> dst_end, BufferView<byte> dst_start, BufferView<byte> src, BufferView<byte> src_end,
                                            BufferView<byte> scratch, BufferView<byte> scratch_end)
    {
        BufferView<byte> src_in = src;
        int mode = 0, chunkhdr = 0, dst_count = 0, src_used = 0, written_bytes = 0;

        while (dst_end - dst != 0)
        {
            dst_count = (int)(dst_end - dst);
            if (dst_count > 0x20000) dst_count = 0x20000;
            if (src_end - src < 4)
                return -1;
            chunkhdr = src[2] | src[1] << 8 | src[0] << 16;
            if ((chunkhdr & 0x800000) == 0)
            {
                // Stored as entropy without any match copying.
                BufferView<byte> outp = dst;
                src_used = Kraken_DecodeBytes(ref outp, src, src_end, ref written_bytes, dst_count, false, scratch, scratch_end);
                if (src_used < 0 || written_bytes != dst_count)
                    return -1;
                // The original leaves the bytes where Kraken_DecodeBytes put them; a stored (type 0) chunk
                // points |outp| into the source instead of copying, so copy it into place here.
                if (outp != dst) MemMove(dst, outp, dst_count);
            }
            else
            {
                src += 3;
                src_used = chunkhdr & 0x7FFFF;
                mode = (chunkhdr >> 19) & 0xF;
                if (src_end - src < src_used)
                    return -1;
                if (src_used < dst_count)
                {
                    long scratch_usage = Min(Min(3 * dst_count + 32 + 0xd000, 0x6C000), scratch_end - scratch);
                    KrakenLzTable lzt = new();
                    if (!Kraken_ReadLzTable(mode, src, src + src_used, dst, dst_count, dst - dst_start,
                                            scratch, scratch + scratch_usage, lzt))
                        return -1;
                    if (!Kraken_ProcessLzRuns(mode, dst, dst_count, dst - dst_start, lzt))
                        return -1;
                }
                else if (src_used > dst_count || mode != 0)
                    return -1;
                else
                    MemMove(dst, src, dst_count);
            }
            src += src_used;
            dst += dst_count;
        }
        return (int)(src - src_in);
    }

    internal static void Kraken_CopyWholeMatch(BufferView<byte> dst, uint offset, long length)
    {
        long i = 0;
        BufferView<byte> src = dst - offset;
        if (offset >= 8)
            for (; i + 8 <= length; i += 8)
                (((dst + i)).As<ulong>()).Value = (((src + i)).As<ulong>()).Value;
        for (; i < length; i++)
            dst[i] = src[i];
    }

}
