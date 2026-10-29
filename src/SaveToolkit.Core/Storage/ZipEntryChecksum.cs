namespace SaveToolkit.Core.Storage;

/// <summary>Incremental ZIP CRC-32 (IEEE), computed over the bytes actually imported.</summary>
internal sealed class ZipEntryChecksum
{
    private const uint Polynomial = 0xEDB88320;
    private const uint InitialValue = uint.MaxValue;
    private const int TableSize = 256;
    private const int BitsPerByte = 8;
    private static readonly uint[] Table = CreateTable();
    private uint _value = InitialValue;

    public uint Value => ~_value;

    public void Append(ReadOnlySpan<byte> bytes)
    {
        foreach (byte value in bytes)
            _value = Table[(byte)(_value ^ value)] ^ (_value >> BitsPerByte);
    }

    private static uint[] CreateTable()
    {
        var table = new uint[TableSize];
        for (int index = 0; index < table.Length; index++)
        {
            uint value = (uint)index;
            for (int bit = 0; bit < BitsPerByte; bit++)
                value = (value & 1) != 0 ? (value >> 1) ^ Polynomial : value >> 1;
            table[index] = value;
        }
        return table;
    }
}
