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
public abstract record FieldSpec
{
    public bool ReadOnly { get; init; }

    /// <summary>Optional unit label, e.g. "HP" or "µs".</summary>
    public string? Unit { get; init; }
}

public sealed record BoolSpec : FieldSpec;

[JsonConverter(typeof(JsonStringEnumConverter<IntegerFormat>))]
public enum IntegerFormat
{
    Decimal,
    Hex,

    /// <summary>Elapsed time; see <see cref="IntegerSpec.UnitsPerSecond"/>.</summary>
    Duration,
}

public sealed record IntegerSpec(
    [property: JsonConverter(typeof(Int128StringConverter))] Int128 Min,
    [property: JsonConverter(typeof(Int128StringConverter))] Int128 Max) : FieldSpec
{
    public IntegerFormat Format { get; init; } = IntegerFormat.Decimal;

    /// <summary>For <see cref="IntegerFormat.Duration"/>: raw units per second (e.g. 1_000_000 for µs).</summary>
    public long? UnitsPerSecond { get; init; }

    public static IntegerSpec Int8 => new(sbyte.MinValue, sbyte.MaxValue);
    public static IntegerSpec UInt8 => new(byte.MinValue, byte.MaxValue);
    public static IntegerSpec Int16 => new(short.MinValue, short.MaxValue);
    public static IntegerSpec UInt16 => new(ushort.MinValue, ushort.MaxValue);
    public static IntegerSpec Int32 => new(int.MinValue, int.MaxValue);
    public static IntegerSpec UInt32 => new(uint.MinValue, uint.MaxValue);
    public static IntegerSpec Int64 => new(long.MinValue, long.MaxValue);
    public static IntegerSpec UInt64 => new(ulong.MinValue, ulong.MaxValue);
}

public sealed record FloatSpec : FieldSpec
{
    public double? Min { get; init; }
    public double? Max { get; init; }

    /// <summary>True when the stored value is float32, so the browser can round accordingly.</summary>
    public bool SinglePrecision { get; init; }

    public static FloatSpec Single => new() { SinglePrecision = true };
    public static FloatSpec Double => new();
}

public sealed record TextSpec : FieldSpec
{
    /// <summary>Maximum length in UTF-16 code units, or null for unbounded.</summary>
    public int? MaxLength { get; init; }

    public bool Multiline { get; init; }
}

public sealed record EnumOption(string Value, string Label)
{
    public string? Description { get; init; }
}

/// <summary>
/// One-of choice. The value is the option's <see cref="EnumOption.Value"/> as text.
/// With <see cref="AllowCustom"/>, values outside the list are accepted (useful for partially
/// mapped hash IDs); the plugin must then validate them itself.
/// </summary>
public sealed record EnumSpec(IReadOnlyList<EnumOption> Options) : FieldSpec
{
    public bool AllowCustom { get; init; }
}

public sealed record FlagBit(int Bit, string Label)
{
    public string? Description { get; init; }
}

/// <summary>Bit set stored as an integer value; unnamed bits are preserved by the browser.</summary>
public sealed record FlagsSpec(IReadOnlyList<FlagBit> Bits, int BitWidth) : FieldSpec;

/// <summary>ISO-8601 timestamp text. Set <see cref="HasTimeZone"/> false for local/unknown-zone clocks.</summary>
public sealed record DateTimeSpec : FieldSpec
{
    public bool HasTimeZone { get; init; }
}
