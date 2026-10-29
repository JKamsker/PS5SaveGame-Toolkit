namespace SaveToolkit.Sdk.Values;

/// <summary>A JSON number lexeme, preserved byte-for-byte rather than converted to a machine number.</summary>
public sealed record ExactNumberSpec : FieldSpec
{
    /// <summary>Maximum lexeme length, bounded by the exact-number validator's hard limit.</summary>
    public int MaxLength { get; init; } = ExactJsonNumber.DefaultMaxLength;
    /// <summary>Maximum absolute exponent, without expanding exponent zeros.</summary>
    public int MaxAbsoluteExponent { get; init; } = ExactJsonNumber.DefaultMaxAbsoluteExponent;

    /// <summary>Inclusive bounds, themselves JSON number lexemes. Comparisons use exact decimal arithmetic.</summary>
    public string? Min { get; init; }
    /// <summary>Inclusive upper bound as an exact JSON number lexeme, or null for no bound.</summary>
    public string? Max { get; init; }
}
