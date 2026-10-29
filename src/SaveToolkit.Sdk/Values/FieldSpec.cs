using System.Text.Json.Serialization;

namespace SaveToolkit.Sdk.Values;

/// <summary>
/// Describes the data type and constraints of an editable value. The browser picks a control from the
/// spec; plugins never choose widgets or styling. The framework validates every incoming edit against
/// the spec before the plugin sees it.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(BoolSpec), "bool")]
[JsonDerivedType(typeof(IntegerSpec), "integer")]
[JsonDerivedType(typeof(FloatSpec), "float")]
[JsonDerivedType(typeof(TextSpec), "text")]
[JsonDerivedType(typeof(EnumSpec), "enum")]
[JsonDerivedType(typeof(FlagsSpec), "flags")]
[JsonDerivedType(typeof(DateTimeSpec), "datetime")]
[JsonDerivedType(typeof(ExactNumberSpec), "exactNumber")]
[JsonDerivedType(typeof(NullableSpec), "nullable")]
[JsonDerivedType(typeof(UnionSpec), "union")]
[JsonDerivedType(typeof(ReferenceSpec), "reference")]
public abstract record FieldSpec
{
    /// <summary>Prevents edits through the host while preserving the field's value and display metadata.</summary>
    public bool ReadOnly { get; init; }

    /// <summary>Optional unit label, e.g. "HP" or "µs".</summary>
    public string? Unit { get; init; }
}

/// <summary>A Boolean field accepting only true or false.</summary>
public sealed record BoolSpec : FieldSpec;

/// <summary>Display format for an integer; the underlying value remains an exact integer.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<IntegerFormat>))]
public enum IntegerFormat
{
    /// <summary>Ordinary decimal integer presentation.</summary>
    Decimal,
    /// <summary>Hexadecimal integer presentation.</summary>
    Hex,

    /// <summary>Elapsed time; see <see cref="IntegerSpec.UnitsPerSecond"/>.</summary>
    Duration,
}

/// <summary>An exact integer constrained by inclusive minimum and maximum bounds.</summary>
public sealed record IntegerSpec(
    [property: JsonConverter(typeof(Int128StringConverter))] Int128 Min,
    [property: JsonConverter(typeof(Int128StringConverter))] Int128 Max) : FieldSpec
{
    /// <summary>Presentation hint without changing integer storage or bounds.</summary>
    public IntegerFormat Format { get; init; } = IntegerFormat.Decimal;

    /// <summary>For <see cref="IntegerFormat.Duration"/>: raw units per second (e.g. 1_000_000 for µs).</summary>
    public long? UnitsPerSecond { get; init; }

    /// <summary>Inclusive signed 8-bit integer bounds.</summary>
    public static IntegerSpec Int8 => new(sbyte.MinValue, sbyte.MaxValue);
    /// <summary>Inclusive unsigned 8-bit integer bounds.</summary>
    public static IntegerSpec UInt8 => new(byte.MinValue, byte.MaxValue);
    /// <summary>Inclusive signed 16-bit integer bounds.</summary>
    public static IntegerSpec Int16 => new(short.MinValue, short.MaxValue);
    /// <summary>Inclusive unsigned 16-bit integer bounds.</summary>
    public static IntegerSpec UInt16 => new(ushort.MinValue, ushort.MaxValue);
    /// <summary>Inclusive signed 32-bit integer bounds.</summary>
    public static IntegerSpec Int32 => new(int.MinValue, int.MaxValue);
    /// <summary>Inclusive unsigned 32-bit integer bounds.</summary>
    public static IntegerSpec UInt32 => new(uint.MinValue, uint.MaxValue);
    /// <summary>Inclusive signed 64-bit integer bounds.</summary>
    public static IntegerSpec Int64 => new(long.MinValue, long.MaxValue);
    /// <summary>Inclusive unsigned 64-bit integer bounds.</summary>
    public static IntegerSpec UInt64 => new(ulong.MinValue, ulong.MaxValue);
}

