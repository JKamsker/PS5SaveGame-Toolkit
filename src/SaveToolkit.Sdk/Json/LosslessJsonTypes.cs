namespace SaveToolkit.Sdk.Json;

/// <summary>Resource limits for <see cref="LosslessJsonDocument.Parse(System.ReadOnlySpan{System.Byte}, LosslessJsonLimits?)"/>.</summary>
public sealed record LosslessJsonLimits
{
    /// <summary>Default maximum input size, including BOM and trivia.</summary>
    public const int DefaultMaxBytes = 16 * 1024 * 1024;
    /// <summary>Maximum input size accepted by this API.</summary>
    public const int HardMaxBytes = 64 * 1024 * 1024;
    /// <summary>Default maximum nesting depth.</summary>
    public const int DefaultMaxDepth = 64;
    /// <summary>Maximum nesting depth accepted by this API.</summary>
    public const int HardMaxDepth = 256;
    /// <summary>Default maximum JSON value-node count (containers and scalars).</summary>
    public const int DefaultMaxTokens = 250_000;
    /// <summary>Maximum JSON value-node count accepted by this API.</summary>
    public const int HardMaxTokens = 1_000_000;

    /// <summary>Maximum number of input bytes.</summary>
    public int MaxBytes { get; init; } = DefaultMaxBytes;
    /// <summary>Maximum object/array nesting depth.</summary>
    public int MaxDepth { get; init; } = DefaultMaxDepth;
    /// <summary>Maximum number of JSON value nodes, including containers and scalars.</summary>
    public int MaxTokens { get; init; } = DefaultMaxTokens;
    /// <summary>Maximum number lexeme length. This cannot exceed <see cref="Values.ExactJsonNumber.HardMaxLength"/>.</summary>
    public int MaxNumberLength { get; init; } = Values.ExactJsonNumber.DefaultMaxLength;
    /// <summary>Maximum absolute exponent in a replacement number.</summary>
    public int MaxAbsoluteExponent { get; init; } = Values.ExactJsonNumber.DefaultMaxAbsoluteExponent;

    /// <summary>Default limits matching the existing JSON plugin safety envelope.</summary>
    public static LosslessJsonLimits Default { get; } = new();

    internal void Validate()
    {
        if (MaxBytes is < 1 or > HardMaxBytes || MaxDepth is < 1 or > HardMaxDepth ||
            MaxTokens is < 1 or > HardMaxTokens || MaxNumberLength is < 1 or > Values.ExactJsonNumber.HardMaxLength ||
            MaxAbsoluteExponent is < 0 or > Values.ExactJsonNumber.HardMaxAbsoluteExponent)
            throw new ArgumentOutOfRangeException(nameof(LosslessJsonLimits), "JSON limits are outside the supported safety envelope.");
    }
}

/// <summary>A byte span in the original JSON input.</summary>
public readonly record struct JsonByteSpan(int Offset, int Length)
{
    /// <summary>Exclusive end offset.</summary>
    public int End => checked(Offset + Length);
}

/// <summary>JSON value kinds exposed by the lossless lexical tree.</summary>
public enum LosslessJsonNodeKind
{
    /// <summary>Object value.</summary>
    Object,
    /// <summary>Array value.</summary>
    Array,
    /// <summary>String scalar.</summary>
    String,
    /// <summary>Number scalar.</summary>
    Number,
    /// <summary>Boolean scalar.</summary>
    Boolean,
    /// <summary>Null scalar.</summary>
    Null,
}

/// <summary>Malformed input or a bounded-resource failure while parsing lossless JSON.</summary>
public sealed class LosslessJsonException : FormatException
{
    /// <summary>Creates a parse exception with an optional byte offset.</summary>
    public LosslessJsonException(string message, int? byteOffset = null, Exception? innerException = null)
        : base(message, innerException) => ByteOffset = byteOffset;

    /// <summary>Input byte offset associated with the failure, when known.</summary>
    public int? ByteOffset { get; }
}
