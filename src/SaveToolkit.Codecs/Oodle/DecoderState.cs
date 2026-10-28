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

using System.Runtime.InteropServices;


namespace SaveToolkit.Codecs.Oodle;

internal static class DecoderState
{
    internal sealed class KrakenHeader
    {
        public int decoder_type;
        public bool restart_decoder;
        public bool uncompressed;
        public bool use_checksums;
    }

    internal sealed class KrakenQuantumHeader
    {
        public uint compressed_size;
        public uint checksum;
        public byte flag1;
        public byte flag2;
        public uint whole_match_distance;
    }

    internal sealed class KrakenLzTable
    {
        public BufferView<byte> cmd_stream;
        public int cmd_stream_size;
        public BufferView<int> offs_stream;
        public int offs_stream_size;
        public BufferView<byte> lit_stream;
        public int lit_stream_size;
        public BufferView<int> len_stream;
        public int len_stream_size;
    }

    internal sealed class BitReader
    {
        public BufferView<byte> p, p_end;
        public uint bits;
        public int bitpos;
    }

    internal sealed class BitReader2
    {
        public BufferView<byte> p, p_end;
        public uint bitpos;
    }

    internal sealed class HuffReader
    {
        public BufferView<byte> output, output_end;
        public BufferView<byte> src, src_mid, src_end, src_mid_org;
        public int src_bitpos, src_mid_bitpos, src_end_bitpos;
        public uint src_bits, src_mid_bits, src_end_bits;
    }

    internal struct HuffRange
    {
        public ushort symbol;
        public ushort num;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct TansLutEnt
    {
        public uint x;
        public byte bits_x;
        public byte symbol;
        public ushort w;
    }

    internal sealed class TansDecoderParams
    {
        public BufferView<TansLutEnt> lut;
        public BufferView<byte> dst, dst_end;
        public BufferView<byte> ptr_f, ptr_b;
        public uint bits_f, bits_b;
        public int bitpos_f, bitpos_b;
        public uint state_0, state_1, state_2, state_3, state_4;
    }

}
