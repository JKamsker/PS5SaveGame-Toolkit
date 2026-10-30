using System.Globalization;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.BaldursGate3;

/// <summary>Paged collections keep every expansion bounded, even for a single enormous node.</summary>
internal sealed class ResourceExplorer(IReadOnlyList<DocumentEntry> entries, Func<string, string, ResourceAttribute, FieldNode> bind)
{
    internal const int PageSize = 128;
    internal const int MaxInlineBytes = 4096;

    internal IReadOnlyList<ViewNode> EntryList(LspkPackage? package) => entries.Count <= PageSize
        ? [EntryTable(0, entries.Count, package)] : Pages("Entries", "entries", entries.Count);

    internal IReadOnlyList<ViewNode> ResourceList() => Pages("Resources and payloads", "resources", entries.Count);

    internal IReadOnlyList<ViewNode> Expand(string id, LspkPackage? package)
    {
        var parts = id.Split('/');
        var paged = parts is ["entries" or "resources", _, _] or ["roots", _, _, _] or
            ["children" or "attributes", _, _, _, _];
        if (paged && int.TryParse(parts[^1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var pageCount) && pageCount > PageSize)
        {
            var pageStart = Index(parts[^2]);
            if (pageStart < 0 || pageCount > BinaryData.MaxRecords || pageStart > BinaryData.MaxRecords - pageCount)
            {
                throw new KeyNotFoundException("Invalid BG3 page range.");
            }

            return Pages("Items", string.Join('/', parts[..^2]), pageCount, pageStart);
        }

        switch (parts)
        {
            case ["entries", var start, var count]:
                return [EntryTable(Index(start), Index(count), package)];
            case ["resources", var start, var count]:
                return Range(Index(start), Index(count), entries.Count).Select(i =>
                    (ViewNode)SectionNode.Lazy(entries[i].Name, $"entry/{i}", entries[i].Resource?.Format ?? "Raw payload")).ToArray();
            case ["entry", var index]:
                return Entry(Index(index));
            case ["roots", var entry, var start, var count]:
                return Nodes(Index(entry), Resource(Index(entry)).Roots, Index(start), Index(count));
            case ["node", var entry, var node]:
                return Node(Index(entry), Index(node));
            case ["children", var entry, var node, var start, var count]:
                return Nodes(Index(entry), Resource(Index(entry)).Nodes[Index(node)].Children, Index(start), Index(count));
            case ["attributes", var entry, var node, var start, var count]:
                return Attributes(Index(entry), Index(node), Index(start), Index(count));
            default:
                throw new KeyNotFoundException($"Unknown BG3 section '{id}'.");
        }
    }

    internal ReadOnlyMemory<byte> Blob(string id) => id.Split('/') switch
    {
        ["payload", var entry] => entries[Index(entry)].Bytes,
        ["attribute", var entry, var attribute] => Resource(Index(entry)).Attributes[Index(attribute)].Data,
        _ => throw new KeyNotFoundException($"Unknown BG3 blob '{id}'."),
    };

    internal bool TryEntryAsset(string id, out DocumentEntry entry)
    {
        entry = null!;
        if (id.Split('/') is ["image", var index] && int.TryParse(index, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i) && i >= 0 && i < entries.Count && IsWebP(entries[i]))
        {
            entry = entries[i];
            return true;
        }

        return false;
    }

    private IReadOnlyList<ViewNode> Entry(int index)
    {
        var entry = entries[index];
        var children = new List<ViewNode>();
        if (IsWebP(entry))
        {
            children.Add(new ImageNode($"image/{index}", entry.Name));
        }

        if (entry.Resource is { } resource)
        {
            children.Add(new StatsNode([new Stat("Nodes", resource.Nodes.Count.ToString()), new Stat("Attributes", resource.Attributes.Count.ToString())]));
            children.AddRange(Pages("Root regions", $"roots/{index}", resource.Roots.Count));
        }

        children.Add(new HexNode($"payload/{index}", entry.Bytes.Length) { Title = entry.Name });
        return children;
    }

    private IReadOnlyList<ViewNode> Node(int entryIndex, int nodeIndex)
    {
        var node = Resource(entryIndex).Nodes[nodeIndex];
        return
        [
            new TextNode($"{node.Name} · node {nodeIndex} · parent {node.Parent}"),
            .. node.KeyAttribute is { } key ? new ViewNode[] { new TextNode($"Key attribute: {key}") } : [],
            .. Pages("Attributes", $"attributes/{entryIndex}/{nodeIndex}", node.Attributes.Count),
            .. Pages("Children", $"children/{entryIndex}/{nodeIndex}", node.Children.Count),
        ];
    }

    private ViewNode[] Nodes(int entryIndex, IReadOnlyList<int> nodes, int start, int count) =>
        Range(start, count, nodes.Count).Select(i => (ViewNode)SectionNode.Lazy(
            $"{Resource(entryIndex).Nodes[nodes[i]].Name} [{nodes[i]}]", $"node/{entryIndex}/{nodes[i]}")).ToArray();

    private IReadOnlyList<ViewNode> Attributes(int entryIndex, int nodeIndex, int start, int count)
    {
        var resource = Resource(entryIndex);
        var attributes = resource.Nodes[nodeIndex].Attributes;
        return Range(start, count, attributes.Count).Select(i =>
        {
            var index = attributes[i];
            var attribute = resource.Attributes[index];
            var label = $"{attribute.Name} ({attribute.Type})";
            if (attribute.Type is LarianAttributeType.ScratchBuffer or LarianAttributeType.TranslatedFSString ||
                attribute.Data.Length > MaxInlineBytes)
            {
                return (ViewNode)new HexNode($"attribute/{entryIndex}/{index}", attribute.Data.Length) { Title = label };
            }

            if (attribute.XmlValue is { Length: > MaxInlineBytes } xml)
            {
                return new TextNode($"{label}: {xml[..MaxInlineBytes]}… ({xml.Length:N0} characters; full text in raw payload)");
            }

            return bind($"resource/{entryIndex}/attribute/{index}", label, attribute);
        }).ToArray();
    }

    private TableNode EntryTable(int start, int count, LspkPackage? package) => new(
        [new("name", "Name"), new("stored", "Stored bytes"), new("size", "Expanded bytes"), new("compression", "Compression")],
        Range(start, count, entries.Count).Select(i => new TableRow(i.ToString(CultureInfo.InvariantCulture),
            new Dictionary<string, ViewNode>
            {
                ["name"] = new TextNode(entries[i].Name),
                ["stored"] = new TextNode((package?.Entries[i].StoredSize ?? entries[i].Bytes.Length).ToString("N0")),
                ["size"] = new TextNode(entries[i].Bytes.Length.ToString("N0")),
                ["compression"] = new TextNode(package is null ? "Standalone resource" : $"{package.Entries[i].Compression} (0x{package.Entries[i].Flags:X2})"),
            })).ToArray()) { Title = "Package entries" };

    private LarianResource Resource(int index) => entries[index].Resource ?? throw new KeyNotFoundException("Entry has no resource tree.");

    internal static IReadOnlyList<ViewNode> Pages(string title, string prefix, int total, int offset = 0)
    {
        var pages = new List<ViewNode>();
        // Hierarchical ranges keep even the list of page links bounded.
        var pageSize = PageSize;
        while ((total + (long)pageSize - 1) / pageSize > PageSize)
        {
            pageSize = checked(pageSize * PageSize);
        }

        for (var start = 0; start < total; start += pageSize)
        {
            var count = Math.Min(pageSize, total - start);
            pages.Add(SectionNode.Lazy($"{title} {offset + start + 1}–{offset + start + count}", $"{prefix}/{offset + start}/{count}", count.ToString()));
        }

        return pages;
    }

    internal static IEnumerable<int> Range(int start, int count, int total)
    {
        if (start < 0 || count < 0 || count > PageSize || start > total || count > total - start)
        {
            throw new KeyNotFoundException("Invalid BG3 explorer range.");
        }

        return Enumerable.Range(start, count);
    }

    private static int Index(string text) => int.Parse(text, NumberStyles.None, CultureInfo.InvariantCulture);

    private static bool IsWebP(DocumentEntry entry) => entry.Bytes.Length >= 12 &&
        entry.Bytes.AsSpan(0, 4).SequenceEqual("RIFF"u8) && entry.Bytes.AsSpan(8, 4).SequenceEqual("WEBP"u8);
}
