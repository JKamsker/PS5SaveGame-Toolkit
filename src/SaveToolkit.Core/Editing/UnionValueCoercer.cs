using System.Text.Json;
using SaveToolkit.Sdk.Values;

namespace SaveToolkit.Core.Editing;

internal static class UnionValueCoercer
{
    private static readonly string[] WireProperties = ["kind", "type", "value"];

    public static FieldValueCoercer.Result FromJson(JsonElement json, UnionSpec spec)
    {
        if (json.ValueKind != JsonValueKind.Object ||
            !json.TryGetProperty("kind", out var kind) || kind.ValueKind != JsonValueKind.String || kind.GetString() != "union" ||
            !json.TryGetProperty("type", out var type) || type.ValueKind != JsonValueKind.String ||
            !json.TryGetProperty("value", out var value) ||
            !json.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal).SequenceEqual(WireProperties))
            return Fail("Select a union type and supply its value.");

        var option = Find(spec, type.GetString()!);
        if (option is null) return Fail("Unknown or ambiguous union type.");
        var result = FieldValueCoercer.FromJson(value, option.Spec);
        return result.Ok ? new(FieldValue.Union(option.Id, result.Value), null) : result;
    }

    public static FieldValueCoercer.Result Normalize(FieldValue raw, UnionSpec spec, bool allowReadOnly)
    {
        if (raw.Kind != FieldValueKind.Union) return Fail("Select a union type and supply its value.");
        var union = raw.AsUnion();
        var option = Find(spec, union.Type);
        if (option is null) return Fail("Unknown or ambiguous union type.");
        var result = FieldValueCoercer.Normalize(union.Value, option.Spec, allowReadOnly);
        return result.Ok ? new(FieldValue.Union(option.Id, result.Value), null) : result;
    }

    private static UnionOption? Find(UnionSpec spec, string id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        var matches = spec.Options.Where(o => o.Id == id).Take(2).ToArray();
        return matches.Length == 1 ? matches[0] : null;
    }

    private static FieldValueCoercer.Result Fail(string error) => new(FieldValue.Null, error);
}
