namespace SaveToolkit.Sdk.Binary;

/// <summary>Overflow-safe bounds checks for slices of a caller-owned binary span.</summary>
public static class BinaryRange
{
    /// <summary>Checks bounds without adding untrusted lengths, including an empty range at the end.</summary>
    public static ReadOnlySpan<byte> Slice(ReadOnlySpan<byte> data, int offset, int length, string context)
    {
        if (offset < 0 || length < 0 || offset > data.Length || length > data.Length - offset)
            throw new SaveFormatException($"{context}: invalid or truncated range at 0x{offset:X} (need {length} bytes).");
        return data.Slice(offset, length);
    }
}
