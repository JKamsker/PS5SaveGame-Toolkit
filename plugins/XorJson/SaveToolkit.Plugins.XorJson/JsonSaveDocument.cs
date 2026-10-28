using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.XorJson;

internal sealed class JsonSaveDocument(string name, string path, byte[] original, JsonTokenTree tree, JsonEncoding encoding) : BoundSaveDocument
{
    private const int PageSize = 128;
    private readonly Dictionary<string, ChildRange> _ranges = new(StringComparer.Ordinal);

    public override string Title => name;

    public override ViewNode BuildView() => new TabsNode([
        new Tab("Overview", [
            new StatsNode([new Stat("Bytes", original.Length.ToString()), new Stat("Values", tree.Nodes.Count.ToString())]),
            new TextNode(encoding.Label),
            new TextNode("Field names come from the file; game semantics and allowed values are unknown.") { Tone = Tone.Warning },
        ]),
        new Tab("JSON", [NodeView(tree.Nodes[0])]),
    ]);

    public override IReadOnlyList<ViewNode> ExpandNode(string lazyId)
    {
        if (!_ranges.TryGetValue(lazyId, out var range))
        {
            throw new KeyNotFoundException($"Unknown JSON section '{lazyId}'.");
        }

        if (range.Count <= PageSize)
        {
            return range.Parent.Children.Skip(range.Start).Take(range.Count).Select(NodeView).ToArray();
        }

        var children = new List<ViewNode>();
        var chunk = Math.Max(PageSize, (range.Count + PageSize - 1) / PageSize);
        for (var offset = 0; offset < range.Count; offset += chunk)
        {
            var count = Math.Min(chunk, range.Count - offset);
            var start = range.Start + offset;
            children.Add(LazyRange($"Entries {start}–{start + count - 1}", range.Parent, start, count));
        }

        return children;
    }

    public override IReadOnlyList<FileOutput> Serialize()
    {
        var edits = tree.Nodes.Where(n => n.Spec is not null && n.Current != n.Original).ToArray();
        using var output = new MemoryStream();
        var position = 0;
        foreach (var node in edits)
        {
            output.Write(original.AsSpan(position, node.Offset - position));
            var encoded = node.Encode();
            if (output.Length + encoded.Length > JsonTokenTree.MaxBytes)
            {
                throw new SaveFormatException("Edited JSON exceeds the 16 MiB safety limit.");
            }

            output.Write(encoded);
            position = node.Offset + node.Length;
        }

        output.Write(original.AsSpan(position));
        var bytes = output.ToArray();
        if (bytes.Length > JsonTokenTree.MaxBytes) throw new SaveFormatException("Edited JSON exceeds the 16 MiB safety limit.");
        var result = encoding.Transform(bytes, false);
        var decoded = JsonEncoding.Decode(result);
        var reparsed = decoded.Tree;
        if (decoded.Encoding != encoding || reparsed.Nodes.Count != tree.Nodes.Count)
            throw new SaveFormatException("Edited JSON changed its encoding or structure.");
        foreach (var node in tree.Nodes)
        {
            var actual = reparsed.Nodes[node.Index];
            if (actual.Kind != node.Kind || actual.Label != node.Label || actual.Current != node.Current)
            {
                throw new SaveFormatException("Edited JSON failed value round-trip validation.");
            }
        }

        return [new FileOutput(path, result)];
    }

    private ViewNode NodeView(JsonValueToken node)
    {
        if (node.IsContainer)
        {
            return LazyRange(node.Label, node, 0, node.Children.Count);
        }

        if (node.Spec is not { } spec)
        {
            return new TextNode($"{node.Label}: null");
        }

        return Field(node.ValueId, node.Label, spec, () => node.Current,
            spec.ReadOnly ? null : value => node.Current = value, node.Validate) with
        {
            Confidence = Confidence.Unknown,
            Warning = node.Warning ?? "Game-specific constraints and dependencies are unknown.",
            Description = $"Original UTF-8 value token at byte offset 0x{node.Offset:X}.",
        };
    }

    private SectionNode LazyRange(string label, JsonValueToken parent, int start, int count)
    {
        var id = $"children/{parent.Index}/{start}/{count}";
        _ranges[id] = new ChildRange(parent, start, count);
        return SectionNode.Lazy(label, id, count.ToString());
    }

    private sealed record ChildRange(JsonValueToken Parent, int Start, int Count);
}
