namespace SaveToolkit.Plugins.UnrealGvas.Tests;

/// <summary>Independent research implementations; no candidate is used to authorize runtime writes.</summary>
internal sealed class IntegrityCrc(uint polynomial, bool reflected)
{
    private readonly uint[] _table = Table(polynomial, reflected);

    public uint Compute(ReadOnlySpan<byte> bytes, uint initial = uint.MaxValue)
    {
        var state = initial;
        foreach (var value in bytes)
            state = reflected ? (state >> 8) ^ _table[(byte)(state ^ value)]
                : (state << 8) ^ _table[(byte)((state >> 24) ^ value)];
        return state;
    }

    // Invert each byte step. For these CRC polynomials the table high/low bytes are permutations.
    // A required seed is derived from one untouched capture, then tested on the other capture.
    public uint RequiredInitial(ReadOnlySpan<byte> bytes, uint finalState)
    {
        Span<byte> inverse = stackalloc byte[256];
        for (var i = 0; i < _table.Length; i++) inverse[(int)(reflected ? _table[i] >> 24 : _table[i] & 255)] = (byte)i;
        var state = finalState;
        for (var i = bytes.Length - 1; i >= 0; i--)
        {
            var index = inverse[(int)(reflected ? state >> 24 : state & 255)];
            state = reflected ? ((state ^ _table[index]) << 8) | (byte)(index ^ bytes[i])
                : ((state ^ _table[index]) >> 8) | ((uint)(index ^ bytes[i]) << 24);
        }
        return state;
    }

    private static uint[] Table(uint polynomial, bool reflected)
    {
        var table = new uint[256];
        for (uint i = 0; i < table.Length; i++)
        {
            var state = reflected ? i : i << 24;
            for (var bit = 0; bit < 8; bit++)
                state = reflected ? (state >> 1) ^ ((state & 1) != 0 ? polynomial : 0)
                    : (state << 1) ^ ((state & 0x80000000) != 0 ? polynomial : 0);
            table[i] = state;
        }
        return table;
    }
}

internal static class IntegrityAlternatives
{
    public static uint Fnv1A(ReadOnlySpan<byte> bytes)
    {
        const uint offset = 2166136261, prime = 16777619;
        var hash = offset;
        foreach (var value in bytes) hash = unchecked((hash ^ value) * prime);
        return hash;
    }

    public static uint Adler32(ReadOnlySpan<byte> bytes)
    {
        const uint modulus = 65521;
        uint low = 1, high = 0;
        foreach (var value in bytes) { low = (low + value) % modulus; high = (high + low) % modulus; }
        return (high << 16) | low;
    }
}
