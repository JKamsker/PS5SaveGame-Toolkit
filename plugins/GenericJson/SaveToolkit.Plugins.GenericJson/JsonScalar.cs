using System.Text;
using System.Text.Json;
using SaveToolkit.Sdk.Values;

namespace SaveToolkit.Plugins.GenericJson;

/// <summary>The raw workbench permits explicit scalar type selection, never implicit coercion.</summary>
internal static class JsonScalar
{
    internal const string Number = "number";
    internal const string Text = "string";
    internal const string Boolean = "boolean";
    public static ExactNumberSpec NumberSpec { get; } = new()
    {
        MaxLength = ExactJsonNumber.HardMaxLength,
        MaxAbsoluteExponent = ExactJsonNumber.HardMaxAbsoluteExponent,
    };
    public static FieldSpec Spec { get; } = new NullableSpec(new UnionSpec([
        new(Number, "JSON number (exact)", NumberSpec),
        new(Text, "JSON string", new TextSpec { Multiline = true, MaxLength = JsonTokenTree.MaxBytes }),
        new(Boolean, "JSON boolean", new BoolSpec()),
    ]));

    public static string? Validate(FieldValue value)
    {
        if (value.IsNull) return null;
        if (value.Kind != FieldValueKind.Union) return "Select an explicit JSON scalar type.";
        var choice = value.AsUnion();
        if (choice.Type == Number && choice.Value.Kind == FieldValueKind.ExactNumber)
            return ExactJsonNumber.Validate(choice.Value.AsExactNumber(), NumberSpec);
        if (choice.Type == Boolean && choice.Value.Kind == FieldValueKind.Bool) return null;
        if (choice.Type != Text || choice.Value.Kind != FieldValueKind.Text) return "Value does not match its JSON type choice.";
        var text = choice.Value.AsString();
        if (text.Length > JsonTokenTree.MaxBytes) return "String exceeds the JSON file size limit.";
        for (var i = 0; i < text.Length; i++)
            if (char.IsSurrogate(text[i]) && (!char.IsHighSurrogate(text[i]) || ++i >= text.Length || !char.IsLowSurrogate(text[i])))
                return "Text contains an unpaired Unicode surrogate.";
        return null;
    }

    public static byte[] Encode(FieldValue value)
    {
        if (value.IsNull) return "null"u8.ToArray();
        var scalar = value.AsUnion().Value;
        return scalar.Kind switch
        {
            FieldValueKind.Text => JsonSerializer.SerializeToUtf8Bytes(scalar.AsString()),
            FieldValueKind.ExactNumber => Encoding.UTF8.GetBytes(scalar.AsExactNumber()),
            FieldValueKind.Bool => scalar.AsBool() ? "true"u8.ToArray() : "false"u8.ToArray(),
            _ => throw new InvalidOperationException("Unsupported JSON scalar type."),
        };
    }

    /// <summary>Counts the exact UTF-8 bytes emitted by the default JSON scalar encoder.</summary>
    public static int EncodedLength(FieldValue value)
    {
        if (value.IsNull) return 4;
        var scalar = value.AsUnion().Value;
        return scalar.Kind switch
        {
            FieldValueKind.Bool => scalar.AsBool() ? 4 : 5,
            FieldValueKind.ExactNumber => checked(Encoding.UTF8.GetByteCount(scalar.AsExactNumber())),
            FieldValueKind.Text => StringEncodedLength(scalar.AsString()),
            _ => throw new InvalidOperationException("Unsupported JSON scalar type."),
        };
    }

    private static int StringEncodedLength(string value)
    {
        var length = 2; // JSON quotes.
        for (var i = 0; i < value.Length; i++)
        {
            var character = value[i];
            if (character is >= '\uD800' and <= '\uDBFF')
            {
                if (++i >= value.Length || value[i] is < '\uDC00' or > '\uDFFF')
                    throw new InvalidOperationException("Text contains an unpaired Unicode surrogate.");
                length = checked(length + 12); // Default encoder emits two \uXXXX escapes.
                continue;
            }

            if (character is >= '\uDC00' and <= '\uDFFF')
                throw new InvalidOperationException("Text contains an unpaired Unicode surrogate.");
            length = checked(length + character switch
            {
                '\\' => 2,
                '\b' or '\f' or '\n' or '\r' or '\t' => 2,
                // Match JsonSerializer's default encoder, including its ASCII hex escapes.
                < '\u0020' or '"' or '+' or '`' or '<' or '>' or '&' or '\'' or > '\u007E' => 6,
                _ => 1,
            });
        }
        return length;
    }

    public static string TypeOf(FieldValue value) => value.IsNull ? "null" : value.AsUnion().Type;

    // Keep explicit JSON transitions alongside typed F6 values, including missing versus present null.
    public static string ReviewTransition(string label, FieldValue before, FieldValue after, bool oldExists = true, bool newExists = true) =>
        $"{label}: {Literal(before, oldExists)} → {Literal(after, newExists)}";

    public static string Literal(FieldValue value, bool exists = true) => !exists ? "(absent)" : value.Kind == FieldValueKind.Text
        ? value.AsString() // Read-only number resource fallback: already a validated JSON number lexeme.
        : Encoding.UTF8.GetString(Encode(value));
}
