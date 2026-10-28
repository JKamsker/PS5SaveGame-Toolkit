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

using static SaveToolkit.Codecs.Oodle.EntropyDecoder;
using static SaveToolkit.Codecs.Oodle.MemoryOps;
using static SaveToolkit.Codecs.Oodle.MultiArrayDecoder;

namespace SaveToolkit.Codecs.Oodle;

internal static class RecursiveDecoder
{
    internal static int Krak_DecodeRecursive(BufferView<byte> src, long src_size, BufferView<byte> output, int output_size, BufferView<byte> scratch, BufferView<byte> scratch_end)
    {
        BufferView<byte> src_org = src;
        BufferView<byte> output_end = output + output_size;
        BufferView<byte> src_end = src + src_size;

        if (src_size < 6)
            return -1;

        int n = src[0] & 0x7f;
        if (n < 2)
            return -1;

        if ((src[0] & 0x80) == 0)
        {
            src++;
            do
            {
                int decoded_size = 0;
                int dec = Kraken_DecodeBytes(ref output, src, src_end, ref decoded_size, output_end - output, true, scratch, scratch_end);
                if (dec < 0)
                    return -1;
                output += decoded_size;
                src += dec;
            } while (--n != 0);
            if (output != output_end)
                return -1;
            return (int)(src - src_org);
        }
        else
        {
            BufferView<byte>[] array_data = new BufferView<byte>[1];
            BufferView<int> array_len = BufferView<int>.Allocate(1, src.Budget); int decoded_size = 0;
            int dec = Kraken_DecodeMultiArray(src, src_end, output, output_end, array_data, array_len, 1, ref decoded_size, true, scratch, scratch_end);
            if (dec < 0)
                return -1;
            output += decoded_size;
            if (output != output_end)
                return -1;
            return dec;
        }
    }

    internal static int Krak_DecodeRLE(BufferView<byte> src, long src_size, BufferView<byte> dst, int dst_size, BufferView<byte> scratch, BufferView<byte> scratch_end)
    {
        if (src_size <= 1)
        {
            if (src_size != 1)
                return -1;
            MemSet(dst, src[0], dst_size);
            return 1;
        }
        BufferView<byte> dst_end = dst + dst_size;
        BufferView<byte> cmd_ptr = src + 1, cmd_ptr_end = src + src_size;
        // Unpack the first X bytes of the command buffer?
        if (src[0] != 0)
        {
            BufferView<byte> dst_ptr = scratch;
            int dec_size = 0;
            int n = Kraken_DecodeBytes(ref dst_ptr, src, src + src_size, ref dec_size, scratch_end - scratch, true, scratch, scratch_end);
            if (n <= 0)
                return -1;
            long cmd_len = src_size - n + dec_size;
            if (cmd_len > scratch_end - scratch)
                return -1;
            MemMove(dst_ptr + dec_size, src + n, src_size - n);
            cmd_ptr = dst_ptr;
            cmd_ptr_end = (dst_ptr + (cmd_len));
        }

        byte rle_byte = 0;

        while (cmd_ptr < cmd_ptr_end)
        {
            uint cmd = cmd_ptr_end[-1];
            if (cmd - 1 >= 0x2f)
            {
                cmd_ptr_end--;
                uint bytes_to_copy = unchecked((uint)-1 - cmd) & 0xF;
                uint bytes_to_rle = cmd >> 4;
                if (dst_end - dst < bytes_to_copy + bytes_to_rle || cmd_ptr_end - cmd_ptr < bytes_to_copy)
                    return -1;
                MemMove(dst, cmd_ptr, bytes_to_copy);
                cmd_ptr += bytes_to_copy;
                dst += bytes_to_copy;
                MemSet(dst, rle_byte, bytes_to_rle);
                dst += bytes_to_rle;
            }
            else if (cmd >= 0x10)
            {
                uint data = (uint)ReadU16(cmd_ptr_end - 2) - 4096;
                cmd_ptr_end -= 2;
                uint bytes_to_copy = data & 0x3F;
                uint bytes_to_rle = data >> 6;
                if (dst_end - dst < bytes_to_copy + bytes_to_rle || cmd_ptr_end - cmd_ptr < bytes_to_copy)
                    return -1;
                MemMove(dst, cmd_ptr, bytes_to_copy);
                cmd_ptr += bytes_to_copy;
                dst += bytes_to_copy;
                MemSet(dst, rle_byte, bytes_to_rle);
                dst += bytes_to_rle;
            }
            else if (cmd == 1)
            {
                rle_byte = (cmd_ptr++).Value;
                cmd_ptr_end--;
            }
            else if (cmd >= 9)
            {
                uint bytes_to_rle = ((uint)ReadU16(cmd_ptr_end - 2) - 0x8ff) * 128;
                cmd_ptr_end -= 2;
                if (dst_end - dst < bytes_to_rle)
                    return -1;
                MemSet(dst, rle_byte, bytes_to_rle);
                dst += bytes_to_rle;
            }
            else
            {
                uint bytes_to_copy = ((uint)ReadU16(cmd_ptr_end - 2) - 511) * 64;
                cmd_ptr_end -= 2;
                if (cmd_ptr_end - cmd_ptr < bytes_to_copy || dst_end - dst < bytes_to_copy)
                    return -1;
                MemMove(dst, cmd_ptr, bytes_to_copy);
                dst += bytes_to_copy;
                cmd_ptr += bytes_to_copy;
            }
        }
        if (cmd_ptr_end != cmd_ptr)
            return -1;

        if (dst != dst_end)
            return -1;

        return (int)src_size;
    }

}
