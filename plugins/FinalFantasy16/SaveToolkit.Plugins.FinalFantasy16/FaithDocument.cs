using System.Buffers.Binary;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Saves;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.FinalFantasy16;

internal sealed class FaithDocument : BoundSaveDocument
{
    private const string CrcId = "integrity/crc32";
    private const int QuantityOffset = 4;
    private const int ChildLevelOffset = 8;
    private const int ChildXpOffset = 12;
    private readonly ISaveFile _file;
    private readonly byte[] _bytes;
    private readonly FaithContainer _container;
    private readonly FaithSchema _schema;
    private readonly FaithInventory _inventory;
    private readonly FaithExplorer _explorer;

    public FaithDocument(ISaveFile file, byte[] bytes, FaithContainer container, FaithSchema schema)
    {
        _file = file;
        _bytes = bytes;
        _container = container;
        _schema = schema;
        _inventory = FaithInventory.Parse(bytes, container);
        _explorer = new FaithExplorer(bytes, container);
    }

    public override string Title => $"Final Fantasy XVI{(_schema.IsDemo ? " Demo" : "")} · {_file.FileName}";
    public override bool CanEdit => _inventory.Items.Any(item => ReadInt(item.Offset + QuantityOffset) >= 0);

    public override ViewNode BuildView() => new TabsNode([
        new Tab("Overview", Overview()),
        new Tab("Inventory", [
            new TextNode("Edit existing stacks only. Item IDs, allocation data, equipment and progression flags are preserved. " +
                "The secondary inventory's role is unconfirmed and is available only in the raw explorer.") { Tone = Tone.Warning },
            _inventory.IsSupported
                ? InventoryTable()
                : new TextNode("No supported ItemData allocator was found; use the read-only structural explorer.") { Tone = Tone.Warning },
        ]),
        new Tab("Structure", _explorer.BuildView()),
        new Tab("Hex", [new HexNode(FaithExplorer.FileBlobId, _bytes.Length) { Title = "Original layout with current edits" }]),
    ]);

    public override IReadOnlyList<ViewNode> ExpandNode(string lazyId) => _explorer.Expand(lazyId);

    public override ReadOnlyMemory<byte> ReadBlob(string blobId, long offset, int count) =>
        _explorer.ReadBlob(blobId, offset, count);

    public override EditOutcome ApplyEdits(IReadOnlyList<ValueEdit> edits)
    {
        var oldCrc = ReadCrc();
        var result = base.ApplyEdits(edits);
        if (!result.Succeeded || result.Changes.Count == 0)
        {
            return result;
        }

        var newCrc = FaithCrc32.Compute(_bytes.AsSpan(FaithContainer.HeaderSize));
        BinaryPrimitives.WriteUInt32LittleEndian(_bytes.AsSpan(FaithContainer.CrcOffset), newCrc);
        return result with
        {
            Changes = oldCrc == newCrc ? result.Changes :
                [.. result.Changes, new ValueChange(CrcId, FieldValue.Of((ulong)oldCrc), FieldValue.Of((ulong)newCrc))],
        };
    }

    public override IReadOnlyList<FileOutput> Serialize()
    {
        var output = _bytes.ToArray();
        BinaryPrimitives.WriteUInt32LittleEndian(output.AsSpan(FaithContainer.CrcOffset),
            FaithCrc32.Compute(output.AsSpan(FaithContainer.HeaderSize)));
        var reparsed = FaithContainer.Parse(output);
        _ = FaithInventory.Parse(output, reparsed);
        return [new FileOutput(_file.RelativePath, output)];
    }

