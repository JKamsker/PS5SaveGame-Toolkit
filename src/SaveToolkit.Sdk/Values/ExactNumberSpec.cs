namespace SaveToolkit.Sdk.Values;

/// <summary>A JSON number lexeme, preserved byte-for-byte rather than converted to a machine number.</summary>
public sealed record ExactNumberSpec : FieldSpec
{
    public int MaxLength { get; init; } = ExactJsonNumber.DefaultMaxLength;
    public int MaxAbsoluteExponent { get; init; } = ExactJsonNumber.DefaultMaxAbsoluteExponent;

    /// <summary>Inclusive bounds, themselves JSON number lexemes. Comparisons use exact decimal arithmetic.</summary>
    public string? Min { get; init; }
    public string? Max { get; init; }
}
