using System.Text;
using System.Text.Json;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;

namespace SaveToolkit.Plugins.XorJson;

internal sealed class JsonTokenTree
{
    internal const int MaxBytes = 16 * 1024 * 1024;
    internal const int MaxNodes = 250_000;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    internal static ExactNumberSpec NumberSpec { get; } = new()
    {
        MaxLength = ExactJsonNumber.HardMaxLength,
        MaxAbsoluteExponent = ExactJsonNumber.HardMaxAbsoluteExponent,
    };
    internal static NullableSpec NullSpec { get; } = new(new UnionSpec([
        new("number", "Number", NumberSpec), new("string", "String", new TextSpec { MaxLength = MaxBytes }), new("boolean", "Boolean", new BoolSpec()),
    ]));

    public List<JsonValueToken> Nodes { get; } = [];

    public static JsonTokenTree Parse(byte[] bytes)
    {
        if (bytes.Length is <= 0 or > MaxBytes) throw new SaveFormatException("JSON files must be between 1 byte and 16 MiB.");
        try
        {
            _ = StrictUtf8.GetCharCount(bytes);
            var bom = bytes.AsSpan().StartsWith("\uFEFF"u8) ? 3 : 0;
            var reader = new Utf8JsonReader(bytes.AsSpan(bom), new JsonReaderOptions { MaxDepth = 64 });
            var tree = new JsonTokenTree();
            tree.ReadNodes(bytes, bom, ref reader);
            if (tree.Nodes.Count == 0) throw new SaveFormatException("JSON must contain one value.");
            return tree;
        }
        catch (Exception ex) when (ex is JsonException or DecoderFallbackException or InvalidOperationException or FormatException)
        {
            // Never include source text or parser exceptions, which may quote private save data.
            throw new SaveFormatException("Invalid JSON or UTF-8 text (maximum depth: 64).");
        }
    }

    private void ReadNodes(byte[] bytes, int bom, ref Utf8JsonReader reader)
    {
        var parents = new Stack<JsonValueToken>();
        var occurrences = new Dictionary<(int Parent, string Name), int>();
        string? property = null;
        var propertyOffset = 0;
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.PropertyName)
            {
                property = reader.GetString();
                propertyOffset = checked((int)reader.TokenStartIndex + bom);
                continue;
            }
            if (reader.TokenType is JsonTokenType.EndObject or JsonTokenType.EndArray)
            {
                var completed = parents.Pop();
                completed.Length = checked((int)reader.BytesConsumed + bom - completed.Offset);
                completed.OriginalChildren = completed.Children.Select(n => n.Index).ToArray();
                continue;
            }
            if (Nodes.Count >= MaxNodes) throw new SaveFormatException("JSON exceeds the 250,000 value safety limit.");
            var parent = parents.TryPeek(out var current) ? current : null;
            var label = property ?? (parent is null ? "$" : $"[{parent.Children.Count}]");
            var node = new JsonValueToken(Nodes.Count, label, reader.TokenType,
                checked((int)reader.TokenStartIndex + bom), checked((int)(reader.BytesConsumed - reader.TokenStartIndex)))
            {
                Source = bytes, Parent = parent,
            };
            node.EntryOffset = property is null ? node.Offset : propertyOffset;
            node.LeadingOffset = parent is null ? node.EntryOffset
                : parent.Children.Count == 0 ? parent.Offset + 1 : parent.Children[^1].Offset + parent.Children[^1].Length;
            AttachChild(node, property, occurrences);
            property = null;
            Nodes.Add(node);
            if (node.IsContainer) parents.Push(node);
            else
            {
                ReadValue(node, ref reader);
                node.Original = node.Current;
            }
        }
    }

    private static void AttachChild(JsonValueToken node, string? property, Dictionary<(int Parent, string Name), int> occurrences)
    {
        if (node.Parent is not { } parent) return;
        if (property is null) node.Segment = new JsonElementPath(parent.Children.Count);
        else
        {
            var key = (parent.Index, property);
            var occurrence = occurrences.GetValueOrDefault(key);
            occurrences[key] = occurrence + 1;
            node.Segment = new JsonMemberPath(property, occurrence);
        }
        parent.Children.Add(node);
    }

    private static void ReadValue(JsonValueToken node, ref Utf8JsonReader reader)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.String:
                node.Spec = new NullableSpec(new TextSpec { MaxLength = MaxBytes });
                node.Current = FieldValue.Of(reader.GetString()!);
                break;
            case JsonTokenType.True:
            case JsonTokenType.False:
                node.Spec = new NullableSpec(new BoolSpec());
                node.Current = FieldValue.Of(reader.GetBoolean());
                break;
            case JsonTokenType.Number:
                var raw = Encoding.UTF8.GetString(reader.ValueSpan);
                if (ExactJsonNumber.Validate(raw, NumberSpec) is null)
                {
                    node.Spec = new NullableSpec(NumberSpec);
                    node.Current = FieldValue.ExactNumber(raw);
                }
                else
                {
                    node.Spec = new TextSpec { ReadOnly = true };
                    node.Current = FieldValue.Of(raw);
                    node.Warning = "Number exceeds the exact-number safety limits; preserved verbatim and read-only.";
                }
                break;
            case JsonTokenType.Null:
                node.Spec = NullSpec;
                node.Current = FieldValue.Null;
                break;
        }
    }
}
