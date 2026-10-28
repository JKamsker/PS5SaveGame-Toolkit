using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.Witcher3;

internal sealed class WitcherDocument(string path, WitcherContainer container, Sav3Directory directory, Sav3Tree tree) : BoundSaveDocument
{
    private const int PageSize = 128;
    public override string Title => "The Witcher 3 · SAV3";
    public override bool CanEdit => false;

    public override ViewNode BuildView() => new TabsNode([
        new Tab("Overview", [
            new StatsNode([new Stat("LZ4 chunks", container.Chunks.Count.ToString()),
                new Stat("Expanded bytes", container.Payload.Length.ToString()),
                new Stat("Indexed variables", directory.Entries.Count.ToString()),
                new Stat("Names", directory.Names.Count.ToString()), new Stat("Explorer nodes", tree.Nodes.Count.ToString())]),
            new TextNode("Read-only: player level, XP, crowns, skill points and integrity dependencies are not yet verified for PS5.") { Tone = Tone.Warning },
            new TextNode("Variable names and encoded types come from the save. Their gameplay effects and ownership may be unknown."),
            Field("header/0", "SAV3 version word 1", IntegerSpec.Int32, () => FieldValue.Of((long)BinaryData.Int32(container.Payload, 4))) with { Confidence = Confidence.Unknown },
            Field("header/1", "SAV3 version word 2", IntegerSpec.Int32, () => FieldValue.Of((long)BinaryData.Int32(container.Payload, 8))) with { Confidence = Confidence.Unknown },
            Field("header/2", "SAV3 version word 3", IntegerSpec.Int32, () => FieldValue.Of((long)BinaryData.Int32(container.Payload, 12))) with { Confidence = Confidence.Unknown },
            .. SummaryFields()]),
        new Tab("Variable tree", ChildPage(-1, 0)),
        new Tab("Names", [SectionNode.Lazy("MANU name dictionary", "names/0", directory.Names.Count.ToString())]),
        new Tab("Container", [SectionNode.Lazy("LZ4 chunk table", "chunks"),
            new HexNode("original", container.Original.Length) { Title = "Original SNFH container" },
            new HexNode("payload", container.Payload.Length) { Title = "Expanded SAV3 payload" },
            new TextNode($"Internal offsets include the {container.HeaderSize}-byte container header; explorer payload offsets start at SAV3.")])]);

    public override IReadOnlyList<ViewNode> ExpandNode(string lazyId)
    {
        if (lazyId == "chunks")
        {
            return [ChunkTable()];
        }

        var parts = lazyId.Split('/');
        if (parts.Length == 2 && parts[0] == "names" && int.TryParse(parts[1], out var start) && start >= 0 && start <= directory.Names.Count)
        {
            return [NamesTable(start), .. NextPage("Names", "names", start, directory.Names.Count)];
        }

        if (parts.Length == 2 && parts[0] == "node" && int.TryParse(parts[1], out var id) && id >= 0 && id < tree.Nodes.Count)
        {
            var node = tree.Nodes[id];
            var scalar = ScalarField(node);
            return [new StatsNode([new Stat("Token", node.Tag), new Stat("Payload offset", $"0x{node.Offset:X}"),
                new Stat("Virtual offset", $"0x{node.Offset + container.HeaderSize:X}"), new Stat("Span bytes", node.Size.ToString())]),
                new TextNode(node.Type is null ? node.Name : $"{node.Name} : {node.Type}"),
                .. (scalar is null ? Array.Empty<ViewNode>() : [scalar]),
                new HexNode($"node/{id}", node.Size) { Title = "Original token bytes" },
                .. ChildPage(id, 0)];
        }

        if (parts.Length == 3 && parts[0] == "children" && int.TryParse(parts[1], out var parent) && parent >= -1 && parent < tree.Nodes.Count &&
            int.TryParse(parts[2], out var page) && page >= 0)
        {
            return ChildPage(parent, page);
        }

        throw new KeyNotFoundException($"Unknown lazy node '{lazyId}'.");
    }

