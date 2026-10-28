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

namespace SaveToolkit.Codecs.Oodle;

internal static class StreamHeaders
{
    internal static BufferView<byte> Kraken_ParseHeader(KrakenHeader hdr, BufferView<byte> p)
    {
        int b = p[0];
        if ((b & 0xF) == 0xC)
        {
            if (((b >> 4) & 3) != 0) return default;
            hdr.restart_decoder = ((b >> 7) & 1) != 0;
            hdr.uncompressed = ((b >> 6) & 1) != 0;
            b = p[1];
            hdr.decoder_type = b & 0x7F;
            hdr.use_checksums = (b >> 7) != 0;
            if (hdr.decoder_type != 6 && hdr.decoder_type != 10 && hdr.decoder_type != 5 && hdr.decoder_type != 11 && hdr.decoder_type != 12)
                return default;
            return p + 2;
        }
        return default;
    }

    internal static BufferView<byte> Kraken_ParseQuantumHeader(KrakenQuantumHeader hdr, BufferView<byte> p, bool use_checksum)
    {
        uint v = (uint)((p[0] << 16) | (p[1] << 8) | p[2]);
        uint size = v & 0x3FFFF;
        if (size != 0x3ffff)
        {
            hdr.compressed_size = size + 1;
            hdr.flag1 = (byte)((v >> 18) & 1);
            hdr.flag2 = (byte)((v >> 19) & 1);
            if (use_checksum)
            {
                hdr.checksum = (uint)((p[3] << 16) | (p[4] << 8) | p[5]);
                return p + 6;
            }
            return p + 3;
        }
        v >>= 18;
        if (v == 1)
        {
            // memset
            hdr.checksum = p[3];
            hdr.compressed_size = 0;
            hdr.whole_match_distance = 0;
            return p + 4;
        }
        return default;
    }

}
