using System.Buffers.Binary;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Saves;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;
using SaveToolkit.Sdk.References;

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
    private readonly FaithCatalog _catalog;
    private readonly FaithReadOnlyContext _context;
    private readonly IReferenceContext _references;

    public FaithDocument(ISaveFile file, byte[] bytes, FaithContainer container, FaithSchema schema, IReferenceContext references)
    {
        _file = file;
        _bytes = bytes;
        _container = container;
        _schema = schema;
        _inventory = FaithInventory.Parse(bytes, container);
        _explorer = new FaithExplorer(bytes, container);
        _references = references;
        _catalog = new FaithCatalog(references);
        _context = new FaithReadOnlyContext(bytes, container, schema, references);
    }

    public override string Title => $"Final Fantasy XVI{(_schema.IsDemo ? " Demo" : "")} · {_file.FileName}";
    public override bool CanEdit => _inventory.Items.Any(item => ReadInt(item.Offset + QuantityOffset) >= 0);

    public override ViewNode BuildView() => new TabsNode([
        new Tab("Overview", Overview()),
        new Tab("Inventory", [.. new FaithInventoryView(_bytes, _inventory, _catalog).Build(Quantity, ItemIdentity),
            new FaithCapacityContext(_bytes, _inventory, _catalog).Build(BindReadOnly)]),
        new Tab("Character context", _context.Build(BindReadOnly, ItemIdentity)),
        new Tab("Structure", _explorer.BuildView()),
        new Tab("Hex", [new HexNode(FaithExplorer.FileBlobId, _bytes.Length) { Title = "Original layout with current edits" }]),
    ]);

    public override IReadOnlyList<ViewNode> ExpandNode(string lazyId) => _explorer.Expand(lazyId);

    public override AssetContent? GetAsset(string assetId) => _references.GetAsset(assetId);

    public override ActionOutcome InvokeAction(string actionId, IReadOnlyDictionary<string, FieldValue> parameters)
    {
        if (actionId != FaithCatalog.LookupActionId) return base.InvokeAction(actionId, parameters);
        if (!parameters.TryGetValue(FaithCatalog.LookupParameter, out var value) || value.Kind != FieldValueKind.Text ||
            !int.TryParse(value.AsString(), System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture, out var itemId) || _catalog.Item(itemId) is null)
            throw new ArgumentException("Choose a known catalog item.", nameof(parameters));
        var count = _inventory.Items.Count(item => item.Id == itemId);
        return new ActionOutcome
        {
            Message = $"{_catalog.Name(itemId)} (ID {itemId}, {_catalog.Category(itemId)}). " +
                $"Allocated main-bank records: {count}; this does not prove current ownership. {_catalog.Provenance(itemId)}",
        };
    }

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
        var player = FaithSchema.Unique(_container, FaithSchema.PlayerType, _schema.SupportsPlayer);
        var user = FaithSchema.Unique(_container, FaithSchema.UserType, FaithSchema.SupportsUser);
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

        var currency = _inventory.Items.Where(item => item.Id is 1 or 2 or 3).Select(item => (ViewNode)Quantity(item)).ToArray();
        if (currency.Length > 0)
        {
            nodes.Add(new SectionNode("Resources", currency));
        }

        return nodes;
    }

    private FieldNode Quantity(FaithItem item)
    {
        var offset = item.Offset + QuantityOffset;
        var writable = ReadInt(offset) >= 0;
        return Field(item.QuantityId, $"{_catalog.Name(item.Id)} stack",
            writable ? new IntegerSpec(0, int.MaxValue) : IntegerSpec.Int32,
            () => FieldValue.Of(ReadInt(offset)),
            writable ? value => BinaryPrimitives.WriteInt32LittleEndian(_bytes.AsSpan(offset), value.AsInt32()) : null,
            value => value.Kind != FieldValueKind.Integer || value.AsInteger() < 0 || value.AsInteger() > int.MaxValue
                ? "Stack must be a non-negative signed 32-bit integer." : null) with
        {
            Confidence = Confidence.Verified,
            Description = $"ItemData slot {item.Slot}, stack at file offset 0x{offset:X}. " +
                _catalog.Provenance(item.Id),
            Warning = FaithStackPolicy.Warning(item.Id, _catalog.Category(item.Id)),
        };
    }

    private FieldNode ReadOnlyInt(string id, string label, int offset, IntegerSpec? spec = null) =>
        Field(id, label, spec ?? IntegerSpec.Int32, () => FieldValue.Of(ReadInt(offset))) with { Confidence = Confidence.Verified };

    private FieldNode ItemIdentity(string id, int itemId) =>
        Field(id, _catalog.Name(itemId), new ReferenceSpec(FaithCatalog.ItemsId) { AllowCustom = true },
            () => FieldValue.Of(FaithCatalog.Key(itemId))) with
        {
            Confidence = _catalog.Item(itemId) is null ? Confidence.Unknown : Confidence.Inferred,
            Description = _catalog.Provenance(itemId),
            Warning = "Identity is read-only; catalog presence does not establish ownership, slot eligibility or game-build availability.",
        };

    private FieldNode BindReadOnly(string id, string label, FieldSpec spec, Func<FieldValue> get,
        Confidence confidence, string description) => Field(id, label, spec, get) with
        { Confidence = confidence, Description = description };

    private int ReadInt(int offset) => BinaryPrimitives.ReadInt32LittleEndian(_bytes.AsSpan(offset));
    private uint ReadCrc() => BinaryPrimitives.ReadUInt32LittleEndian(_bytes.AsSpan(FaithContainer.CrcOffset));
}
