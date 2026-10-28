using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SaveToolkit.Sdk.Values;

public enum FieldValueKind
{
    Null,
    Bool,
    Integer,
    Float,
    Text,
    ExactNumber,
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

    public FieldValueKind Kind { get; }

    public static FieldValue Null => default;

    public bool IsNull => Kind == FieldValueKind.Null;

    public static FieldValue Of(bool value) => new(FieldValueKind.Bool, integer: value ? 1 : 0);
    public static FieldValue Of(long value) => new(FieldValueKind.Integer, integer: value);
    public static FieldValue Of(ulong value) => new(FieldValueKind.Integer, integer: value);
    public static FieldValue Of(Int128 value) => new(FieldValueKind.Integer, integer: value);
    public static FieldValue Of(double value) => new(FieldValueKind.Float, @float: value);
    public static FieldValue Of(string value) => new(FieldValueKind.Text, text: value ?? throw new ArgumentNullException(nameof(value)));

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

    public static FieldValue Union(string type, FieldValue value)
    {
        ArgumentException.ThrowIfNullOrEmpty(type);
        return new(FieldValueKind.Union, union: new(type, value));
    }

    public string AsExactNumber() => Kind == FieldValueKind.ExactNumber ? _text! : throw Mismatch(FieldValueKind.ExactNumber);
    public UnionFieldValue AsUnion() => Kind == FieldValueKind.Union ? _union! : throw Mismatch(FieldValueKind.Union);

    public bool AsBool() => Kind == FieldValueKind.Bool ? _integer != 0 : throw Mismatch(FieldValueKind.Bool);

    public Int128 AsInteger() => Kind == FieldValueKind.Integer ? _integer : throw Mismatch(FieldValueKind.Integer);

    public long AsInt64() => checked((long)AsInteger());
    public ulong AsUInt64() => checked((ulong)AsInteger());
    public int AsInt32() => checked((int)AsInteger());
    public uint AsUInt32() => checked((uint)AsInteger());

    public double AsDouble() => Kind switch
    {
        FieldValueKind.Float => _float,
        FieldValueKind.Integer => (double)_integer,
        _ => throw Mismatch(FieldValueKind.Float),
    };

    public string AsString() => Kind == FieldValueKind.Text ? _text! : throw Mismatch(FieldValueKind.Text);

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
    public override Int128 Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.TokenType == JsonTokenType.String
            ? Int128.Parse(reader.GetString()!, CultureInfo.InvariantCulture)
            : reader.GetInt64();

    public override void Write(Utf8JsonWriter writer, Int128 value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.ToString(CultureInfo.InvariantCulture));
}
