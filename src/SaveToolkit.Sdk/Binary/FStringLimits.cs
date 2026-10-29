namespace SaveToolkit.Sdk.Binary;

/// <summary>
/// Limits include the terminator, exclude the four-byte prefix. MaxBytes caps either encoding's body;
/// MaxUtf16CodeUnits additionally caps wide strings (surrogate pairs count as two units).
/// For a format capped at N serialized units use (2*N, N), then additionally cap narrow bytes at N.
/// </summary>
public sealed record FStringLimits(int MaxBytes, int MaxUtf16CodeUnits)
{
    /// <summary>Optional narrower byte cap for positive-length UTF-8 strings; defaults to MaxBytes.</summary>
    public int? MaxUtf8Bytes { get; init; }

    internal void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfNegative(MaxBytes);
        ArgumentOutOfRangeException.ThrowIfNegative(MaxUtf16CodeUnits);
        if (MaxUtf8Bytes is < 0) throw new ArgumentOutOfRangeException(nameof(MaxUtf8Bytes));
    }
}
