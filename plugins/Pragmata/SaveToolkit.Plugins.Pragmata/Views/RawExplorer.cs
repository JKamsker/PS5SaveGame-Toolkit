using SaveToolkit.Plugins.Pragmata.Format;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.Pragmata.Views;

internal sealed class RawExplorer(PragmataDocument document, LazyPages pages)
{
    public IReadOnlyList<ViewNode> Roots() => document.Layout.Roots.Select(node => Node(node)).ToArray();

    public ViewNode Node(SaveNode node, bool raw = true)
    {
        var label = node.Index >= 0 ? $"[{node.Index}]" : HashNames.Label(node.Hash);
        if (node.Type is FieldTags.Array or FieldTags.Class)
        {
            var title = node.Type == FieldTags.Class ? $"{label} · {HashNames.Label(node.ClassHash)}" : label;
            var prefix = raw ? "tree" : "header";
            return pages.Section($"{prefix}/{node.Path}", title, node.Children.Count,
                (start, count) => node.Children.Skip(start).Take(count).Select(child => Node(child, raw)).ToArray()) with
            { Description = $"{HashNames.Provenance(node.Hash)} Class: {HashNames.Provenance(node.ClassHash)} {document.References.SchemaDescription(node)}" };
        }
        if (node.IsPrimitive)
        {
            var value = document.Read(node);
            if (node.Type is FieldTags.Float32 or FieldTags.Float64 && !double.IsFinite(value.AsDouble()))
                return new TextNode($"{label}: {value} (non-finite, read-only) · {node.Path}") { Monospace = true };
            return document.Field(node, raw) with { Label = label };
        }
        return new TextNode($"{label}: {document.Preview(node)} · {node.Size} bytes · {node.Path} (read-only) · {HashNames.Provenance(node.Hash)}") { Monospace = node.Type == FieldTags.Struct };
    }

}
