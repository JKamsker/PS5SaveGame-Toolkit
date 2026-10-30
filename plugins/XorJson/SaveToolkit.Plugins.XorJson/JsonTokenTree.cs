using System.Text.Json;
using System.Runtime.InteropServices;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Json;
using SaveToolkit.Sdk.Values;

namespace SaveToolkit.Plugins.XorJson;

internal sealed class JsonTokenTree
{
    internal const int MaxBytes = LosslessJsonLimits.DefaultMaxBytes;
    internal const int MaxNodes = LosslessJsonLimits.DefaultMaxTokens;
    internal static ExactNumberSpec NumberSpec { get; } = new()
    {
        MaxLength = ExactJsonNumber.HardMaxLength,
        MaxAbsoluteExponent = ExactJsonNumber.HardMaxAbsoluteExponent,
    };
    internal static NullableSpec NullSpec { get; } = new(new UnionSpec([
        new("number", "Number", NumberSpec), new("string", "String", new TextSpec { MaxLength = MaxBytes }), new("boolean", "Boolean", new BoolSpec()),
    ]));

    public List<JsonValueToken> Nodes { get; } = [];
    internal LosslessJsonDocument Lossless { get; private init; } = null!;

    public static JsonTokenTree Parse(byte[] bytes)
    {
        LosslessJsonDocument document;
        try
        {
            document = LosslessJsonDocument.Parse(bytes, new LosslessJsonLimits
            {
                MaxBytes = MaxBytes, MaxDepth = 64, MaxTokens = MaxNodes,
                MaxNumberLength = ExactJsonNumber.HardMaxLength,
                MaxAbsoluteExponent = ExactJsonNumber.HardMaxAbsoluteExponent,
            });
        }
        catch (LosslessJsonException ex)
        {
            throw new SaveFormatException(ParseDiagnostic(ex), ex);
        }

        var tree = new JsonTokenTree { Lossless = document };
        var source = SourceArray(document.OriginalBytes, bytes);
        var mapped = new Dictionary<int, JsonValueToken>();
        foreach (var sourceNode in document.Nodes)
        {
            var parent = sourceNode.Parent is { } sourceParent ? mapped[sourceParent.Id] : null;
            var token = new JsonValueToken(sourceNode.Id, Label(sourceNode, parent), Kind(sourceNode),
                sourceNode.OriginalSpan.Offset, sourceNode.OriginalSpan.Length)
            {
                Source = source, Parent = parent, SdkNode = sourceNode,
            };
            if (parent is null)
            {
                token.EntryOffset = token.Offset;
                token.LeadingOffset = token.Offset;
                token.SdkDocument = document;
            }
            else
            {
                token.Segment = sourceNode.PropertyName is { } name
                    ? new JsonMemberPath(name, sourceNode.PropertyOccurrence)
                    : new JsonElementPath(parent.Children.Count);
                token.LeadingOffset = parent.Children.Count == 0
                    ? parent.Offset + 1 : parent.Children[^1].Offset + parent.Children[^1].Length;
                token.EntryOffset = sourceNode.PropertyNameSpan is { } propertySpan
                    ? propertySpan.Offset : token.Offset;
                parent.Children.Add(token);
            }
            if (!sourceNode.IsScalar) token.OriginalChildren = sourceNode.Children.Select(child => child.Id).ToArray();
            else ReadScalar(token, sourceNode);
            mapped.Add(token.Index, token);
            tree.Nodes.Add(token);
        }
        return tree;
    }

    private static string ParseDiagnostic(LosslessJsonException exception)
    {
        const int maxDiagnosticLength = 256;
        var detail = exception.Message.Length <= maxDiagnosticLength
            ? exception.Message
            : exception.Message[..maxDiagnosticLength] + "...";
        if (exception.ByteOffset is { } offset &&
            !detail.Contains("byte offset", StringComparison.OrdinalIgnoreCase))
            detail += $" (byte offset {offset})";
        return $"Invalid JSON or UTF-8 text: {detail}";
    }

    private static byte[] SourceArray(ReadOnlyMemory<byte> original, byte[] fallback)
    {
        if (MemoryMarshal.TryGetArray(original, out var segment) && segment.Offset == 0 &&
            segment.Count == original.Length && segment.Array is { } source)
            return source;
        return fallback;
    }

    private static string Label(LosslessJsonNode node, JsonValueToken? parent) =>
        node.PropertyName ?? (parent is null ? "$" : $"[{parent.Children.Count}]");

    private static JsonTokenType Kind(LosslessJsonNode node) => node.Kind switch
    {
        LosslessJsonNodeKind.Object => JsonTokenType.StartObject,
        LosslessJsonNodeKind.Array => JsonTokenType.StartArray,
        LosslessJsonNodeKind.String => JsonTokenType.String,
        LosslessJsonNodeKind.Number => JsonTokenType.Number,
        LosslessJsonNodeKind.Boolean => node.BooleanValue == true ? JsonTokenType.True : JsonTokenType.False,
        LosslessJsonNodeKind.Null => JsonTokenType.Null,
        _ => throw new SaveFormatException("Unsupported JSON value kind."),
    };

    private static void ReadScalar(JsonValueToken node, LosslessJsonNode source)
    {
        switch (source.Kind)
        {
            case LosslessJsonNodeKind.String:
                node.Spec = new NullableSpec(new TextSpec { MaxLength = MaxBytes });
                node.Current = FieldValue.Of(source.StringValue!);
                break;
            case LosslessJsonNodeKind.Boolean:
                node.Spec = new NullableSpec(new BoolSpec());
                node.Current = FieldValue.Of(source.BooleanValue == true);
                break;
            case LosslessJsonNodeKind.Number:
                var raw = source.OriginalLexeme!;
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
            case LosslessJsonNodeKind.Null:
                node.Spec = NullSpec;
                node.Current = FieldValue.Null;
                break;
            default:
                throw new SaveFormatException("Unsupported JSON scalar kind.");
        }
        node.Original = node.Current;
    }
}
