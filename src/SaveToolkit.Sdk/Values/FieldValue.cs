using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SaveToolkit.Sdk.Values;

/// <summary>The stored representation of a <see cref="FieldValue"/>, independent of its display format.</summary>
public enum FieldValueKind
{
    /// <summary>Explicit null, distinct from an absent field.</summary>
    Null,
    /// <summary>Boolean true or false.</summary>
    Bool,
    /// <summary>Exact signed 128-bit integer.</summary>
    Integer,
    /// <summary>Double-precision floating-point value.</summary>
    Float,
    /// <summary>Uninterpreted text, including enum choices and timestamps.</summary>
    Text,
    /// <summary>Validated JSON numeric lexeme preserved as text.</summary>
    ExactNumber,
    /// <summary>An explicit option ID and its typed inner value.</summary>
    Union,
}

/// <summary>
/// A typed value travelling between plugin, framework and browser.
/// Integers are 128-bit so every int8..uint64 fits; on the wire they are decimal strings
/// because JavaScript numbers lose precision above 2^53.
/// Enum values are <see cref="FieldValueKind.Text"/> (the option value); timestamps are ISO-8601 text;
/// flag sets are integers. Exact numbers and explicit union choices use tagged wire objects.
/// </summary>
[JsonConverter(typeof(FieldValueJsonConverter))]
public readonly record struct FieldValue
{
    private readonly Int128 _integer;
    private readonly double _float;
    private readonly string? _text;
    private readonly UnionFieldValue? _union;

    private FieldValue(FieldValueKind kind, Int128 integer = default, double @float = 0, string? text = null, UnionFieldValue? union = null)
    {
        Kind = kind;
        _integer = integer;
        _float = @float;
        _text = text;
        _union = union;
    }

    /// <summary>Active representation; accessors reject incompatible kinds.</summary>
    public FieldValueKind Kind { get; }

    /// <summary>An explicit null value; also the default value of this struct.</summary>
    public static FieldValue Null => default;

    /// <summary>Whether this value represents explicit null.</summary>
    public bool IsNull => Kind == FieldValueKind.Null;

    /// <summary>Creates a Boolean value.</summary>
    public static FieldValue Of(bool value) => new(FieldValueKind.Bool, integer: value ? 1 : 0);
    /// <summary>Creates an exact signed integer value.</summary>
    public static FieldValue Of(long value) => new(FieldValueKind.Integer, integer: value);
    /// <summary>Creates an exact unsigned integer value.</summary>
    public static FieldValue Of(ulong value) => new(FieldValueKind.Integer, integer: value);
    /// <summary>Creates an exact 128-bit integer value.</summary>
    public static FieldValue Of(Int128 value) => new(FieldValueKind.Integer, integer: value);
    /// <summary>Creates a floating-point value; field validation enforces finite values and bounds.</summary>
    public static FieldValue Of(double value) => new(FieldValueKind.Float, @float: value);
    /// <summary>Creates non-null text without interpreting numeric or timestamp content.</summary>
    public static FieldValue Of(string value) => new(FieldValueKind.Text, text: value ?? throw new ArgumentNullException(nameof(value)));

    /// <summary>Creates a validated JSON numeric lexeme, preserving its exact spelling without floating-point conversion.</summary>
    public static FieldValue ExactNumber(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var error = ExactJsonNumber.Validate(text, new ExactNumberSpec
        {
            MaxLength = ExactJsonNumber.HardMaxLength,
            MaxAbsoluteExponent = ExactJsonNumber.HardMaxAbsoluteExponent,
        });
        if (error is not null) throw new ArgumentException(error, nameof(text));
        return new(FieldValueKind.ExactNumber, text: text);
    }

    /// <summary>Wraps a value in an explicit union option ID; the host validates it against the selected inner spec.</summary>
    public static FieldValue Union(string type, FieldValue value)
    {
        ArgumentException.ThrowIfNullOrEmpty(type);
        return new(FieldValueKind.Union, union: new(type, value));
    }

    /// <summary>Returns the original exact-number lexeme; throws for another kind.</summary>
    public string AsExactNumber() => Kind == FieldValueKind.ExactNumber ? _text! : throw Mismatch(FieldValueKind.ExactNumber);
    /// <summary>Returns the selected union option and its inner value; throws for another kind.</summary>
    public UnionFieldValue AsUnion() => Kind == FieldValueKind.Union ? _union! : throw Mismatch(FieldValueKind.Union);

    /// <summary>Returns the Boolean value; throws for another kind.</summary>
    public bool AsBool() => Kind == FieldValueKind.Bool ? _integer != 0 : throw Mismatch(FieldValueKind.Bool);

    /// <summary>Returns the exact integer; throws for another kind.</summary>
    public Int128 AsInteger() => Kind == FieldValueKind.Integer ? _integer : throw Mismatch(FieldValueKind.Integer);

    /// <summary>Returns a signed 64-bit integer, throwing for a different kind or an out-of-range value.</summary>
    public long AsInt64() => checked((long)AsInteger());
    /// <summary>Returns an unsigned 64-bit integer, throwing for a different kind or an out-of-range value.</summary>
    public ulong AsUInt64() => checked((ulong)AsInteger());
    /// <summary>Returns a signed 32-bit integer, throwing for a different kind or an out-of-range value.</summary>
    public int AsInt32() => checked((int)AsInteger());
    /// <summary>Returns an unsigned 32-bit integer, throwing for a different kind or an out-of-range value.</summary>
    public uint AsUInt32() => checked((uint)AsInteger());

    /// <summary>Returns a float or converts an integer to double, potentially losing integer precision; throws for other kinds.</summary>
    public double AsDouble() => Kind switch
    {
        FieldValueKind.Float => _float,
        FieldValueKind.Integer => (double)_integer,
        _ => throw Mismatch(FieldValueKind.Float),
    };

    /// <summary>Returns text without conversion; exact numbers and other kinds require their own accessors.</summary>
    public string AsString() => Kind == FieldValueKind.Text ? _text! : throw Mismatch(FieldValueKind.Text);

    /// <summary>Invariant display text; unions display their inner value. Use JSON serialization for tagged wire values.</summary>
    public override string ToString() => Kind switch
    {
        FieldValueKind.Null => "null",
        FieldValueKind.Bool => AsBool() ? "true" : "false",
        FieldValueKind.Integer => _integer.ToString(CultureInfo.InvariantCulture),
        FieldValueKind.Float => _float.ToString("R", CultureInfo.InvariantCulture),
        FieldValueKind.Union => _union!.Value.ToString(),
        _ => _text!,
    };

    private InvalidOperationException Mismatch(FieldValueKind expected) =>
        new($"Field value is {Kind}, expected {expected}.");
}

