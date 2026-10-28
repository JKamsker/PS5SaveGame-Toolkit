using System.Text.Json;
using SaveToolkit.Sdk.Saves;

namespace SaveToolkit.Testing.Diff;

/// <summary>
/// Example decoder for plain JSON saves (UTF-8, optional BOM, optional trailing NUL padding).
/// Object members become <c>parent/name</c>. Arrays of objects that all carry a unique primitive
/// identity property (see <see cref="IdentityKeys"/>) become <c>parent[id=42]</c>, so reordered or
/// inserted records still align; other arrays fall back to the index (<c>parent[3]</c>). Every array
/// also reports its length as <c>parent[#]</c>. Numbers keep their exact source text.
/// </summary>
public sealed class JsonSaveDecoder : ISaveDecoder
{
    public static IReadOnlyList<string> DefaultIdentityKeys { get; } = ["id", "Id", "ID", "guid", "Guid", "GUID", "uuid", "key", "Key", "name", "Name"];

    public JsonSaveDecoder(IReadOnlyList<string>? identityKeys = null) => IdentityKeys = identityKeys ?? DefaultIdentityKeys;

    public string Id => "json";

    /// <summary>Property names tried, in order, as record identity for arrays of objects.</summary>
    public IReadOnlyList<string> IdentityKeys { get; }

    public bool CanDecode(ISaveFile file)
    {
        try
        {
            var header = file.ReadHeader(64).AsSpan();
            if (header.StartsWith("﻿"u8))
            {
                header = header[3..];
            }

            var first = header.IndexOfAnyExcept(" \t\r\n"u8);
            return first >= 0 && header[first] is (byte)'{' or (byte)'[';
        }
        catch (IOException)
        {
            return false;
        }
    }

    public SemanticTree Decode(ISaveFile file, DecodeLimits limits)
    {
        if (file.Length > limits.MaxFileBytes)
        {
            throw new InvalidDataException($"File is {file.Length} bytes; the JSON decoder limit is {limits.MaxFileBytes}.");
        }

        return Decode(file.ReadAllBytes(), limits);
    }

    public SemanticTree Decode(ReadOnlyMemory<byte> content, DecodeLimits limits)
    {
        var span = content.Span;
        var start = span.StartsWith("﻿"u8) ? 3 : 0;
        var end = span.LastIndexOfAnyExcept((byte)0) + 1;
        var options = new JsonDocumentOptions
        {
            AllowTrailingCommas = true,
            CommentHandling = JsonCommentHandling.Skip,
            MaxDepth = limits.MaxDepth,
        };
        using var document = JsonDocument.Parse(content[start..Math.Max(start, end)], options);
        var builder = new SemanticTreeBuilder(Id, limits);
        _ = Walk(document.RootElement, "", builder);
        return builder.Build();
    }

    private bool Walk(JsonElement element, string path, SemanticTreeBuilder builder) => element.ValueKind switch
    {
        JsonValueKind.Object => WalkObject(element, path, builder),
        JsonValueKind.Array => WalkArray(element, path, builder),
        JsonValueKind.String => builder.Add(path, "string", element.GetString()!),
        JsonValueKind.Number => builder.Add(path, "number", element.GetRawText()),
        JsonValueKind.True => builder.Add(path, "bool", "true"),
        JsonValueKind.False => builder.Add(path, "bool", "false"),
        _ => builder.Add(path, "null", "null"),
    };

    private bool WalkObject(JsonElement element, string path, SemanticTreeBuilder builder)
    {
        var any = false;
        foreach (var property in element.EnumerateObject())
        {
            any = true;
            var child = path.Length == 0 ? EscapeSegment(property.Name) : $"{path}/{EscapeSegment(property.Name)}";
            if (!Walk(property.Value, child, builder))
            {
                return false;
            }
        }

        return any || builder.Add(path, "object", "{}");
    }

    private bool WalkArray(JsonElement element, string path, SemanticTreeBuilder builder)
    {
        var length = element.GetArrayLength();
        if (!builder.Add($"{path}[#]", "count", length.ToString(System.Globalization.CultureInfo.InvariantCulture)))
        {
            return false;
        }

        var identity = FindIdentityKey(element);
        var index = 0;
        foreach (var item in element.EnumerateArray())
        {
            var segment = identity is null
                ? $"[{index}]"
                : $"[{identity}={IdentityText(item.GetProperty(identity))}]";
            index++;
            if (!Walk(item, path + segment, builder))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>First identity key present on every element with a unique primitive value, or null.</summary>
    private string? FindIdentityKey(JsonElement array)
    {
        if (array.GetArrayLength() == 0 || array.EnumerateArray().Any(e => e.ValueKind != JsonValueKind.Object))
        {
            return null;
        }

        foreach (var key in IdentityKeys)
        {
            var values = new HashSet<string>(StringComparer.Ordinal);
            var usable = true;
            foreach (var item in array.EnumerateArray())
            {
                if (!item.TryGetProperty(key, out var id)
                    || id.ValueKind is JsonValueKind.Object or JsonValueKind.Array or JsonValueKind.Null
                    || !values.Add(IdentityText(id)))
                {
                    usable = false;
                    break;
                }
            }

            if (usable)
            {
                return key;
            }
        }

        return null;
    }

    private static string IdentityText(JsonElement id) =>
        EscapeSegment(id.ValueKind == JsonValueKind.String ? id.GetString()! : id.GetRawText()).Replace("]", "~2", StringComparison.Ordinal);

    /// <summary>JSON-pointer style escaping so names containing '/' cannot fake a deeper path.</summary>
    private static string EscapeSegment(string name) =>
        name.Replace("~", "~0", StringComparison.Ordinal).Replace("/", "~1", StringComparison.Ordinal);
}