    public override IReadOnlyList<FileOutput> Serialize() => [new FileOutput(path, container.Original.ToArray())];

    public override ReadOnlyMemory<byte> ReadBlob(string blobId, long offset, int count)
    {
        ReadOnlyMemory<byte> data = blobId switch
        {
            "original" => container.Original,
            "payload" => container.Payload,
            _ => NodeBlob(blobId),
        };
        if (offset < 0 || offset > data.Length || count < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(offset));
        }

        return data.Slice((int)offset, Math.Min(count, data.Length - (int)offset)).ToArray();
    }

    private ReadOnlyMemory<byte> NodeBlob(string id)
    {
        if (id.StartsWith("node/", StringComparison.Ordinal) && int.TryParse(id.AsSpan(5), out var index) && index >= 0 && index < tree.Nodes.Count)
        {
            var node = tree.Nodes[index];
            return container.Payload.AsMemory(node.Offset, node.Size);
        }

        throw new KeyNotFoundException($"Unknown blob '{id}'.");
    }

    private IEnumerable<ViewNode> SummaryFields()
    {
        // These are exact stored names, not a claim to have decoded player progression.
        string[] selectedNames = ["path", "description", "playthroughId"];
        return tree.Nodes.Where(n => n.Offset < 128 && n.Tag == "VL" && selectedNames.Contains(n.Name, StringComparer.Ordinal))
            .Select(ScalarField).OfType<FieldNode>();
    }

    private FieldNode? ScalarField(Sav3Node node)
    {
        var scalar = ScalarReader.Read(node, container.Payload, directory.Names);
        return scalar is null ? null : Field($"variable/{node.Offset}", node.Name, scalar.Spec, () => scalar.Value) with
        {
            Confidence = Confidence.Inferred,
            Description = $"Stored {node.Tag} name and {node.Type} type; gameplay meaning and owner are unconfirmed.",
        };
    }

    private IReadOnlyList<ViewNode> ChildPage(int parent, int start)
    {
        var children = parent < 0 ? tree.Roots : tree.Nodes[parent].Children;
        if (start > children.Count)
        {
            throw new KeyNotFoundException("Variable page exceeds the child list.");
        }

        return [.. children.Skip(start).Take(PageSize).Select(id => (ViewNode)SectionNode.Lazy(
            $"{tree.Nodes[id].Name} [{tree.Nodes[id].Tag}]", $"node/{id}",
            tree.Nodes[id].Children.Count > 0 ? $"{tree.Nodes[id].Children.Count} children" : tree.Nodes[id].Type ?? $"{tree.Nodes[id].Size} bytes")),
            .. NextPage("Variables", $"children/{parent}", start, children.Count)];
    }

    private TableNode NamesTable(int start) => new([new("index", "Name index"), new("name", "Stored name")],
        directory.Names.Skip(start).Take(PageSize).Select((name, index) => new TableRow((start + index + 1).ToString(), new Dictionary<string, ViewNode>
        {
            ["index"] = new TextNode((start + index + 1).ToString()), ["name"] = new TextNode(name),
        })).ToArray());

    private TableNode ChunkTable() => new([new("index", "Chunk"), new("offset", "File offset"), new("stored", "Stored bytes"), new("expanded", "Expanded bytes")],
        container.Chunks.Select((chunk, index) => new TableRow(index.ToString(), new Dictionary<string, ViewNode>
        {
            ["index"] = new TextNode(index.ToString()), ["offset"] = new TextNode($"0x{chunk.Offset:X}"),
            ["stored"] = new TextNode(chunk.StoredSize.ToString()), ["expanded"] = new TextNode(chunk.ExpandedSize.ToString()),
        })).ToArray());

    private static IEnumerable<ViewNode> NextPage(string title, string prefix, int start, int count) =>
        start + PageSize < count ? [SectionNode.Lazy($"{title}: next {PageSize}", $"{prefix}/{start + PageSize}")] : [];
}