/// <summary>
/// Writes values in their wire form. Reading is lenient (integer strings become Text); the framework
/// re-interprets incoming JSON against the field's <see cref="FieldSpec"/>, so specs decide types.
/// </summary>
public sealed class FieldValueJsonConverter : JsonConverter<FieldValue>
{
    /// <inheritdoc />
    public override FieldValue Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.TokenType switch
        {
            JsonTokenType.Null => FieldValue.Null,
            JsonTokenType.True => FieldValue.Of(true),
            JsonTokenType.False => FieldValue.Of(false),
            JsonTokenType.String => FieldValue.Of(reader.GetString()!),
            JsonTokenType.Number when reader.TryGetInt64(out var l) => FieldValue.Of(l),
            JsonTokenType.Number => FieldValue.Of(reader.GetDouble()),
            JsonTokenType.StartObject => TaggedFieldValueJson.Read(ref reader, options),
            _ => throw new JsonException($"Unsupported token {reader.TokenType} for a field value."),
        };

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, FieldValue value, JsonSerializerOptions options)
    {
        switch (value.Kind)
        {
            case FieldValueKind.Null:
                writer.WriteNullValue();
                break;
            case FieldValueKind.Bool:
                writer.WriteBooleanValue(value.AsBool());
                break;
            case FieldValueKind.Integer:
                writer.WriteStringValue(value.ToString());
                break;
            case FieldValueKind.Float:
                var d = value.AsDouble();
                if (double.IsFinite(d))
                {
                    writer.WriteNumberValue(d);
                }
                else
                {
                    writer.WriteStringValue(value.ToString());
                }

                break;
            case FieldValueKind.ExactNumber:
            case FieldValueKind.Union:
                TaggedFieldValueJson.Write(writer, value, options);
                break;
            default:
                writer.WriteStringValue(value.AsString());
                break;
        }
    }
}

/// <summary>Serialises <see cref="Int128"/> as a decimal string (JavaScript-safe).</summary>
public sealed class Int128StringConverter : JsonConverter<Int128>
{
    /// <inheritdoc />
    public override Int128 Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.TokenType == JsonTokenType.String
            ? Int128.Parse(reader.GetString()!, CultureInfo.InvariantCulture)
            : reader.GetInt64();

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, Int128 value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.ToString(CultureInfo.InvariantCulture));
}
