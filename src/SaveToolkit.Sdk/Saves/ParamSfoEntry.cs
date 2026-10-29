namespace SaveToolkit.Sdk.Saves;

/// <summary>Lossless structural metadata for one SFO index entry.</summary>
public sealed class ParamSfoEntry
{
    private readonly byte[] _keyBytes;
    private readonly byte[] _valueBytes;
    private readonly byte[] _reservedBytes;
    private readonly byte[] _rawIndexBytes;

    internal ParamSfoEntry(
        int index,
        string key,
        ushort keyOffset,
        ushort format,
        int length,
        int maximumLength,
        int dataOffset,
        byte[] keyBytes,
        byte[] valueBytes,
        byte[] reservedBytes,
        byte[] rawIndexBytes,
        string entrySha256)
    {
        Index = index;
        Key = key;
        KeyOffset = keyOffset;
        Format = format;
        Length = length;
        MaximumLength = maximumLength;
        DataOffset = dataOffset;
        _keyBytes = keyBytes;
        _valueBytes = valueBytes;
        _reservedBytes = reservedBytes;
        _rawIndexBytes = rawIndexBytes;
        EntrySha256 = entrySha256;
    }

    /// <summary>Zero-based index-table position.</summary>
    public int Index { get; }

    /// <summary>Decoded key without its terminator.</summary>
    public string Key { get; }

    /// <summary>Offset into the key table.</summary>
    public ushort KeyOffset { get; }

    /// <summary>Original SFO format code.</summary>
    public ushort Format { get; }

    /// <summary>Logical value length from the index entry.</summary>
    public int Length { get; }

    /// <summary>Reserved value-slot length from the index entry.</summary>
    public int MaximumLength { get; }

    /// <summary>Offset into the data table.</summary>
    public int DataOffset { get; }

    /// <summary>Raw key bytes, excluding the NUL terminator.</summary>
    public ReadOnlyMemory<byte> KeyBytes => _keyBytes.ToArray();

    /// <summary>Raw logical value bytes, including any stored terminator.</summary>
    public ReadOnlyMemory<byte> ValueBytes => _valueBytes.ToArray();

    /// <summary>All bytes in the reserved value slot.</summary>
    public ReadOnlyMemory<byte> ReservedBytes => _reservedBytes.ToArray();

    /// <summary>Raw 16-byte index entry.</summary>
    public ReadOnlyMemory<byte> RawIndexBytes => _rawIndexBytes.ToArray();

    /// <summary>SHA-256 fingerprint of the entry metadata and reserved bytes.</summary>
    public string EntrySha256 { get; }
}
