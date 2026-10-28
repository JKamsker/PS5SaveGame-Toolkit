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

    public static FieldValue Read(ref Utf8JsonReader reader) => reader.TokenType switch
    {
        JsonTokenType.Null => FieldValue.Null,
        JsonTokenType.String => FieldValue.Union(Text, FieldValue.Of(reader.GetString()!)),
        JsonTokenType.True or JsonTokenType.False => FieldValue.Union(Boolean, FieldValue.Of(reader.GetBoolean())),
        JsonTokenType.Number => FieldValue.Union(Number, FieldValue.ExactNumber(Encoding.UTF8.GetString(reader.ValueSpan))),
        _ => throw new InvalidOperationException("Expected a JSON scalar."),
    };

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

    public static string TypeOf(FieldValue value) => value.IsNull ? "null" : value.AsUnion().Type;

    // F6's current browser formatter uses String(value) for tagged values. Keep exact, typed effects
    // and also place JSON literal transitions in preview labels until that shared formatter supports F10.
    public static string ReviewTransition(string label, FieldValue before, FieldValue after, bool oldExists = true, bool newExists = true) =>
        $"{label}: {Literal(before, oldExists)} → {Literal(after, newExists)}";

    private static string Literal(FieldValue value, bool exists) => !exists ? "(absent)" : value.Kind == FieldValueKind.Text
        ? value.AsString() // Read-only number resource fallback: already a validated JSON number lexeme.
        : Encoding.UTF8.GetString(Encode(value));
}
