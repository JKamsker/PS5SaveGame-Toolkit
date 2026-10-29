namespace SaveToolkit.Sdk.Binary;

/// <summary>CRC-32/ISO-HDLC (IEEE), reflected 0xEDB88320, all-one initialization and final complement.</summary>
public sealed class Crc32Ieee
{
    private const uint ReflectedPolynomial = 0xEDB88320;
    private const int BitsPerByte = 8;
    private static readonly uint[] Table = BuildTable();
    private uint _state = uint.MaxValue;

    public static uint Compute(ReadOnlySpan<byte> bytes) => ~Update(uint.MaxValue, bytes);
    public void Append(ReadOnlySpan<byte> bytes) => _state = Update(_state, bytes);

    /// <summary>Finalized value without consuming/resetting state. Coverage, folding and byte order are caller-owned.</summary>
    public uint GetCurrentHash() => ~_state;
    public void Reset() => _state = uint.MaxValue;

    private static uint Update(uint state, ReadOnlySpan<byte> bytes)
    {
        foreach (var value in bytes) state = (state >> BitsPerByte) ^ Table[(state ^ value) & byte.MaxValue];
        return state;
    }

    private static uint[] BuildTable()
    {
        var table = new uint[byte.MaxValue + 1];
        for (var index = 0; index < table.Length; index++)
        {
            var value = (uint)index;
            for (var bit = 0; bit < BitsPerByte; bit++)
                value = (value >> 1) ^ ((value & 1) != 0 ? ReflectedPolynomial : 0);
            table[index] = value;
        }
        return table;
    }
}
