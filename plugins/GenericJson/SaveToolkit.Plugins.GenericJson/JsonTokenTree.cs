using System.Text;
using System.Text.Json;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;

namespace SaveToolkit.Plugins.GenericJson;

internal sealed class JsonTokenTree
{
    internal const int MaxBytes = 16 * 1024 * 1024;
    private const int MaxNodes = 250_000;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public List<JsonValueToken> Nodes { get; } = [];

    public static JsonTokenTree Parse(byte[] bytes)
    {
        if (bytes.Length is <= 0 or > MaxBytes)
        {
            throw new SaveFormatException("JSON files must be between 1 byte and 16 MiB.");
        }

        try
        {
            _ = StrictUtf8.GetCharCount(bytes);
            var bom = bytes.AsSpan().StartsWith("\uFEFF"u8) ? 3 : 0;
            var reader = new Utf8JsonReader(bytes.AsSpan(bom), new JsonReaderOptions { MaxDepth = 64 });
            var tree = new JsonTokenTree();
            var parents = new Stack<JsonValueToken>();
            string? property = null;
            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.PropertyName)
                {
                    property = reader.GetString();
                    continue;
                }

                if (reader.TokenType is JsonTokenType.EndObject or JsonTokenType.EndArray)
                {
                    parents.Pop();
                    continue;
                }

                if (tree.Nodes.Count >= MaxNodes)
                {
                    throw new SaveFormatException("JSON exceeds the 250,000 value safety limit.");
                }

                var parent = parents.TryPeek(out var current) ? current : null;
                var label = property ?? (parent is null ? "$" : $"[{parent.Children.Count}]");
                property = null;
                var node = new JsonValueToken(tree.Nodes.Count, label, reader.TokenType,
                    checked((int)reader.TokenStartIndex + bom), checked((int)(reader.BytesConsumed - reader.TokenStartIndex)));
                tree.Nodes.Add(node);
                parent?.Children.Add(node);
                if (node.IsContainer)
                {
                    parents.Push(node);
                }
                else
                {
                    ReadValue(node, ref reader);
                    node.Original = node.Current;
                }
            }

            if (tree.Nodes.Count == 0)
            {
                throw new SaveFormatException("JSON must contain one value.");
            }

            return tree;
        }
        catch (Exception ex) when (ex is JsonException or DecoderFallbackException or InvalidOperationException or FormatException)
        {
            // Do not include input text: JSON may contain private settings.
            throw new SaveFormatException("Invalid JSON or UTF-8 text (maximum depth: 64).", ex);
        }
    }

    private static void ReadValue(JsonValueToken node, ref Utf8JsonReader reader)
    {
        if (reader.TokenType == JsonTokenType.Number)
        {
            var raw = Encoding.UTF8.GetString(reader.ValueSpan);
            if (ExactJsonNumber.Validate(raw, JsonScalar.NumberSpec) is not null)
            {
                node.Spec = new TextSpec { ReadOnly = true };
                node.Current = FieldValue.Of(raw);
                node.Warning = "Number exceeds SDK lexeme/exponent resource limits; preserved verbatim and read-only.";
                return;
            }
        }

        node.Spec = JsonScalar.Spec;
        node.Current = JsonScalar.Read(ref reader);
    }
}
