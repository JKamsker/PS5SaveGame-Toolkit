using System.Text;
using System.Text.Json;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;

namespace SaveToolkit.Plugins.GenericJson;

internal sealed class JsonTokenTree
{
    internal const int MaxBytes = 16 * 1024 * 1024;
    internal const int MaxNodes = 250_000;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public List<JsonValueToken> Nodes { get; } = [];
    public Dictionary<int, JsonValueToken> ById { get; } = [];
    public ReadOnlyMemory<byte> Preamble { get; private set; }
    public ReadOnlyMemory<byte> Postamble { get; private set; }
    public int NextId { get; set; }

    public JsonValueToken? FindValue(string valueId) => valueId.StartsWith("value/", StringComparison.Ordinal)
        && int.TryParse(valueId.AsSpan("value/".Length), out var id) && ById.TryGetValue(id, out var node) && node.ValueId == valueId ? node : null;

    public static JsonTokenTree Parse(string text)
    {
        try { return Parse(StrictUtf8.GetBytes(text)); }
        catch (EncoderFallbackException)
        {
            throw new SaveFormatException("JSON text contains an unpaired Unicode surrogate.");
        }
    }

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
            var propertyOffset = 0;
            var propertyLength = 0;
            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.PropertyName)
                {
                    property = reader.GetString();
                    propertyOffset = checked((int)reader.TokenStartIndex + bom);
                    propertyLength = reader.ValueSpan.Length + 2;
                    continue;
                }

                if (reader.TokenType is JsonTokenType.EndObject or JsonTokenType.EndArray)
                {
                    var container = parents.Pop();
                    container.Length = checked((int)reader.BytesConsumed + bom - container.Offset);
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
                    checked((int)reader.TokenStartIndex + bom), checked((int)(reader.BytesConsumed - reader.TokenStartIndex)))
                {
                    Source = bytes, Parent = parent,
                    MemberOffset = parent?.Kind == JsonTokenType.StartObject ? propertyOffset : checked((int)reader.TokenStartIndex + bom),
                    MemberNameLength = parent?.Kind == JsonTokenType.StartObject ? propertyLength : 0,
                };
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

            tree.InitializeSpans(bytes);
            tree.NextId = tree.Nodes.Count;
            tree.Reindex();
            return tree;
        }
        catch (Exception ex) when (ex is JsonException or DecoderFallbackException or InvalidOperationException or FormatException)
        {
            // Do not include input text: JSON may contain private settings.
            throw new SaveFormatException("Invalid JSON or UTF-8 text (maximum depth: 64).", ex);
        }
    }

    private void InitializeSpans(byte[] bytes)
    {
        var root = Nodes[0];
        Preamble = bytes.AsMemory(0, root.Offset);
        Postamble = bytes.AsMemory(root.Offset + root.Length);
        foreach (var container in Nodes.Where(n => n.IsContainer))
        {
            var start = container.Offset + 1;
            var end = container.Offset + container.Length - 1;
            var previousEnd = start;
            foreach (var child in container.Children)
            {
                if (previousEnd == start) container.Leading = bytes.AsMemory(start, child.MemberOffset - start);
                else child.SeparatorBefore = bytes.AsMemory(previousEnd, child.MemberOffset - previousEnd);
                previousEnd = child.Offset + child.Length;
            }
            if (container.Children.Count == 0) container.Leading = bytes.AsMemory(start, end - start);
            else container.Trailing = bytes.AsMemory(previousEnd, end - previousEnd);
            container.DefaultSeparator = container.Children.Skip(1).FirstOrDefault()?.SeparatorBefore
                ?? new byte[] { (byte)',' }.Concat(container.Leading.ToArray()).ToArray();
        }
    }

    public void Reindex()
    {
        var root = Nodes[0];
        Nodes.Clear();
        ById.Clear();
        Visit(root, null);
        void Visit(JsonValueToken node, JsonValueToken? parent)
        {
            node.Parent = parent;
            Nodes.Add(node);
            ById.Add(node.Index, node);
            var occurrences = node.Kind == JsonTokenType.StartObject ? new Dictionary<string, int>(StringComparer.Ordinal) : null;
            for (var i = 0; i < node.Children.Count; i++)
            {
                if (node.Kind == JsonTokenType.StartArray) node.Children[i].Label = $"[{i}]";
                var child = node.Children[i];
                child.Position = i;
                child.Occurrence = 1;
                if (occurrences is not null)
                {
                    occurrences.TryGetValue(child.Label, out var previous);
                    child.Occurrence = previous + 1;
                    occurrences[child.Label] = child.Occurrence;
                }
                Visit(node.Children[i], node);
            }
        }
    }

    public JsonTokenTree Clone()
    {
        var clone = new JsonTokenTree { Preamble = Preamble, Postamble = Postamble, NextId = NextId };
        clone.Nodes.Add(Nodes[0].Clone());
        clone.Reindex();
        return clone;
    }

    public static JsonTokenTree FromValue(JsonValueToken value)
    {
        var tree = new JsonTokenTree();
        var root = value.Clone();
        root.Label = "$";
        tree.Nodes.Add(root);
        tree.Reindex();
        return tree;
    }

    public long EstimateBytes()
    {
        const int NodeOverhead = 320;
        return Nodes.Select(n => n.Source).Distinct().Sum(b => (long)b.Length)
            + Nodes.Sum(n => NodeOverhead + 2L * (n.Label.Length + n.OriginalLabel.Length + n.Original.ToString().Length + n.Current.ToString().Length));
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
            node.Spec = JsonScalar.Spec;
            node.Current = FieldValue.Union(JsonScalar.Number, FieldValue.ExactNumber(raw));
            return;
        }

        node.Spec = JsonScalar.Spec;
        node.Current = JsonScalar.Read(ref reader);
    }
}
