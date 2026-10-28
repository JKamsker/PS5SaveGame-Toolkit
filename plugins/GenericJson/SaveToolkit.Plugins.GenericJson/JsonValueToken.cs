using System.Text.Json;
using SaveToolkit.Sdk.Values;

namespace SaveToolkit.Plugins.GenericJson;

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

    public string? Validate(FieldValue value) => Spec?.ReadOnly == true ? "Field is read-only." : JsonScalar.Validate(value);

    public byte[] Encode() => JsonScalar.Encode(Current);
}
