using System.Buffers;
using System.Text;
using System.Text.Json;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;

namespace SaveToolkit.Plugins.GenericJson;

/// <summary>Reparses complete bytes without constructing another editable token tree.</summary>
internal static class JsonTokenValidation
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public static void Validate(byte[] bytes, JsonTokenTree? expected = null)
    {
        if (bytes.Length is <= 0 or > JsonTokenTree.MaxBytes)
            throw new SaveFormatException("JSON files must be between 1 byte and 16 MiB.");
        long tokenOffset = 0;
        char[]? rented = null;
        try
        {
            _ = StrictUtf8.GetCharCount(bytes);
            ValidateTokens(bytes, expected, ref tokenOffset, ref rented);
        }
        catch (DecoderFallbackException ex)
        {
            throw new SaveFormatException($"Invalid UTF-8 text at byte offset {ex.Index}.", ex);
        }
        catch (JsonException ex)
        {
            throw new SaveFormatException($"Invalid JSON at line {ex.LineNumber + 1}, byte position {ex.BytePositionInLine} (maximum depth: {JsonTokenTree.MaxDepth}).", ex);
        }
        catch (Exception ex) when (ex is InvalidOperationException or FormatException)
        {
            throw new SaveFormatException($"Invalid JSON string or Unicode escape at byte offset {tokenOffset}.", ex);
        }
        finally
        {
            if (rented is not null) ArrayPool<char>.Shared.Return(rented);
        }
    }

    private static void ValidateTokens(byte[] bytes, JsonTokenTree? expected, ref long tokenOffset, ref char[]? rented)
    {
        var bom = bytes.AsSpan().StartsWith("\uFEFF"u8) ? 3 : 0;
        var reader = new Utf8JsonReader(bytes.AsSpan(bom), new JsonReaderOptions { MaxDepth = JsonTokenTree.MaxDepth });
        Span<char> scratch = stackalloc char[256];
        Span<int> childCounts = stackalloc int[JsonTokenTree.MaxDepth + 1];
        childCounts.Clear();
        var count = 0;
        JsonValueToken? parent = null;
        while (reader.Read())
        {
            tokenOffset = reader.TokenStartIndex + bom;
            // The reader checks JSON syntax; decoding escapes also checks paired Unicode surrogates.
            if (reader.TokenType is JsonTokenType.String or JsonTokenType.PropertyName && reader.ValueIsEscaped)
            {
                if (reader.ValueSpan.Length > scratch.Length)
                {
                    var replacement = ArrayPool<char>.Shared.Rent(reader.ValueSpan.Length);
                    if (rented is not null) ArrayPool<char>.Shared.Return(rented);
                    rented = replacement;
                    scratch = rented;
                }
                _ = reader.CopyString(scratch);
            }
            if (reader.TokenType == JsonTokenType.PropertyName)
            {
                if (expected is not null && (count >= expected.Nodes.Count || !reader.ValueTextEquals(expected.Nodes[count].Label)))
                    Mismatch();
                continue;
            }
            if (reader.TokenType is JsonTokenType.EndArray or JsonTokenType.EndObject)
            {
                if (expected is not null)
                {
                    if (parent is null || childCounts[reader.CurrentDepth + 1] != parent.Children.Count) Mismatch();
                    parent = parent!.Parent;
                }
                continue;
            }
            if (++count > JsonTokenTree.MaxNodes)
                throw new SaveFormatException("JSON exceeds the 250,000 value safety limit.");
            if (expected is null) continue;
            CompareNode(expected, count, ref reader, ref parent, childCounts, scratch);
        }
        if (count == 0) throw new SaveFormatException("JSON must contain one value.");
        if (expected is not null && count != expected.Nodes.Count) Mismatch();
    }

    private static void CompareNode(JsonTokenTree expected, int count, ref Utf8JsonReader reader,
        ref JsonValueToken? parent, scoped Span<int> childCounts, scoped Span<char> scratch)
    {
        if (count > expected.Nodes.Count) Mismatch();
        var node = expected.Nodes[count - 1];
        if (!ReferenceEquals(node.Parent, parent)) Mismatch();
        if (parent?.Kind == JsonTokenType.StartArray)
        {
            var position = childCounts[reader.CurrentDepth];
            scratch[0] = '[';
            _ = position.TryFormat(scratch[1..], out var digits);
            scratch[digits + 1] = ']';
            if (!node.Label.AsSpan().SequenceEqual(scratch[..(digits + 2)])) Mismatch();
        }
        else if (parent is null && node.Label != "$") Mismatch();
        childCounts[reader.CurrentDepth]++;
        if (node.IsContainer)
        {
            if (reader.TokenType != node.Kind) Mismatch();
            parent = node;
            childCounts[reader.CurrentDepth + 1] = 0;
        }
        else if (!ScalarEquals(node.Current, ref reader)) Mismatch();
    }

    private static bool ScalarEquals(FieldValue value, ref Utf8JsonReader reader)
    {
        if (value.IsNull) return reader.TokenType == JsonTokenType.Null;
        // Read-only resource fallback numbers are deliberately plain text, never converted to double.
        if (value.Kind == FieldValueKind.Text) return NumberEquals(value.AsString(), ref reader);
        var scalar = value.AsUnion();
        return scalar.Type switch
        {
            JsonScalar.Text => reader.TokenType == JsonTokenType.String && reader.ValueTextEquals(scalar.Value.AsString()),
            JsonScalar.Boolean => reader.TokenType == (scalar.Value.AsBool() ? JsonTokenType.True : JsonTokenType.False),
            JsonScalar.Number => NumberEquals(scalar.Value.AsExactNumber(), ref reader),
            _ => false,
        };
    }

    private static bool NumberEquals(string number, ref Utf8JsonReader reader)
    {
        if (reader.TokenType != JsonTokenType.Number || reader.ValueSpan.Length != number.Length) return false;
        for (var i = 0; i < number.Length; i++)
            if (reader.ValueSpan[i] != number[i]) return false;
        return true;
    }

    private static void Mismatch() => throw new SaveFormatException("Edited JSON failed token round-trip validation.");
}
