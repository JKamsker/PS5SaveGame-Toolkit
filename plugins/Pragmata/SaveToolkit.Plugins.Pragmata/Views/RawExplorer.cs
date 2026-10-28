using SaveToolkit.Plugins.Pragmata.Format;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.Pragmata.Views;

internal sealed class RawExplorer(PragmataDocument document, LazyPages pages)
{
    public IReadOnlyList<ViewNode> Roots() => document.Layout.Roots.Select(Node).ToArray();

    private ViewNode Node(SaveNode node)
    {
        var label = node.Index >= 0 ? $"[{node.Index}]" : HashNames.Label(node.Hash);
        if (node.Type is FieldTags.Array or FieldTags.Class)
        {
            var title = node.Type == FieldTags.Class ? $"{label} · {HashNames.Label(node.ClassHash)}" : label;
            return pages.Section($"tree/{node.Path}", title, node.Children.Count,
                (start, count) => node.Children.Skip(start).Take(count).Select(Node).ToArray());
        }
        if (node.IsPrimitive)
        {
            var value = document.Read(node);
            if (node.Type is FieldTags.Float32 or FieldTags.Float64 && !double.IsFinite(value.AsDouble()))
                return new TextNode($"{label}: {value} (non-finite, read-only) · {node.Path}") { Monospace = true };
            return document.Field(node, raw: true) with { Label = label };
        }
        return new TextNode($"{label}: {document.Preview(node)} · {node.Size} bytes · {node.Path} (read-only)") { Monospace = node.Type == FieldTags.Struct };
    }

}
