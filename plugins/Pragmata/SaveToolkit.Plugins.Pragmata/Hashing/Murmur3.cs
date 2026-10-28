using System.Buffers.Binary;
using System.Numerics;

namespace SaveToolkit.Plugins.Pragmata.Hashing;

internal static class Murmur3
{
    // MurmurHash3 x86-32; RE Engine uses the non-default all-ones seed.
    public static uint Hash(ReadOnlySpan<byte> data, uint seed = uint.MaxValue)
    {
        unchecked
        {
            var hash = seed;
            var end = data.Length & ~3;
            for (var offset = 0; offset < end; offset += 4)
            {
                hash ^= Mix(BinaryPrimitives.ReadUInt32LittleEndian(data[offset..]));
                hash = BitOperations.RotateLeft(hash, 13) * 5 + 0xe6546b64;
            }

            uint tail = 0;
            for (var i = end; i < data.Length; i++)
                tail |= (uint)data[i] << ((i - end) * 8);
            if (end != data.Length)
                hash ^= Mix(tail);
            hash ^= (uint)data.Length;
            hash ^= hash >> 16;
            hash *= 0x85ebca6b;
            hash ^= hash >> 13;
            hash *= 0xc2b2ae35;
            return hash ^ (hash >> 16);
        }
    }

    private static uint Mix(uint value) => unchecked(BitOperations.RotateLeft(value * 0xcc9e2d51, 15) * 0x1b873593);
}
