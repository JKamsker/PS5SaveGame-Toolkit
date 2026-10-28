using System.Globalization;
using SaveToolkit.Plugins.Pragmata.Format;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.Pragmata.Views;

/// <summary>Bounded navigation and existing-value edits by hash-confirmed section families.</summary>
internal sealed class EvidenceView(PragmataDocument document, LazyPages pages)
{
    public IReadOnlyList<ViewNode> Gadgets() => [Notice(), .. document.Layout.Inventory!.Children
        .Where(n => HashNames.All.GetValueOrDefault(n.Hash) is "_Gadgets" or "_ShortcutSlot")
        .Select(Node)];

    public IReadOnlyList<ViewNode> Family(params string[] classNames)
    {
        var sections = document.Layout.Sections
            .Where(n => classNames.Contains(HashNames.All.GetValueOrDefault(n.ClassHash), StringComparer.Ordinal)).ToArray();
        var counts = sections.SelectMany(section => section.Children.Where(n => n.Type == FieldTags.Array).Select(array =>
            new Stat($"{document.References.Section(section.ClassHash)?.Name ?? HashNames.Label(section.ClassHash)} / {HashNames.Label(array.Hash)}",
                array.Children.Count.ToString(CultureInfo.InvariantCulture))
            {
                Hint = $"Stored records only; completion and quest names Unknown. " +
                    $"{document.References.Section(section.ClassHash)?.AttributionText ?? HashNames.Provenance(section.ClassHash)} " +
                    document.References.IdentifierSource(array.Hash),
            })).ToArray();
        return [Notice(), new StatsNode(counts), .. sections.Select(Section)];
    }

    private static TextNode Notice() => new("Existing primitive values are editable; strings, structs and record counts remain read-only. Names identify stored types; gameplay meanings, dependencies and legal values are unconfirmed. Editor bounds use matching typed fields in this save, including 0 and 1. Counts are stored records, not game completion.") { Tone = Tone.Warning };

    private ViewNode Section(SaveNode node)
    {
        var record = document.References.Section(node.ClassHash);
        return Container(node, record?.Name ?? HashNames.Label(node.ClassHash)) with
        {
            Description = $"{record?.Description ?? "Grouping inferred from class identifier."} {record?.AttributionText ?? HashNames.Provenance(node.ClassHash)} Raw class: {node.ClassHash:x8}.",
        };
    }

    private ViewNode Node(SaveNode node)
    {
        if (node.Type == FieldTags.Array)
            return document.Evidence.Table(node, child => child.Type == FieldTags.Class
                ? Container(child, HashNames.Label(child.ClassHash)) : Node(child));
        if (node.Type == FieldTags.Class)
            return Container(node, $"{HashNames.Label(node.Hash)} · {HashNames.Label(node.ClassHash)}");
        if (!node.IsPrimitive)
            return new TextNode($"{HashNames.Label(node.Hash)}: {document.Preview(node)} · {node.Size} bytes · {HashNames.Provenance(node.Hash)}") { Monospace = true };
        if (node.Type is FieldTags.Float32 or FieldTags.Float64 && !double.IsFinite(document.Read(node).AsDouble()))
            return new TextNode($"{HashNames.Label(node.Hash)}: {document.Read(node)} (non-finite) · {HashNames.Provenance(node.Hash)}");
        return node.Hash == KnownHashes.ItemId && node.Type == FieldTags.UInt32 && node.Parent?.ClassHash == document.Layout.Items.FirstOrDefault()?.ClassHash
            ? document.References.ItemField(node) : document.References.Primitive(node);
    }

    private SectionNode Container(SaveNode node, string title) => pages.Section($"evidence/{node.Path}", title,
        node.Children.Count, (start, count) => node.Children.Skip(start).Take(count).Select(Node).ToArray()) with
    {
        Description = $"{HashNames.Provenance(node.Hash)} Class: {HashNames.Provenance(node.ClassHash)}",
    };

}
