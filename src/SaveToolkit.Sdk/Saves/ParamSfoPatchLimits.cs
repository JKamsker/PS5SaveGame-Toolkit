namespace SaveToolkit.Sdk.Saves;

/// <summary>Finite limits used while parsing and patching a lossless SFO document.</summary>
public sealed record ParamSfoPatchLimits
{
    /// <summary>Default limits for ordinary save metadata.</summary>
    public static ParamSfoPatchLimits Default { get; } = new(1 * 1024 * 1024, 64 * 1024, 1024, 4 * 1024);

    /// <summary>Maximum complete SFO size accepted by the parser.</summary>
    public int MaxFileBytes { get; }

    /// <summary>Maximum reserved slot size for one entry.</summary>
    public int MaxEntryBytes { get; }

    /// <summary>Maximum key bytes before the NUL terminator.</summary>
    public int MaxKeyBytes { get; }

    /// <summary>Maximum encoded replacement bytes including the required NUL terminator.</summary>
    public int MaxEncodedValueBytes { get; }

    /// <summary>Creates finite parser and writer limits.</summary>
    public ParamSfoPatchLimits(
        int maxFileBytes,
        int maxEntryBytes,
        int maxKeyBytes,
        int maxEncodedValueBytes = 4 * 1024)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxFileBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxEntryBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxKeyBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxEncodedValueBytes);

        MaxFileBytes = maxFileBytes;
        MaxEntryBytes = maxEntryBytes;
        MaxKeyBytes = maxKeyBytes;
        MaxEncodedValueBytes = maxEncodedValueBytes;
    }
}