    private IReadOnlyList<ViewNode> Overview()
    {
        var player = UniqueBlock(_schema.SupportsPlayer);
        var user = UniqueBlock(FaithSchema.SupportsUser);
        var stats = new List<Stat>
        {
            new("Variant", _schema.Name),
            new("Role", _container.IsSystem ? "System/settings" : "Gameplay"),
            new("Blocks", _container.Blocks.Count.ToString()),
            new("Bytes", _bytes.Length.ToString()),
        };
        if (player is not null)
        {
            stats.Add(new Stat("Level", ReadInt(player.PayloadOffset).ToString()));
        }

        if (user is not null)
        {
            stats.Add(new Stat("Play time", $"{ReadInt(user.PayloadOffset)} s"));
        }

        var nodes = new List<ViewNode>
        {
            new StatsNode(stats),
            new TextNode("Experimental: inventory writes pass format round trips; console loading has not been tested.") { Tone = Tone.Warning },
            Field(CrcId, "CRC32 (payload)", IntegerSpec.UInt32 with { Format = IntegerFormat.Hex },
                () => FieldValue.Of((ulong)ReadCrc())) with { Confidence = Confidence.Verified },
        };
        if (player is not null)
        {
            nodes.Add(new SectionNode("Character", [
                ReadOnlyInt("player/level", "Level", player.PayloadOffset),
                ReadOnlyInt("player/xp", "Experience", player.PayloadOffset + sizeof(int)),
                ReadOnlyInt("player/childLevel", "Child level", player.PayloadOffset + ChildLevelOffset),
                ReadOnlyInt("player/childXp", "Child experience", player.PayloadOffset + ChildXpOffset),
                new TextNode("Level and experience are read-only because growth/stat dependencies have not been mapped.") { Tone = Tone.Muted },
            ]));
        }

        if (user is not null)
        {
            nodes.Add(ReadOnlyInt("user/playTime", "Play time", user.PayloadOffset,
                IntegerSpec.Int32 with { Format = IntegerFormat.Duration, UnitsPerSecond = 1, Unit = "s" }));
        }

        var currency = _inventory.Items.Where(item => item.HasKnownName).Select(item => (ViewNode)Quantity(item)).ToArray();
        if (currency.Length > 0)
        {
            nodes.Add(new SectionNode("Resources", currency));
        }

        return nodes;
    }

    private TableNode InventoryTable() => new([
        new TableColumn("slot", "Storage slot"), new TableColumn("id", "Item ID"),
        new TableColumn("name", "Name"), new TableColumn("quantity", "Stack"),
    ], _inventory.Items.Select(item => new TableRow(item.Slot.ToString(), new Dictionary<string, ViewNode>
    {
        ["slot"] = new TextNode(item.Slot.ToString()),
        ["id"] = new TextNode(item.Id.ToString()),
        ["name"] = new TextNode(item.Name),
        ["quantity"] = Quantity(item),
    })).ToArray()) { Title = "Main inventory (allocated records, including zero stacks)" };

    private FieldNode Quantity(FaithItem item)
    {
        var offset = item.Offset + QuantityOffset;
        var writable = ReadInt(offset) >= 0;
        return Field(item.QuantityId, item.HasKnownName ? item.Name : $"Item {item.Id} stack",
            writable ? new IntegerSpec(0, int.MaxValue) : IntegerSpec.Int32,
            () => FieldValue.Of(ReadInt(offset)),
            writable ? value => BinaryPrimitives.WriteInt32LittleEndian(_bytes.AsSpan(offset), value.AsInt32()) : null,
            value => value.Kind != FieldValueKind.Integer || value.AsInteger() < 0 || value.AsInteger() > int.MaxValue
                ? "Stack must be a non-negative signed 32-bit integer." : null) with
        {
            Confidence = Confidence.Verified,
            Description = $"ItemData slot {item.Slot}, stack at file offset 0x{offset:X}. " +
                (item.HasKnownName ? "Name from Nenkai's item ID research." : "Stack layout is known; item name is unmapped."),
            Warning = "Game-specific stack limits and progression effects are unknown. Large values may be clamped or rejected by the game.",
        };
    }

    private FieldNode ReadOnlyInt(string id, string label, int offset, IntegerSpec? spec = null) =>
        Field(id, label, spec ?? IntegerSpec.Int32, () => FieldValue.Of(ReadInt(offset))) with { Confidence = Confidence.Verified };

    private FaithBlock? UniqueBlock(Func<FaithBlock, bool> predicate)
    {
        var candidates = _container.Blocks.Where(predicate).Take(2).ToArray();
        return candidates.Length == 1 ? candidates[0] : null;
    }

    private int ReadInt(int offset) => BinaryPrimitives.ReadInt32LittleEndian(_bytes.AsSpan(offset));
    private uint ReadCrc() => BinaryPrimitives.ReadUInt32LittleEndian(_bytes.AsSpan(FaithContainer.CrcOffset));
}
