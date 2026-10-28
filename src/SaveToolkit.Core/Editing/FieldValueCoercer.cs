using System.Globalization;
using System.Text.Json;
using SaveToolkit.Sdk.Values;

namespace SaveToolkit.Core.Editing;

/// <summary>
/// Converts loosely-typed incoming values (browser JSON, action parameters) into the exact
/// <see cref="FieldValue"/> shape a spec demands, enforcing every constraint the spec declares.
/// This is the trust boundary: plugins only ever receive values that passed here.
/// </summary>
public static class FieldValueCoercer
{
    public readonly record struct Result(FieldValue Value, string? Error)
    {
        public bool Ok => Error is null;
    }

    public static Result FromJson(JsonElement json, FieldSpec spec)
    {
        FieldValue raw = json.ValueKind switch
        {
            JsonValueKind.True => FieldValue.Of(true),
            JsonValueKind.False => FieldValue.Of(false),
            JsonValueKind.String => FieldValue.Of(json.GetString()!),
            JsonValueKind.Number when json.TryGetInt64(out var l) => FieldValue.Of(l),
            JsonValueKind.Number => FieldValue.Of(json.GetDouble()),
            _ => FieldValue.Null,
        };
        return Normalize(raw, spec);
    }

    public static Result Normalize(FieldValue raw, FieldSpec spec, bool allowReadOnly = false)
    {
        if (spec.ReadOnly && !allowReadOnly)
        {
            return Fail("Field is read-only.");
        }

        return spec switch
        {
            BoolSpec => raw.Kind == FieldValueKind.Bool ? Ok(raw) : Fail("Expected true or false."),
            IntegerSpec s => Integer(raw, s.Min, s.Max),
            FloatSpec s => Float(raw, s),
            TextSpec s => Text(raw, s),
            EnumSpec s => Enum(raw, s),
            FlagsSpec s => Integer(raw, 0, s.BitWidth >= 127 ? Int128.MaxValue : (Int128.One << s.BitWidth) - 1),
            DateTimeSpec s => DateTimeValue(raw, s),
            _ => Fail($"Unsupported field kind {spec.GetType().Name}."),
        };
    }

    private static Result Integer(FieldValue raw, Int128 min, Int128 max)
    {
        Int128 value;
        switch (raw.Kind)
        {
            case FieldValueKind.Integer:
                value = raw.AsInteger();
                break;
            case FieldValueKind.Text when TryParseInteger(raw.AsString().Trim(), out var parsed):
                value = parsed;
                break;
            case FieldValueKind.Float when double.IsInteger(raw.AsDouble()) && Math.Abs(raw.AsDouble()) < 9.007199254740992E15:
                value = (Int128)raw.AsDouble();
                break;
            default:
                return Fail("Expected a whole number.");
        }

        return value < min || value > max
            ? Fail($"Must be between {min.ToString(CultureInfo.InvariantCulture)} and {max.ToString(CultureInfo.InvariantCulture)}.")
            : Ok(FieldValue.Of(value));
    }

    private static bool TryParseInteger(string text, out Int128 value)
    {
        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            var ok = UInt128.TryParse(text[2..], NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var u) && u <= (UInt128)Int128.MaxValue;
            value = ok ? (Int128)u : default;
            return ok;
        }

        return Int128.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out value);
    }

    private static Result Float(FieldValue raw, FloatSpec spec)
    {
        double value;
        switch (raw.Kind)
        {
            case FieldValueKind.Float or FieldValueKind.Integer:
                value = raw.AsDouble();
                break;
            case FieldValueKind.Text when double.TryParse(raw.AsString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed):
                value = parsed;
                break;
            default:
                return Fail("Expected a number.");
        }

        if (!double.IsFinite(value))
        {
            return Fail("Number must be finite.");
        }

        if (spec.SinglePrecision)
        {
            if (Math.Abs(value) > float.MaxValue)
            {
                return Fail("Number is out of float32 range.");
            }

            value = (float)value;
        }

        if ((spec.Min is { } min && value < min) || (spec.Max is { } max && value > max))
        {
            return Fail($"Must be between {spec.Min?.ToString(CultureInfo.InvariantCulture) ?? "-∞"} and {spec.Max?.ToString(CultureInfo.InvariantCulture) ?? "∞"}.");
        }

        return Ok(FieldValue.Of(value));
    }

    private static Result Text(FieldValue raw, TextSpec spec)
    {
        if (raw.Kind != FieldValueKind.Text)
        {
            return Fail("Expected text.");
        }

        var text = raw.AsString();
        if (spec.MaxLength is { } max && text.Length > max)
        {
            return Fail($"At most {max} characters.");
        }

        if (!spec.Multiline && text.AsSpan().IndexOfAny('\r', '\n') >= 0)
        {
            return Fail("Line breaks are not allowed.");
        }

        return Ok(raw);
    }

    private static Result Enum(FieldValue raw, EnumSpec spec)
    {
        var text = raw.Kind switch
        {
            FieldValueKind.Text => raw.AsString(),
            FieldValueKind.Integer => raw.ToString(),
            _ => null,
        };
        if (text is null)
        {
            return Fail("Expected an option.");
        }

        return spec.AllowCustom || spec.Options.Any(o => o.Value == text)
            ? Ok(FieldValue.Of(text))
            : Fail("Not one of the allowed options.");
    }

    private static Result DateTimeValue(FieldValue raw, DateTimeSpec spec)
    {
        if (raw.Kind != FieldValueKind.Text)
        {
            return Fail("Expected an ISO-8601 timestamp.");
        }

        var text = raw.AsString();
        if (spec.HasTimeZone)
        {
            return DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dto)
                ? Ok(FieldValue.Of(dto.ToString("o", CultureInfo.InvariantCulture)))
                : Fail("Expected an ISO-8601 timestamp.");
        }

        return DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt)
            ? Ok(FieldValue.Of(DateTime.SpecifyKind(dt, DateTimeKind.Unspecified).ToString("yyyy-MM-ddTHH:mm:ss.fffffff", CultureInfo.InvariantCulture)))
            : Fail("Expected an ISO-8601 timestamp.");
    }

    private static Result Ok(FieldValue value) => new(value, null);

    private static Result Fail(string error) => new(FieldValue.Null, error);
}
