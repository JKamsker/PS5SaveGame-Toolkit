using System.Globalization;
using System.Text.Json;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Diff;
using SaveToolkit.Sdk.Saves;

namespace SaveToolkit.Plugins.XorJson;

/// <summary>SDK-only F12 hook sharing the production parser and exact scalar spelling.</summary>
internal sealed class XorJsonDecoder : ISaveDecoderHook
{
    private static readonly string[] IdentityKeys = ["name", "AttributeName", "QuestName", "TaskName", "Tag", "id", "Id", "ID", "guid", "Guid", "key", "Key", "Name"];
    public string Id => "xorjson.tokens";
    public bool CanDecode(ISaveFile file) => new XorJsonPlugin().Probe(file).IsMatch;

    public void Decode(ISaveFile file, ISaveDecodeSink sink)
    {
        if (file.Length > sink.MaxFileBytes) throw new SaveFormatException("File exceeds the semantic decoder byte limit.");
        var decoded = JsonEncoding.Decode(file.ReadAllBytes());
        if (!Walk(decoded.Tree.Nodes[0], "", 0, sink)) sink.MarkTruncated("Semantic output reached the configured value limit.");
    }

    private static bool Walk(JsonValueToken node, string path, int depth, ISaveDecodeSink sink)
    {
        if (depth > sink.MaxDepth) { sink.MarkTruncated("JSON tree exceeds the semantic decoder depth limit."); return false; }
        if (!node.IsContainer)
        {
            var kind = node.Kind switch
            {
                JsonTokenType.String => "string", JsonTokenType.Number => "number",
                JsonTokenType.True or JsonTokenType.False => "bool", _ => "null",
            };
            return sink.Add(path, kind, node.Scalar.ToString());
        }
        if (node.Kind == JsonTokenType.StartArray)
        {
            if (!sink.Add(path + "[#]", "count", node.Children.Count.ToString(CultureInfo.InvariantCulture))) return false;
            var identity = IdentityKeys.FirstOrDefault(key => Unique(node.Children, key));
            for (var index = 0; index < node.Children.Count; index++)
            {
                var child = node.Children[index];
                var suffix = identity is null ? $"[{index}]" : $"[{identity}={Escape(child.Member(identity)!.Scalar.ToString())}]";
                if (!Walk(child, path + suffix, depth + 1, sink)) return false;
            }
            return true;
        }
        if (node.Children.Count == 0) return sink.Add(path, "object", "{}");
        var duplicates = node.Children.GroupBy(child => child.Label, StringComparer.Ordinal)
            .Where(group => group.Count() > 1).Select(group => group.Key).ToHashSet(StringComparer.Ordinal);
        foreach (var child in node.Children)
        {
            var part = Escape(child.Label);
            if (duplicates.Contains(child.Label)) part += $"[occurrence={((JsonMemberPath)child.Segment!).Occurrence}]";
            if (!Walk(child, path.Length == 0 ? part : path + "/" + part, depth + 1, sink)) return false;
        }
        return true;
    }

    private static bool Unique(IReadOnlyList<JsonValueToken> rows, string key)
    {
        if (rows.Count == 0) return false;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var row in rows)
        {
            var value = row.Member(key);
            if (value is null || value.IsContainer || value.Scalar.IsNull || !seen.Add(value.Scalar.ToString())) return false;
        }
        return true;
    }

    private static string Escape(string text) => text.Replace("~", "~0", StringComparison.Ordinal)
        .Replace("/", "~1", StringComparison.Ordinal).Replace("]", "~2", StringComparison.Ordinal)
        .Replace("[", "~3", StringComparison.Ordinal).Replace("=", "~4", StringComparison.Ordinal);
}
