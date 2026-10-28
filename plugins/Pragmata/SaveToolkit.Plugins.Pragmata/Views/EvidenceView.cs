using System.Globalization;
using SaveToolkit.Plugins.Pragmata.Format;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.Pragmata.Views;

/// <summary>Bounded, read-only navigation by hash-confirmed section families.</summary>
internal sealed class EvidenceView(PragmataDocument document, LazyPages pages)
{
    public IReadOnlyList<ViewNode> Gadgets() => [Notice(), .. document.Layout.Inventory!.Children
        .Where(n => HashNames.All.GetValueOrDefault(n.Hash) is "_Gadgets" or "_ShortcutSlot")
        .Select(Node)];

    public IReadOnlyList<ViewNode> Family(params string[] classNames) => [Notice(), .. document.Layout.Sections
        .Where(n => classNames.Contains(HashNames.All.GetValueOrDefault(n.ClassHash), StringComparer.Ordinal)).Select(Section)];

    private static TextNode Notice() => new("Read-only evidence view. Names identify stored types; gameplay meanings, completion totals and legal values are unconfirmed. Counts are stored records, not game completion.") { Tone = Tone.Info };

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
            return pages.Section($"evidence/{node.Path}", HashNames.Label(node.Hash), node.Children.Count,
                (start, count) => [Table(node, start, count)]) with { Description = HashNames.Provenance(node.Hash) };
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

    private TableNode Table(SaveNode array, int start, int count) => new(
        [new("record", "Stored record"), new("details", "Read-only fields / details")],
        array.Children.Skip(start).Take(count).Select(node => new TableRow(node.Path, new Dictionary<string, ViewNode>
        {
            ["record"] = new TextNode(node.Index.ToString(CultureInfo.InvariantCulture)),
            ["details"] = node.Type == FieldTags.Class
                ? Container(node, HashNames.Label(node.ClassHash)) : Node(node),
        })).ToArray())
    {
        Title = HashNames.Label(array.Hash), Description = $"{HashNames.Provenance(array.Hash)} Positional records have no proven gameplay identity.",
    };
}
