using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;
using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Collections;

namespace SaveToolkit.Plugins.Witcher3;

internal sealed class WitcherDocument(string path, WitcherContainer container, Sav3Directory directory, Sav3Tree tree,
    IReferenceContext references) : BoundSaveDocument, IQueryableDocument
{
    private const int PageSize = 128;
    private readonly PlayerData? _player = PlayerData.Read(container, directory, tree);
    private InventoryViews? _inventory;
    private ObservedFeatures? _features;
    private InventoryViews Inventory => _inventory ??= new InventoryViews(_player?.Inventory?.Items ?? [],
        references.Catalog(InventoryViews.CatalogId, ReferenceBuild), ItemField, references.AssetProvenance);
    private string ReferenceBuild => $"sav3-{BinaryData.Int32(container.Payload, 4)}.{BinaryData.Int32(container.Payload, 8)}.{BinaryData.Int32(container.Payload, 12)}";
    private ObservedFeatures Features => _features ??= new ObservedFeatures(_player, tree, container.Payload, directory.Names);
    public override string Title => "The Witcher 3 · SAV3";
    public override bool CanEdit => _player is not null;
    public IReadOnlyList<QueryableCollection> Collections => [Inventory.Collection];
    public QueryableCollectionPage Query(string collectionId, QueryableCollectionQuery query) => collectionId == InventoryViews.CollectionId
        ? Inventory.Query(query) : throw new KeyNotFoundException($"Unknown collection '{collectionId}'.");
    public override AssetContent? GetAsset(string assetId) => references.GetAsset(assetId);
    public override ActionOutcome InvokeAction(string actionId, IReadOnlyDictionary<string, FieldValue> parameters) => actionId == InventoryViews.LookupActionId
        ? Inventory.Inspect(parameters) : base.InvokeAction(actionId, parameters);

    public override ViewNode BuildView()
    {
        // Preserve direct scalar bindings for the existing small-inventory editing API. Large lists bind on query.
        if (_player?.Inventory is { Items.Count: <= PageSize } small)
        {
            foreach (var item in small.Items) { _ = ItemField(item); }
        }

        return new TabsNode([
        new Tab("Overview", [
            new StatsNode([new Stat("LZ4 chunks", container.Chunks.Count.ToString()),
                new Stat("Expanded bytes", container.Payload.Length.ToString()),
                new Stat("Indexed variables", directory.Entries.Count.ToString()),
                new Stat("Names", directory.Names.Count.ToString()), new Stat("Explorer nodes", tree.Nodes.Count.ToString())]),
            new TextNode(CanEdit ? "Experimental editing: output is validated by re-opening; console loading has not been tested." :
                "Read-only: this save does not match the supported PS5 player schema.") { Tone = Tone.Warning },
            new TextNode("Variable names and encoded types come from the save. Their gameplay effects and ownership may be unknown."),
            Field("header/0", "SAV3 version word 1", IntegerSpec.Int32, () => FieldValue.Of((long)BinaryData.Int32(container.Payload, 4))) with { Confidence = Confidence.Unknown },
            Field("header/1", "SAV3 version word 2", IntegerSpec.Int32, () => FieldValue.Of((long)BinaryData.Int32(container.Payload, 8))) with { Confidence = Confidence.Unknown },
            Field("header/2", "SAV3 version word 3", IntegerSpec.Int32, () => FieldValue.Of((long)BinaryData.Int32(container.Payload, 12))) with { Confidence = Confidence.Unknown },
            .. SummaryFields()]),
        new Tab("Player", PlayerFields()),
        new Tab("Inventory", Inventory.Build()),
        new Tab("Abilities and Gwent", Features.Build()),
        new Tab("Variable tree", ChildPage(-1, 0)),
        new Tab("Names", [SectionNode.Lazy("MANU name dictionary", "names/0", directory.Names.Count.ToString())]),
        new Tab("Container", [SectionNode.Lazy("LZ4 chunk table", "chunks"),
            new HexNode("original", container.Original.Length) { Title = "Original SNFH container" },
            new HexNode("payload", container.Payload.Length) { Title = "Expanded SAV3 payload" },
            new TextNode($"Internal offsets include the {container.HeaderSize}-byte container header; explorer payload offsets start at SAV3.")])]);
    }

    public override IReadOnlyList<ViewNode> ExpandNode(string lazyId)
    {
        if (lazyId.StartsWith("ability/", StringComparison.Ordinal) || lazyId.StartsWith("gwent/", StringComparison.Ordinal))
        {
            return Features.Expand(lazyId);
        }

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
                new HexNode($"node/{id}", node.Size) { Title = "Input token bytes" },
                .. ChildPage(id, 0)];
        }

        if (parts.Length == 3 && parts[0] == "children" && int.TryParse(parts[1], out var parent) && parent >= -1 && parent < tree.Nodes.Count &&
            int.TryParse(parts[2], out var page) && page >= 0)
        {
            return ChildPage(parent, page);
        }

        throw new KeyNotFoundException($"Unknown lazy node '{lazyId}'.");
    }

    public override IReadOnlyList<FileOutput> Serialize()
    {
        var patches = _player?.Patches(directory, container).ToArray() ?? [];
        if (patches.Length == 0)
        {
            return [new FileOutput(path, container.Original.ToArray())];
        }

        var payload = Sav3Writer.Write(container, directory, tree, patches);
        var bytes = WitcherWriter.Write(container, payload);
        var check = WitcherContainer.Parse(bytes);
        var checkDirectory = Sav3Directory.Parse(check);
        var checkTree = Sav3Tree.Parse(check.Payload, checkDirectory);
        _player!.Verify(PlayerData.Read(check, checkDirectory, checkTree));
        return [new FileOutput(path, bytes)];
    }

    private IReadOnlyList<ViewNode> PlayerFields()
    {
        if (_player is null)
        {
            return [new TextNode("Player identity and level manager could not be established for this schema.")];
        }

        var progress = _player.Progress;
        var crowns = _player.Inventory?.Items.Where(i => i.Name == "Crowns").ToArray() ?? [];
        return [new StatsNode([new Stat("Level", progress.Level.ToString()),
                new Stat("Total XP", ((long)progress.Experience + progress.UsedExperience).ToString()),
                new Stat("Available ability points", progress.AbilityPoints.ToString())]),
            Field("player/level", "Character level", new IntegerSpec(1, progress.MaximumDefinedLevel),
                () => FieldValue.Of(progress.Level), v => progress.Level = v.AsInt32()) with {
                Confidence = Confidence.Inferred, Description = "Player IdTag → Entity → levelManager → level.",
                Warning = "Changes the stored level only. XP, level rewards, skill slots and scaled stats are not recalculated." },
            Field("player/xp", "Unspent XP", new IntegerSpec(0, int.MaxValue - progress.UsedExperience),
                () => FieldValue.Of(progress.Experience), v => progress.Experience = v.AsInt32()) with {
                Confidence = Confidence.Inferred, Description = "levelManager.points[1].free; total XP includes used XP.",
                Warning = "Does not run the game's level-up logic. Keep XP consistent with the character level." },
            Field("player/abilityPoints", "Available ability points", new IntegerSpec(0, int.MaxValue - progress.UsedAbilityPoints),
                () => FieldValue.Of(progress.AbilityPoints), v => progress.AbilityPoints = v.AsInt32()) with {
                Confidence = Confidence.Inferred, Description = "levelManager.points[0].free; omitted defaults are decoded as zero." },
            Field("player/usedXp", "Used XP", IntegerSpec.Int32, () => FieldValue.Of(progress.UsedExperience)) with { Confidence = Confidence.Inferred },
            Field("player/usedAbilityPoints", "Spent ability points", IntegerSpec.Int32, () => FieldValue.Of(progress.UsedAbilityPoints)) with { Confidence = Confidence.Inferred },
            .. (crowns.Length == 1 ? new ViewNode[] { ItemField(crowns[0]) } : [new TextNode("No unique Crowns stack was decoded.")]),
            new TextNode("Crowns are money. Orens and Florens are separate inventory items if present. Values are inferred from stored names and published layouts."),
            new TextNode("Raw views show the input image; pending edits appear in Player and Inventory.")];
    }

    private FieldNode ItemField(InventoryItem item) => Field(item.ValueId, item.Name, new IntegerSpec(1, ushort.MaxValue),
        () => FieldValue.Of(item.Quantity), v => item.Quantity = checked((ushort)v.AsInteger()),
        v => v.AsInteger() < 1 || v.AsInteger() > ushort.MaxValue ? "Stored quantity must be in 1..65,535." : null) with {
        Confidence = Confidence.Inferred,
        Description = $"Native CInventoryComponent record {item.Id}; stored item name, uint16 quantity.",
        Warning = "Stack limits and quest/equipment dependencies are unknown. Changing quantities may affect gameplay." };

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
