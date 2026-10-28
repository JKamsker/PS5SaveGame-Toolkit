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

using System.Buffers.Binary;
using System.Numerics;


namespace SaveToolkit.Codecs.Oodle;

internal static class MemoryOps
{
    internal static uint BSR(uint x) => (uint)BitOperations.Log2(x);
    internal static uint BSF(uint x) => (uint)BitOperations.TrailingZeroCount(x);
    internal static int CountLeadingZeros(uint bits) => BitOperations.LeadingZeroCount(bits);
    internal static uint Rotl(uint x, int n) => BitOperations.RotateLeft(x, n);
    internal static uint ByteSwap(uint x) => BinaryPrimitives.ReverseEndianness(x);
    internal static ulong ByteSwap(ulong x) => BinaryPrimitives.ReverseEndianness(x);
    internal static uint ReadU32(BufferView<byte> p) => p.As<uint>().Value;
    internal static ushort ReadU16(BufferView<byte> p) => p.As<ushort>().Value;
    internal static void Copy64(BufferView<byte> d, BufferView<byte> s) => d.As<ulong>().Value = s.As<ulong>().Value;
    internal static long Min(long a, long b) => Math.Min(a, b);
    internal static void MemSet(BufferView<byte> d, byte v, long n) { if (n > 0) d.Bytes(n).Fill(v); }
    internal static void MemMove(BufferView<byte> d, BufferView<byte> s, long n) { if (n > 0) s.Bytes(n).CopyTo(d.Bytes(n)); }
    internal static BufferView<byte> AlignPointer(BufferView<byte> p, int align) => p + ((-p.Offset) & (align - 1));

    /// <summary>_mm_add_epi8 over 8 bytes: d[i] = s[i] + t[i] mod 256, with both read before d is written.</summary>
    internal static void Copy64Add(BufferView<byte> d, BufferView<byte> s, BufferView<byte> t)
    {
        ulong a = ((s).As<ulong>()).Value, b = ((t).As<ulong>()).Value;
        const ulong H = 0x8080808080808080UL;
        ((d).As<ulong>()).Value = ((a & ~H) + (b & ~H)) ^ ((a ^ b) & H);
    }

    internal static void Copy64Bytes(BufferView<byte> d, BufferView<byte> s)
    {
        for (int i = 0; i < 64; i += 16)
        {
            ulong lo = (((s + i)).As<ulong>()).Value, hi = (((s + i + 8)).As<ulong>()).Value;
            (((d + i)).As<ulong>()).Value = lo;
            (((d + i + 8)).As<ulong>()).Value = hi;
        }
    }
}
