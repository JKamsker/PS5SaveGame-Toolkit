namespace SaveToolkit.Plugins.GodOfWarRagnarok.Tests;

/// <summary>Original offline reflected-CRC probes. No candidate is used by the writer.</summary>
internal sealed class RagnarokCrcCandidate(uint polynomial)
{
    private readonly uint[] _table = Table(polynomial);
    private uint[][]? _forward;
    private uint[][]? _reverse;

    public uint Calculate(ReadOnlySpan<byte> bytes)
    {
        var crc = uint.MaxValue;
        foreach (var value in bytes) crc = Update(crc, value);
        return ~crc;
    }

    public IReadOnlyDictionary<int, uint> Prefixes(ReadOnlySpan<byte> bytes, IEnumerable<int> boundaries)
    {
        var result = new Dictionary<int, uint> { [0] = 0 };
        var crc = uint.MaxValue;
        var position = 0;
        foreach (var boundary in boundaries.Distinct().Order())
        {
            if (boundary < 0 || boundary > bytes.Length) throw new ArgumentOutOfRangeException(nameof(boundaries));
            while (position < boundary) crc = Update(crc, bytes[position++]);
            result[boundary] = ~crc;
        }
        return result;
    }

    public uint Range(IReadOnlyDictionary<int, uint> prefixes, int start, int end) =>
        prefixes[end] ^ Shift(prefixes[start], end - start);

    public uint Combine(uint first, uint second, int secondLength) => Shift(first, secondLength) ^ second;

    // A CRC is linear over GF(2). Exponentiation advances (or reverses) zero bytes in O(log length).
    // This lets the experiments omit/zero fields without copying or rehashing each multi-MiB file.
    public uint Shift(uint value, int bytes, bool reverse = false)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(bytes);
        var powers = reverse ? _reverse ??= Powers(reverse: true) : _forward ??= Powers(reverse: false);
        var power = 0;
        while (bytes > 0)
        {
            if ((bytes & 1) != 0) value = Multiply(powers[power], value);
            bytes >>= 1;
            power++;
        }
        return value;
    }

    public uint RecoverInitial(uint stored, uint standard, int length) =>
        Shift(stored ^ standard, length, reverse: true) ^ uint.MaxValue;

    private uint Update(uint crc, byte value) => _table[(crc ^ value) & byte.MaxValue] ^ (crc >> 8);

    private uint[][] Powers(bool reverse)
    {
        var powers = new uint[32][];
        powers[0] = Enumerable.Range(0, 32).Select(bit => reverse ? ReverseZero(1u << bit) : Update(1u << bit, 0)).ToArray();
        for (var i = 1; i < powers.Length; i++)
            powers[i] = powers[i - 1].Select(column => Multiply(powers[i - 1], column)).ToArray();
        return powers;
    }

    private uint ReverseZero(uint crc)
    {
        // The high table byte uniquely identifies the low previous CRC byte for these polynomials.
        for (uint index = 0; index < _table.Length; index++)
            if ((_table[index] >> 24) == (crc >> 24)) return ((crc ^ _table[index]) << 8) | index;
        throw new InvalidOperationException("CRC polynomial is not invertible.");
    }

    private static uint Multiply(uint[] matrix, uint value)
    {
        uint result = 0;
        for (var bit = 0; value != 0; bit++, value >>= 1)
            if ((value & 1) != 0) result ^= matrix[bit];
        return result;
    }

    private static uint[] Table(uint polynomial) => Enumerable.Range(0, 256).Select(value =>
    {
        var crc = (uint)value;
        for (var bit = 0; bit < 8; bit++) crc = (crc >> 1) ^ ((crc & 1) != 0 ? polynomial : 0);
        return crc;
    }).ToArray();
}
