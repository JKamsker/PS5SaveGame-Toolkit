using System.Text;
using System.Text.Json;
using SaveToolkit.Sdk.Values;

namespace SaveToolkit.Sdk.Json;

internal readonly record struct ParsedScalar(LosslessJsonNodeKind Kind, string Lexeme, string? StringValue);

internal static class LosslessJsonScalar
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public static ParsedScalar FromReader(Utf8JsonReader reader, string lexeme, LosslessJsonLimits limits)
    {
        var kind = reader.TokenType switch
        {
            JsonTokenType.String => LosslessJsonNodeKind.String,
            JsonTokenType.Number => LosslessJsonNodeKind.Number,
            JsonTokenType.True or JsonTokenType.False => LosslessJsonNodeKind.Boolean,
            JsonTokenType.Null => LosslessJsonNodeKind.Null,
            _ => throw new LosslessJsonException("Expected a JSON scalar.")
        };
        // Parsing preserves valid oversized number syntax exactly; replacement validation is stricter
        // and uses ExactJsonNumber. This matches GenericJson's read-only resource fallback.
        return new ParsedScalar(kind, lexeme, kind == LosslessJsonNodeKind.String ? reader.GetString() : null);
    }

    public static ParsedScalar Parse(string lexeme, LosslessJsonLimits limits)
    {
        if (lexeme.Length == 0) throw new LosslessJsonException("A scalar replacement cannot be empty.");
        byte[] bytes;
        try { bytes = StrictUtf8.GetBytes(lexeme); }
        catch (EncoderFallbackException ex) { throw new LosslessJsonException("Replacement contains an unpaired Unicode surrogate.", ex.Index, ex); }
        var reader = new Utf8JsonReader(bytes, new JsonReaderOptions());
        if (!reader.Read() || reader.TokenStartIndex != 0 || reader.BytesConsumed != bytes.Length ||
            reader.TokenType is JsonTokenType.StartObject or JsonTokenType.EndObject or JsonTokenType.StartArray or JsonTokenType.EndArray or JsonTokenType.PropertyName)
            throw new LosslessJsonException("Replacement must be exactly one JSON scalar.");
        ParsedScalar parsed;
        try { parsed = FromReader(reader, lexeme, limits); }
        catch (InvalidOperationException ex)
        {
            throw new LosslessJsonException("Replacement contains an invalid Unicode escape.", null, ex);
        }
        if (parsed.Kind == LosslessJsonNodeKind.Number)
        {
            var error = ExactJsonNumber.Validate(lexeme, new ExactNumberSpec
            {
                MaxLength = Math.Min(limits.MaxNumberLength, ExactJsonNumber.HardMaxLength),
                MaxAbsoluteExponent = Math.Min(limits.MaxAbsoluteExponent, ExactJsonNumber.HardMaxAbsoluteExponent)
            });
            if (error is not null) throw new LosslessJsonException(error);
        }
        return parsed;
    }
}