/// <summary>Display format for a floating-point value, with optional elapsed-time presentation.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<FloatFormat>))]
public enum FloatFormat
{
    /// <summary>Ordinary floating-point presentation.</summary>
    Decimal,
    /// <summary>Elapsed-time presentation using the spec's units per second.</summary>
    Duration,
}

/// <summary>A finite floating-point field with optional inclusive bounds and float32 rounding.</summary>
public sealed record FloatSpec : FieldSpec
{
    /// <summary>Presentation hint without changing numeric storage.</summary>
    public FloatFormat Format { get; init; } = FloatFormat.Decimal;

    /// <summary>Raw units per second for Duration; defaults to seconds. Must be finite and positive.</summary>
    public double UnitsPerSecond { get; init; } = 1;

    /// <summary>Inclusive lower bound, or null for no additional bound.</summary>
    public double? Min { get; init; }
    /// <summary>Inclusive upper bound, or null for no additional bound.</summary>
    public double? Max { get; init; }

    /// <summary>True when the stored value is float32, so the browser can round accordingly.</summary>
    public bool SinglePrecision { get; init; }

    /// <summary>A float32 field with no additional numeric bounds.</summary>
    public static FloatSpec Single => new() { SinglePrecision = true };
    /// <summary>A float64 field with no additional numeric bounds.</summary>
    public static FloatSpec Double => new();
}

/// <summary>A text field with independent optional UTF-16 and UTF-8 bounds and an explicit line-break policy.</summary>
public sealed record TextSpec : FieldSpec
{
    /// <summary>Smallest UTF-8 capacity: only the empty logical value fits.</summary>
    public const int MinimumUtf8ByteCapacity = 0;

    /// <summary>Largest representable UTF-8 capacity, in bytes.</summary>
    public const int MaximumUtf8ByteCapacity = int.MaxValue;

    /// <summary>Maximum length in UTF-16 code units, or null for unbounded.</summary>
    public int? MaxLength { get; init; }

    /// <summary>
    /// Optional inclusive UTF-8 byte limit for the logical value, excluding storage terminators and any
    /// encoding preamble. A literal U+FEFF in the value counts as three bytes. Must be nonnegative;
    /// zero accepts only empty text. When present, edits must contain valid Unicode scalar sequences.
    /// No normalization, replacement, or truncation is performed. Independent of <see cref="MaxLength"/>.
    /// </summary>
    public int? MaxUtf8Bytes { get; init; }

    /// <summary>Allows carriage returns and line feeds; false requires a single line.</summary>
    public bool Multiline { get; init; }
}

/// <summary>A stable raw option value and its display label; labels are never written to the save.</summary>
public sealed record EnumOption(string Value, string Label)
{
    /// <summary>Optional explanation or attribution for this option.</summary>
    public string? Description { get; init; }
}

/// <summary>
/// One-of choice. The value is the option's <see cref="EnumOption.Value"/> as text.
/// With <see cref="AllowCustom"/>, values outside the list are accepted (useful for partially
/// mapped hash IDs); the plugin must then validate them itself.
/// </summary>
public sealed record EnumSpec(IReadOnlyList<EnumOption> Options) : FieldSpec
{
    /// <summary>Accepts raw values outside the published list; native-format validation remains the plugin's responsibility.</summary>
    public bool AllowCustom { get; init; }
}

/// <summary>A named zero-based bit position within a <see cref="FlagsSpec"/> integer.</summary>
public sealed record FlagBit(int Bit, string Label)
{
    /// <summary>Optional explanation of the named bit's meaning.</summary>
    public string? Description { get; init; }
}

/// <summary>Bit set stored as an integer value; unnamed bits are preserved by the browser.</summary>
public sealed record FlagsSpec(IReadOnlyList<FlagBit> Bits, int BitWidth) : FieldSpec;

/// <summary>ISO-8601 timestamp text. Set <see cref="HasTimeZone"/> false for local/unknown-zone clocks.</summary>
public sealed record DateTimeSpec : FieldSpec
{
    /// <summary>True validates an offset-aware timestamp; false retains an unspecified local clock.</summary>
    public bool HasTimeZone { get; init; }
}
