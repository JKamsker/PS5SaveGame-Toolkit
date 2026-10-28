using System.Text.Json;

namespace SaveToolkit.Plugins.GenericJson;

internal static class JsonPaths
{
    private const int MaxDisplayPath = 2048;
    private const int MaxDisplayKey = 256;

    public static IEnumerable<(JsonValueToken Node, string Path)> Walk(JsonValueToken root) => Walk(root, "$");

    public static string ForNode(JsonValueToken node)
    {
        var chain = new Stack<JsonValueToken>();
        for (var current = node; current.Parent is not null; current = current.Parent) chain.Push(current);
        var path = "$";
        foreach (var child in chain)
        {
            var segment = child.Parent!.Kind == JsonTokenType.StartArray ? $"[{child.Position}]"
                : $"[{JsonSerializer.Serialize(Clip(child.Label, MaxDisplayKey))}]#{child.Occurrence}";
            path = Clip(path + segment, MaxDisplayPath);
        }
        return path.EndsWith('…') ? path + $" ({node.ValueId})" : path;
    }

    private static IEnumerable<(JsonValueToken Node, string Path)> Walk(JsonValueToken parent, string path)
    {
        yield return (parent, path);
        for (var i = 0; i < parent.Children.Count; i++)
        {
            var child = parent.Children[i];
            var segment = parent.Kind == JsonTokenType.StartArray ? $"[{i}]"
                : $"[{JsonSerializer.Serialize(Clip(child.Label, MaxDisplayKey))}]#{child.Occurrence}";
            var childPath = Clip(path + segment, MaxDisplayPath);
            if (childPath.EndsWith('…')) childPath += $" ({child.ValueId})";
            foreach (var descendant in Walk(child, childPath)) yield return descendant;
        }
    }

    private static string Clip(string value, int limit) => value.Length <= limit ? value : value[..limit] + "…";
}
