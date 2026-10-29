using System.Text;
using System.Text.Json;
using SaveToolkit.Sdk.Json;
using SaveToolkit.Sdk.Values;

namespace SaveToolkit.Plugins.XorJson;

internal sealed class JsonValueToken(int index, string label, JsonTokenType kind, int offset, int length)
{
    public int Index { get; set; } = index;
    public string Label { get; set; } = label;
    public JsonTokenType Kind { get; } = kind;
    public int Offset { get; } = offset;
    public int Length { get; set; } = length;
    public int EntryOffset { get; set; } = offset;
    public int LeadingOffset { get; set; } = offset;
    public byte[] Source { get; init; } = [];
    public JsonValueToken? Parent { get; set; }
    public JsonPathSegment? Segment { get; set; }
    public List<JsonValueToken> Children { get; } = [];
    public int[] OriginalChildren { get; set; } = [];
    public FieldSpec? Spec { get; set; }
    public FieldValue Original { get; set; }
    public FieldValue Current { get; set; }
    public string? Warning { get; set; }
    internal LosslessJsonNode? SdkNode { get; init; }
    internal LosslessJsonDocument? SdkDocument { get; set; }
    public bool IsContainer => Kind is JsonTokenType.StartObject or JsonTokenType.StartArray;
    public string ValueId => $"value/{Index}";
    public string TypedPath => (Parent?.TypedPath ?? "$") + (Segment?.Display ?? "");
    public FieldValue Scalar => Current.Kind == FieldValueKind.Union ? Current.AsUnion().Value : Current;

    public JsonValueToken? Member(string name)
    {
        JsonValueToken? result = null;
        foreach (var child in Children)
        {
            if (child.Segment is not JsonMemberPath p || p.Name != name) continue;
            if (result is not null) return null; // Ambiguous semantic keys stay visible in the raw tree.
            result = child;
        }
        return result;
    }

    public string? Validate(FieldValue value)
    {
        if (Spec?.ReadOnly == true) return "This token exceeds editable limits.";
        var inner = Spec is NullableSpec nullable ? nullable.Inner : Spec;
        if (value.IsNull && Spec is NullableSpec) return null;
        if (inner is UnionSpec union)
        {
            if (value.Kind != FieldValueKind.Union) return "Choose an explicit JSON scalar type.";
            var choice = value.AsUnion();
            inner = union.Options.FirstOrDefault(o => o.Id == choice.Type)?.Spec;
            if (inner is null) return "Unknown JSON scalar type.";
            value = choice.Value;
        }
        if (inner is ExactNumberSpec exact)
            return value.Kind == FieldValueKind.ExactNumber
                ? ExactJsonNumber.Validate(value.AsExactNumber(), exact) : "An exact JSON number is required.";
        if (inner is BoolSpec) return value.Kind == FieldValueKind.Bool ? null : "A boolean is required.";
        if (inner is not (TextSpec or ReferenceSpec) || value.Kind != FieldValueKind.Text)
            return "The value does not match the JSON scalar type.";
        var text = value.AsString();
        if (inner is TextSpec { MaxLength: { } limit } && text.Length > limit) return "Text exceeds the field length limit.";
        for (var i = 0; i < text.Length; i++)
            if (char.IsSurrogate(text[i]) && (!char.IsHighSurrogate(text[i]) || ++i >= text.Length || !char.IsLowSurrogate(text[i])))
                return "Text contains an unpaired Unicode surrogate.";
        return null;
    }

    public byte[] Encode() => Scalar.Kind switch
    {
        FieldValueKind.Text => JsonSerializer.SerializeToUtf8Bytes(Scalar.AsString()),
        FieldValueKind.ExactNumber => Encoding.UTF8.GetBytes(Scalar.AsExactNumber()),
        FieldValueKind.Bool => Scalar.AsBool() ? "true"u8.ToArray() : "false"u8.ToArray(),
        FieldValueKind.Null => "null"u8.ToArray(),
        _ => throw new InvalidOperationException("Unsupported JSON scalar type."),
    };
}

internal abstract record JsonPathSegment
{
    public abstract string Display { get; }
}

internal sealed record JsonMemberPath(string Name, int Occurrence) : JsonPathSegment
{
    public override string Display => $".member({JsonSerializer.Serialize(Name)},{Occurrence})";
}

internal sealed record JsonElementPath(int Position) : JsonPathSegment
{
    public override string Display => $".element({Position})";
}
