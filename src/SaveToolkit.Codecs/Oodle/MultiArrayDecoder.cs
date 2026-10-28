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
using static SaveToolkit.Codecs.Oodle.EntropyDecoder;
using static SaveToolkit.Codecs.Oodle.MemoryOps;

namespace SaveToolkit.Codecs.Oodle;

internal static class MultiArrayDecoder
{
    internal static int Kraken_DecodeMultiArray(BufferView<byte> src, BufferView<byte> src_end, BufferView<byte> dst, BufferView<byte> dst_end,
                                               BufferView<byte>[] array_data, BufferView<int> array_lens, int array_count,
                                               ref int total_size_out, bool force_memmove, BufferView<byte> scratch, BufferView<byte> scratch_end)
    {
        BufferView<byte> src_org = src;

        if (src_end - src < 4)
            return -1;

        int decoded_size = 0;
        int num_arrays_in_file = (src++).Value;
        if ((num_arrays_in_file & 0x80) == 0)
            return -1;
        num_arrays_in_file &= 0x3f;

        if (dst == scratch)
        {
            scratch += (scratch_end - scratch - 0xc000) >> 1;
            dst_end = scratch;
        }

        int total_size = 0;

        if (num_arrays_in_file == 0)
        {
            for (int i = 0; i < array_count; i++)
            {
                BufferView<byte> chunk_dst = dst;
                int dec = Kraken_DecodeBytes(ref chunk_dst, src, src_end, ref decoded_size, dst_end - dst, force_memmove, scratch, scratch_end);
                if (dec < 0)
                    return -1;
                dst += decoded_size;
                array_lens[i] = decoded_size;
                array_data[i] = chunk_dst;
                src += dec;
                total_size += decoded_size;
            }
            total_size_out = total_size;
            return (int)(src - src_org);
        }

        BufferView<byte>[] entropy_array_data = new BufferView<byte>[32];
        BufferView<uint> entropy_array_size = BufferView<uint>.Allocate(32, src.Budget);

        // First loop just decodes everything to scratch
        BufferView<byte> scratch_cur = scratch;

        for (int i = 0; i < num_arrays_in_file; i++)
        {
            BufferView<byte> chunk_dst = scratch_cur;
            int dec = Kraken_DecodeBytes(ref chunk_dst, src, src_end, ref decoded_size, scratch_end - scratch_cur, force_memmove, scratch_cur, scratch_end);
            if (dec < 0)
                return -1;
            entropy_array_data[i] = chunk_dst;
            entropy_array_size[i] = (uint)decoded_size;
            scratch_cur += decoded_size;
            total_size += decoded_size;
            src += dec;
        }
        total_size_out = total_size;

        if (src_end - src < 3)
            return -1;

        int Q = ReadU16(src);
        src += 2;

        int out_size = 0;
        if (Kraken_GetBlockSize(src, src_end, ref out_size, total_size) < 0)
            return -1;
        int num_indexes = out_size;

        int num_lens = num_indexes - array_count;
        if (num_lens < 1)
            return -1;

        if (scratch_end - scratch_cur < num_indexes)
            return -1;
        BufferView<byte> interval_lenlog2 = scratch_cur;
        scratch_cur += num_indexes;

        if (scratch_end - scratch_cur < num_indexes)
            return -1;
        BufferView<byte> interval_indexes = scratch_cur;
        scratch_cur += num_indexes;

        if ((Q & 0x8000) != 0)
        {
            int size_out = 0;
            int n = Kraken_DecodeBytes(ref interval_indexes, src, src_end, ref size_out, num_indexes, false, scratch_cur, scratch_end);
            if (n < 0 || size_out != num_indexes)
                return -1;
            src += n;

            for (int i = 0; i < num_indexes; i++)
            {
                int t = interval_indexes[i];
                interval_lenlog2[i] = (byte)(t >> 4);
                interval_indexes[i] = (byte)(t & 0xF);
            }

            num_lens = num_indexes;
        }
        else
        {
            int lenlog2_chunksize = num_indexes - array_count;

            int size_out = 0;
            int n = Kraken_DecodeBytes(ref interval_indexes, src, src_end, ref size_out, num_indexes, false, scratch_cur, scratch_end);
            if (n < 0 || size_out != num_indexes)
                return -1;
            src += n;

            n = Kraken_DecodeBytes(ref interval_lenlog2, src, src_end, ref size_out, lenlog2_chunksize, false, scratch_cur, scratch_end);
            if (n < 0 || size_out != lenlog2_chunksize)
                return -1;
            src += n;

            for (int i = 0; i < lenlog2_chunksize; i++)
                if (interval_lenlog2[i] > 16)
                    return -1;
        }

        if (scratch_end - scratch_cur < 4)
            return -1;

        scratch_cur = AlignPointer(scratch_cur, 4);
        if (scratch_end - scratch_cur < num_lens * 4)
            return -1;
        BufferView<uint> decoded_intervals = (scratch_cur).As<uint>();

        int varbits_complen = Q & 0x3FFF;
        if (src_end - src < varbits_complen)
            return -1;

        BufferView<byte> f = src;
        uint bits_f = 0;
        int bitpos_f = 24;

        BufferView<byte> src_end_actual = src + varbits_complen;

        BufferView<byte> b = src_end_actual;
        uint bits_b = 0;
        int bitpos_b = 24;

        int ii = 0;
        for (ii = 0; ii + 2 <= num_lens; ii += 2)
        {
            bits_f |= ByteSwap(ReadU32(f)) >> (24 - bitpos_f);
            f += (bitpos_f + 7) >> 3;

            bits_b |= ((b).As<uint>())[-1] >> (24 - bitpos_b);
            b -= (bitpos_b + 7) >> 3;

            int numbits_f = interval_lenlog2[ii + 0];
            int numbits_b = interval_lenlog2[ii + 1];

            bits_f = Rotl(bits_f | 1, numbits_f);
            bitpos_f += numbits_f - 8 * ((bitpos_f + 7) >> 3);

            bits_b = Rotl(bits_b | 1, numbits_b);
            bitpos_b += numbits_b - 8 * ((bitpos_b + 7) >> 3);

            int value_f = (int)(bits_f & Bitmasks[numbits_f]);
            bits_f &= ~Bitmasks[numbits_f];

            int value_b = (int)(bits_b & Bitmasks[numbits_b]);
            bits_b &= ~Bitmasks[numbits_b];

            decoded_intervals[ii + 0] = (uint)value_f;
            decoded_intervals[ii + 1] = (uint)value_b;
        }

        // read final one since above loop reads 2
        if (ii < num_lens)
        {
            bits_f |= ByteSwap(ReadU32(f)) >> (24 - bitpos_f);
            int numbits_f = interval_lenlog2[ii];
            bits_f = Rotl(bits_f | 1, numbits_f);
            int value_f = (int)(bits_f & Bitmasks[numbits_f]);
            decoded_intervals[ii + 0] = (uint)value_f;
        }

        if (interval_indexes[num_indexes - 1] != 0)
            return -1;

        int indi = 0, leni = 0, source;
        int increment_leni = (Q & 0x8000) != 0 ? 1 : 0;

        for (int arri = 0; arri < array_count; arri++)
        {
            array_data[arri] = dst;
            if (indi >= num_indexes)
                return -1;

            while ((source = interval_indexes[indi++]) != 0)
            {
                if (source > num_arrays_in_file)
                    return -1;
                if (leni >= num_lens)
                    return -1;
                int cur_len = (int)decoded_intervals[leni++];
                int bytes_left = (int)entropy_array_size[source - 1];
                if (cur_len > bytes_left || cur_len > dst_end - dst)
                    return -1;
                BufferView<byte> blksrc = entropy_array_data[source - 1];
                entropy_array_size[source - 1] -= (uint)cur_len;
                entropy_array_data[source - 1] += cur_len;
                BufferView<byte> dstx = dst;
                dst += cur_len;
                MemMove(dstx, blksrc, cur_len);
            }
            leni += increment_leni;
            array_lens[arri] = (int)(dst - array_data[arri]);
        }

        if (indi != num_indexes || leni != num_lens)
            return -1;

        for (int i = 0; i < num_arrays_in_file; i++)
            if (entropy_array_size[i] != 0)
                return -1;
        return (int)(src_end_actual - src_org);
    }
}
