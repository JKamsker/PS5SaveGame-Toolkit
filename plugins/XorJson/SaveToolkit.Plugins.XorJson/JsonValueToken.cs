using System.Globalization;
using System.Text;
using System.Text.Json;
using SaveToolkit.Sdk.Values;

namespace SaveToolkit.Plugins.XorJson;

internal sealed class JsonValueToken(int index, string label, JsonTokenType kind, int offset, int length)
{
    public int Index { get; } = index;
    public string Label { get; } = label;
    public JsonTokenType Kind { get; } = kind;
    public int Offset { get; } = offset;
    public int Length { get; } = length;
    public List<JsonValueToken> Children { get; } = [];
    public FieldSpec? Spec { get; set; }
    public FieldValue Original { get; set; }
    public FieldValue Current { get; set; }
    public string? Warning { get; set; }
    public bool IsContainer => Kind is JsonTokenType.StartObject or JsonTokenType.StartArray;
    public string ValueId => $"value/{Index}";

    public string? Validate(FieldValue value)
    {
        if (value.Kind != Original.Kind)
        {
            return "JSON value types cannot be changed.";
        }

        if (value.Kind == FieldValueKind.Float && !double.IsFinite(value.AsDouble()))
        {
            return "JSON numbers must be finite.";
        }

        if (value.Kind == FieldValueKind.Text)
        {
            var text = value.AsString();
            for (var i = 0; i < text.Length; i++)
            {
                if (char.IsSurrogate(text[i]) && (!char.IsHighSurrogate(text[i]) || ++i >= text.Length || !char.IsLowSurrogate(text[i])))
                {
                    return "Text contains an unpaired Unicode surrogate.";
                }
            }
        }

        return null;
    }

    public byte[] Encode()
    {
        if (Current.Kind == FieldValueKind.Text)
        {
            return JsonSerializer.SerializeToUtf8Bytes(Current.AsString());
        }

        var text = Current.Kind switch
        {
            FieldValueKind.Bool => Current.AsBool() ? "true" : "false",
            FieldValueKind.Integer => Current.AsInteger().ToString(CultureInfo.InvariantCulture),
            FieldValueKind.Float => Current.AsDouble().ToString("R", CultureInfo.InvariantCulture),
            _ => throw new InvalidOperationException("Unsupported JSON field type."),
        };
        // Keep float tokens distinguishable from integral source tokens after reopening.
        if (Current.Kind == FieldValueKind.Float && !text.Contains('.') && !text.Contains('E') && !text.Contains('e'))
        {
            text += ".0";
        }

        return Encoding.UTF8.GetBytes(text);
    }
}
