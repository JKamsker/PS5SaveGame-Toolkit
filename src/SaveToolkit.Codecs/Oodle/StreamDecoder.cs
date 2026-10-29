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
using static SaveToolkit.Codecs.Oodle.QuantumDecoder;
using static SaveToolkit.Codecs.Oodle.StreamHeaders;

namespace SaveToolkit.Codecs.Oodle;

internal static class StreamDecoder
{
    private const int ScratchBytes = 0x6C000;
    private const int ScratchPaddingBytes = 64;

    internal sealed class KrakenDecoder
    {
        public int src_used, dst_used;
        public BufferView<byte> scratch;
        public KrakenHeader hdr = new();
    }

    internal static bool Kraken_DecodeStep(KrakenDecoder dec, BufferView<byte> dst_start, int offset, long dst_bytes_left_in,
                                          BufferView<byte> src, long src_bytes_left)
    {
        BufferView<byte> src_in = src;
        BufferView<byte> src_end = src + src_bytes_left;
        KrakenQuantumHeader qhdr = new();
        int n = 0;

        if ((offset & 0x3FFFF) == 0)
        {
            src = Kraken_ParseHeader(dec.hdr, src);
            if (src == default)
                return false;
        }

        // Only Kraken is ported. Mermaid, Selkie, Leviathan, LZNA and Bitknit are refused, not guessed at.
        if (dec.hdr.decoder_type != 6)
            throw new InvalidDataException($"Oodle codec {dec.hdr.decoder_type} is not supported; only Kraken (6) is");
        if (dec.hdr.use_checksums)
            return false; // Upstream's quantum checksum is a stub; never silently skip it.

        int dst_bytes_left = (int)Min(0x40000, dst_bytes_left_in);

        if (dec.hdr.uncompressed)
        {
            if (src_end - src < dst_bytes_left)
            {
                dec.src_used = dec.dst_used = 0;
                return true;
            }
            MemMove(dst_start + offset, src, dst_bytes_left);
            dec.src_used = (int)(src - src_in) + dst_bytes_left;
            dec.dst_used = dst_bytes_left;
            return true;
        }

        src = Kraken_ParseQuantumHeader(qhdr, src, dec.hdr.use_checksums);

        if (src == default || src > src_end)
            return false;

        // Too few bytes in buffer to make any progress?
        if ((nuint)(src_end - src) < qhdr.compressed_size)
        {
            dec.src_used = dec.dst_used = 0;
            return true;
        }

        if (qhdr.compressed_size > (uint)dst_bytes_left)
            return false;

        if (qhdr.compressed_size == 0)
        {
            if (qhdr.whole_match_distance != 0)
            {
                if (qhdr.whole_match_distance > (uint)offset)
                    return false;
                Kraken_CopyWholeMatch(dst_start + offset, qhdr.whole_match_distance, dst_bytes_left);
            }
            else
                MemSet(dst_start + offset, (byte)qhdr.checksum, dst_bytes_left);
            dec.src_used = (int)(src - src_in);
            dec.dst_used = dst_bytes_left;
            return true;
        }

        if (qhdr.compressed_size == dst_bytes_left)
        {
            MemMove(dst_start + offset, src, dst_bytes_left);
            dec.src_used = (int)(src - src_in) + dst_bytes_left;
            dec.dst_used = dst_bytes_left;
            return true;
        }

        // Stored and memset quanta never use entropy scratch. Allocate it only for the first
        // compressed quantum and retain the same owned, padded buffer for the rest of this stream.
        if (dec.scratch == default)
            dec.scratch = BufferView<byte>.Allocate(ScratchBytes + ScratchPaddingBytes, src.Budget);
        n = Kraken_DecodeQuantum(dst_start + offset, dst_start + offset + dst_bytes_left, dst_start,
                                 src, src + qhdr.compressed_size, dec.scratch, dec.scratch + ScratchBytes);

        if (n != qhdr.compressed_size)
            return false;

        dec.src_used = (int)(src - src_in) + n;
        dec.dst_used = dst_bytes_left;
        return true;
    }

    internal static int DecompressCore(BufferView<byte> src, long src_len, BufferView<byte> dst, long dst_len)
    {
        KrakenDecoder dec = new();
        int offset = 0;
        while (dst_len != 0)
        {
            if (!Kraken_DecodeStep(dec, dst, offset, dst_len, src, src_len))
                return -1;
            if (dec.src_used == 0)
                return -1;
            src += dec.src_used;
            src_len -= dec.src_used;
            dst_len -= dec.dst_used;
            offset += dec.dst_used;
        }
        if (src_len != 0)
            return -1;
        return offset;
    }
}
