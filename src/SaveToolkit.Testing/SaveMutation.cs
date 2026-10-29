using System.Buffers.Binary;

namespace SaveToolkit.Testing;

/// <summary>A reproducible, bounded mutation of one original file (never a previous mutation).</summary>
public sealed record SaveMutation(byte[] Content, string Description)
{
    public const uint DefaultSeed = 0x40C0FFEE;
    private const int MutationKinds = 6;
    private const int MaxInsertedBytes = 16;
    private const int MaxDeletedBytes = 32;
    private const int MaxDuplicatedBytes = 256;
    private const int HeaderWindow = 128;

    /// <summary>Zero-based indices are independent; changing the run length cannot change a reproducer.</summary>
    public static SaveMutation Create(ReadOnlySpan<byte> original, uint seed, int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        var random = new MutationRandom(unchecked(seed + (uint)index));
        var offset = random.Next(original.Length);
        switch (index % MutationKinds)
        {
            case 0 when !original.IsEmpty:
                var flipped = original.ToArray();
                var bit = random.Next(8);
                flipped[offset] ^= (byte)(1 << bit);
                return new(flipped, $"bit flip at {offset}, bit {bit}");
            case 2 when !original.IsEmpty:
                var deleted = Math.Min(original.Length - offset, 1 + random.Next(MaxDeletedBytes));
                return new([.. original[..offset], .. original[(offset + deleted)..]], $"delete {deleted} at {offset}");
            case 3:
                var length = random.Next(original.Length + 1);
                return new(original[..length].ToArray(), $"truncate to {length}");
            case 4 when original.Length >= sizeof(ushort):
                var width = sizeof(ushort) << (index / (MutationKinds * 3) % 3);
                while (width > original.Length) width /= 2;
                // Alternate header fields and arbitrary payload positions; no format knowledge is assumed.
                var window = index / MutationKinds % 2 == 0 ? Math.Min(HeaderWindow, original.Length) : original.Length;
                offset = random.Next(Math.Max(1, window - width + 1));
                var overwritten = original.ToArray();
                Span<byte> value = stackalloc byte[sizeof(ulong)];
                var number = (index / MutationKinds % 3) switch
                {
                    0 => 0UL,
                    1 => ulong.MaxValue,
                    _ => 1UL << (width * 8 - 2),
                };
                if (index / (MutationKinds * 9) % 2 == 0)
                {
                    BinaryPrimitives.WriteUInt64LittleEndian(value, number);
                    value[..width].CopyTo(overwritten.AsSpan(offset));
                }
                else
                {
                    BinaryPrimitives.WriteUInt64BigEndian(value, number);
                    value[(sizeof(ulong) - width)..].CopyTo(overwritten.AsSpan(offset));
                }
                return new(overwritten, $"overwrite {width} bytes at {offset} with 0x{number:X16}");
            case 5 when !original.IsEmpty:
                var count = Math.Min(original.Length - offset, 1 + random.Next(MaxDuplicatedBytes));
                var target = random.Next(original.Length + 1);
                return new([.. original[..target], .. original.Slice(offset, count), .. original[target..]],
                    $"duplicate {count} bytes from {offset} at {target}");
            default:
                offset = random.Next(original.Length + 1);
                var inserted = new byte[1 + random.Next(MaxInsertedBytes)];
                for (var i = 0; i < inserted.Length; i++) inserted[i] = (byte)random.Next(256);
                return new([.. original[..offset], .. inserted, .. original[offset..]], $"insert {inserted.Length} at {offset}");
        }
    }

    // Specified xorshift32 instead of System.Random, whose sequence is not a cross-runtime contract.
    private sealed class MutationRandom(uint seed)
    {
        private uint _state = seed == 0 ? DefaultSeed : seed;

        public int Next(int exclusiveMax)
        {
            _state ^= _state << 13;
            _state ^= _state >> 17;
            _state ^= _state << 5;
            return exclusiveMax <= 1 ? 0 : (int)(_state % (uint)exclusiveMax);
        }
    }
}
