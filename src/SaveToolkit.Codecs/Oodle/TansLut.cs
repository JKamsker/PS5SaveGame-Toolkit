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
using static SaveToolkit.Codecs.Oodle.TansTables;

namespace SaveToolkit.Codecs.Oodle;

internal static class TansLut
{
    internal static void Tans_InitLut(TansData tans_data, int L_bits, BufferView<TansLutEnt> lut)
    {
        BufferView<TansLutEnt>[] pointers = new BufferView<TansLutEnt>[4];

        int L = 1 << L_bits;
        int a_used = (int)tans_data.A_used;

        uint slots_left_to_alloc = (uint)(L - a_used);

        uint sa = slots_left_to_alloc >> 2;
        pointers[0] = lut;
        uint sb = sa + ((slots_left_to_alloc & 3) > 0 ? 1u : 0u);
        pointers[1] = lut + sb;
        sb += sa + ((slots_left_to_alloc & 3) > 1 ? 1u : 0u);
        pointers[2] = lut + sb;
        sb += sa + ((slots_left_to_alloc & 3) > 2 ? 1u : 0u);
        pointers[3] = lut + sb;

        // Setup the single entrys with weight=1
        {
            BufferView<TansLutEnt> lut_singles = lut + slots_left_to_alloc;
            TansLutEnt le = default;
            le.w = 0;
            le.bits_x = (byte)L_bits;
            le.x = (1u << L_bits) - 1;
            le.symbol = 0;
            for (int i = 0; i < a_used; i++)
            {
                lut_singles[i] = le;
                lut_singles[i].symbol = tans_data.A[i];
            }
        }

        // Setup the entrys with weight >= 2
        int weights_sum = 0;
        for (int i = 0; i < tans_data.B_used; i++)
        {
            int weight = (int)(tans_data.B[i] & 0xffff);
            int symbol = (int)(tans_data.B[i] >> 16);
            if (weight > 4)
            {
                uint sym_bits = BSR((uint)weight);
                int Z = L_bits - (int)sym_bits;
                TansLutEnt le = default;
                le.symbol = (byte)symbol;
                le.bits_x = (byte)Z;
                le.x = (1u << Z) - 1;
                le.w = (ushort)((L - 1) & (weight << Z));
                int what_to_add = 1 << Z;
                int X = (1 << (int)(sym_bits + 1)) - weight;

                for (int j = 0; j < 4; j++)
                {
                    BufferView<TansLutEnt> dst = pointers[j];

                    int Y = (weight + ((weights_sum - j - 1) & 3)) >> 2;
                    if (X >= Y)
                    {
                        for (int n = Y; n != 0; n--)
                        {
                            (dst++).Value = le;
                            le.w = (ushort)(le.w + what_to_add);
                        }
                        X -= Y;
                    }
                    else
                    {
                        for (int n = X; n != 0; n--)
                        {
                            (dst++).Value = le;
                            le.w = (ushort)(le.w + what_to_add);
                        }
                        Z--;

                        what_to_add >>= 1;
                        le.bits_x = (byte)Z;
                        le.w = 0;
                        le.x >>= 1;
                        for (int n = Y - X; n != 0; n--)
                        {
                            (dst++).Value = le;
                            le.w = (ushort)(le.w + what_to_add);
                        }
                        X = weight;
                    }
                    pointers[j] = dst;
                }
            }
            else
            {
                uint bits = ((1u << weight) - 1) << (weights_sum & 3);
                bits |= bits >> 4;
                int n = weight, ww = weight;
                do
                {
                    uint idx = BSF(bits);
                    bits &= bits - 1;
                    BufferView<TansLutEnt> dst = pointers[idx]++;
                    dst.Value.symbol = (byte)symbol;
                    uint weight_bits = BSR((uint)ww);
                    dst.Value.bits_x = (byte)(L_bits - (int)weight_bits);
                    dst.Value.x = (1u << (L_bits - (int)weight_bits)) - 1;
                    dst.Value.w = (ushort)((L - 1) & (ww++ << (L_bits - (int)weight_bits)));
                } while (--n != 0);
            }
            weights_sum += weight;
        }
    }
}
