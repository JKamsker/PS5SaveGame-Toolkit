using System.Globalization;
using System.Text.Json;

namespace SaveToolkit.Plugins.GenericJson;

internal static class JsonPaths
{
    private const int MaxDisplayPath = 2048;
    private const int MaxDisplayKey = 256;
    private const int MaxIdentitySuffixLength = 32;
    private const int MaxIndexLength = 11;

    public static string ForNode(JsonValueToken node)
    {
        Span<char> path = stackalloc char[MaxDisplayPath + MaxIdentitySuffixLength];
        return new string(path[..Format(node, path)]);
    }

    public static bool Contains(JsonValueToken node, string text)
    {
        Span<char> path = stackalloc char[MaxDisplayPath + MaxIdentitySuffixLength];
        return path[..Format(node, path)].Contains(text, StringComparison.OrdinalIgnoreCase);
    }

    private static int Format(JsonValueToken node, Span<char> path)
    {
        var length = Write(node, path);
        if (length <= MaxDisplayPath) return length;
        " (".AsSpan().CopyTo(path[length..]);
        length += 2;
        node.ValueId.AsSpan().CopyTo(path[length..]);
        length += node.ValueId.Length;
        path[length++] = ')';
        return length;
    }

    private static int Write(JsonValueToken node, Span<char> path)
    {
        if (node.Parent is null) { path[0] = '$'; return 1; }
        var length = Write(node.Parent, path);
        if (length > MaxDisplayPath) return length;
        Append(path, ref length, "[");
        Span<char> number = stackalloc char[MaxIndexLength];
        if (node.Parent.Kind == JsonTokenType.StartArray)
        {
            _ = node.Position.TryFormat(number, out var digits, provider: CultureInfo.InvariantCulture);
            Append(path, ref length, number[..digits]);
            Append(path, ref length, "]");
        }
        else
        {
            node.EscapedPathLabel ??= JsonSerializer.Serialize(Clip(node.Label, MaxDisplayKey));
            Append(path, ref length, node.EscapedPathLabel);
            Append(path, ref length, "]#");
            _ = node.Occurrence.TryFormat(number, out var digits, provider: CultureInfo.InvariantCulture);
            Append(path, ref length, number[..digits]);
        }
        return length;
    }

    private static void Append(Span<char> path, ref int length, ReadOnlySpan<char> text)
    {
        if (length > MaxDisplayPath) return;
        var copied = Math.Min(text.Length, MaxDisplayPath - length);
        text[..copied].CopyTo(path[length..]);
        length += copied;
        if (copied < text.Length) path[length++] = '…';
    }

    private static string Clip(string value, int limit) => value.Length <= limit ? value : value[..limit] + "…";
}
