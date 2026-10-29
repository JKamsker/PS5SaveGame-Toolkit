using System.Text;
using System.Text.Json;

namespace SaveToolkit.Sdk.Json;

internal static class LosslessJsonParser
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public static LosslessJsonDocument Parse(byte[] source, LosslessJsonLimits limits)
    {
        var bom = source.AsSpan().StartsWith("\uFEFF"u8) ? 3 : 0;
        var reader = new Utf8JsonReader(source.AsSpan(bom), new JsonReaderOptions { MaxDepth = limits.MaxDepth });
        var nodes = new List<LosslessJsonNode>();
        var stack = new Stack<LosslessJsonNode>();
        var occurrences = new Stack<Dictionary<string, int>>();
        PendingProperty pending = default;
        LosslessJsonNode? root = null;
        var valueCount = 0;
        var complete = false;

        while (reader.Read())
        {
            var start = checked((int)reader.TokenStartIndex + bom);
            switch (reader.TokenType)
            {
                case JsonTokenType.PropertyName:
                    if (stack.Count == 0 || stack.Peek().Kind != LosslessJsonNodeKind.Object || pending.Name is not null)
                        throw new LosslessJsonException("Unexpected JSON property name.", start);
                    var propertyLength = checked(reader.ValueSpan.Length + 2);
                    pending = new PendingProperty(reader.GetString()!, Lexeme(source, start, propertyLength),
                        new JsonByteSpan(start, propertyLength));
                    break;
                case JsonTokenType.StartObject:
                case JsonTokenType.StartArray:
                    EnsureValueAllowed(stack, pending, complete, start);
                    if (++valueCount > limits.MaxTokens)
                        throw new LosslessJsonException($"JSON exceeds the {limits.MaxTokens}-value safety limit.", start);
                    var containerKind = reader.TokenType == JsonTokenType.StartObject ? LosslessJsonNodeKind.Object : LosslessJsonNodeKind.Array;
                    var container = AddNode(nodes, stack, occurrences, pending, new JsonByteSpan(start, 1),
                        containerKind, containerKind == LosslessJsonNodeKind.Object ? "{" : "[", null);
                    pending = default;
                    root ??= container;
                    stack.Push(container);
                    occurrences.Push(container.Kind == LosslessJsonNodeKind.Object ? new(StringComparer.Ordinal) : []);
                    if (stack.Count > limits.MaxDepth) throw new LosslessJsonException("JSON exceeds the configured depth limit.", start);
                    break;
                case JsonTokenType.EndObject:
                case JsonTokenType.EndArray:
                    if (stack.Count == 0 || (reader.TokenType == JsonTokenType.EndObject && stack.Peek().Kind != LosslessJsonNodeKind.Object) ||
                        (reader.TokenType == JsonTokenType.EndArray && stack.Peek().Kind != LosslessJsonNodeKind.Array) || pending.Name is not null)
                        throw new LosslessJsonException("Unexpected JSON container end.", start);
                    var ended = stack.Pop();
                    _ = occurrences.Pop();
                    ended.OriginalSpan = new JsonByteSpan(ended.OriginalSpan.Offset,
                        checked((int)reader.BytesConsumed + bom - ended.OriginalSpan.Offset));
                    if (stack.Count == 0) complete = true;
                    break;
                default:
                    EnsureValueAllowed(stack, pending, complete, start);
                    if (++valueCount > limits.MaxTokens)
                        throw new LosslessJsonException($"JSON exceeds the {limits.MaxTokens}-value safety limit.", start);
                    var tokenLength = checked((int)(reader.BytesConsumed - reader.TokenStartIndex));
                    var scalar = LosslessJsonScalar.FromReader(reader, Lexeme(source, start, tokenLength), limits);
                    var node = AddNode(nodes, stack, occurrences, pending, new JsonByteSpan(start, tokenLength),
                        scalar.Kind, scalar.Lexeme, scalar.StringValue);
                    pending = default;
                    root ??= node;
                    if (stack.Count == 0) complete = true;
                    break;
            }
        }

        if (root is null || stack.Count != 0 || pending.Name is not null || !complete)
            throw new LosslessJsonException("JSON must contain exactly one complete value.");
        return new LosslessJsonDocument(source, limits, nodes);
    }

    private static LosslessJsonNode AddNode(List<LosslessJsonNode> nodes, Stack<LosslessJsonNode> stack,
        Stack<Dictionary<string, int>> occurrences, PendingProperty pending, JsonByteSpan span,
        LosslessJsonNodeKind kind, string lexeme, string? stringValue)
    {
        var node = new LosslessJsonNode(nodes.Count, kind, span)
        {
            PropertyName = pending.Name,
            PropertyNameLexeme = pending.Lexeme,
            PropertyNameSpan = pending.Span,
            StringValue = stringValue,
            ImportedStringValue = stringValue,
            BooleanValue = kind == LosslessJsonNodeKind.Boolean && lexeme == "true" ? true
                : kind == LosslessJsonNodeKind.Boolean && lexeme == "false" ? false : null,
            ImportedBooleanValue = kind == LosslessJsonNodeKind.Boolean && lexeme == "true" ? true
                : kind == LosslessJsonNodeKind.Boolean && lexeme == "false" ? false : null,
            OriginalLexeme = kind is LosslessJsonNodeKind.Object or LosslessJsonNodeKind.Array ? null : lexeme,
            CurrentLexeme = kind is LosslessJsonNodeKind.Object or LosslessJsonNodeKind.Array ? null : lexeme,
        };
        if (stack.Count > 0)
        {
            var parent = stack.Peek();
            node.Parent = parent;
            if (parent.Kind == LosslessJsonNodeKind.Object)
            {
                var names = occurrences.Peek();
                names.TryGetValue(pending.Name!, out var prior);
                node.PropertyOccurrence = prior;
                names[pending.Name!] = prior + 1;
            }
            parent.MutableChildren.Add(node);
        }
        nodes.Add(node);
        return node;
    }

    private static void EnsureValueAllowed(Stack<LosslessJsonNode> stack, PendingProperty pending, bool complete, int offset)
    {
        if (complete || (stack.Count > 0 && stack.Peek().Kind == LosslessJsonNodeKind.Object && pending.Name is null))
            throw new LosslessJsonException("A JSON value is missing its object member name or the document has multiple roots.", offset);
        if (stack.Count > 0 && stack.Peek().Kind == LosslessJsonNodeKind.Array && pending.Name is not null)
            throw new LosslessJsonException("Unexpected JSON property name in an array.", offset);
    }

    private static string Lexeme(byte[] source, int offset, int length) => StrictUtf8.GetString(source, offset, length);
    private readonly record struct PendingProperty(string? Name, string? Lexeme, JsonByteSpan? Span);
}
