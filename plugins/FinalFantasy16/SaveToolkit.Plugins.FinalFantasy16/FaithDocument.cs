using System.Buffers.Binary;
using static SaveToolkit.Plugins.FinalFantasy16.FaithContextLayout;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Saves;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;
using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Collections;

namespace SaveToolkit.Plugins.FinalFantasy16;

internal sealed class FaithDocument : BoundSaveDocument, IActionPlanningDocument, IQueryableDocument
{
    private const string CrcId = "integrity/crc32";
    private const int QuantityOffset = 4;
    private readonly ISaveFile _file;
    private readonly byte[] _bytes;
    private readonly FaithContainer _container;
    private readonly FaithSchema _schema;
    private FaithInventory _inventory;
    private readonly FaithExplorer _explorer;
    private readonly FaithCatalog _catalog;
    private readonly FaithCharacterContext _context;
    private readonly IReferenceContext _references;
    private readonly FaithWriteMap _writes;

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
        _context = new FaithCharacterContext(bytes, container, schema, references);
        _writes = new FaithWriteMap(bytes, container, schema, _inventory);
        // Register primary storage bindings independently of paging. F6/undo can address unloaded
        // rows, and the original document can resolve their values without building eager tables.
        foreach (var item in _inventory.Items)
        {
            _ = Quantity(item);
            _ = ItemIdentity($"inventory/{item.Slot}/item", item.Id);
        }
    }

    public override string Title => $"Final Fantasy XVI{(_schema.IsDemo ? " Demo" : "")} · {_file.FileName}";
    public override bool CanEdit => !_container.IsSystem &&
        (_writes.CanEdit || _inventory.Items.Any(item => ReadInt(item.Offset + QuantityOffset) >= 0));

    public override ViewNode BuildView() => new TabsNode([
        new Tab("Overview", Overview()),
        new Tab("Inventory", [.. new FaithInventoryView(_bytes, _inventory, _catalog).Build(Quantity, ItemIdentity),
            FaithQuantityPlanning.Action(),
            new FaithCapacityContext(_bytes, _inventory, _catalog).Build(BindContextField)]),
        new Tab("Character context", _context.Build(BindContextField, ItemIdentity)),
        new Tab(FaithAbilityView.TabTitle, [new TextNode("Optional reference tree contains Eikon/DLC spoilers. " +
            "Names and AP annotations are separate from saved rank bytes; unlock/mastery state remains Unknown."),
            SectionNode.Lazy(FaithAbilityView.SectionTitle, FaithAbilityView.LazyId, $"{_catalog.Abilities().Count} reference abilities")]),
        new Tab("Structure", _explorer.BuildView()),
        new Tab("Hex", [new HexNode(FaithExplorer.FileBlobId, _bytes.Length) { Title = "Original layout with current edits" }]),
    ]);

    public override IReadOnlyList<ViewNode> ExpandNode(string lazyId) => lazyId == FaithAbilityView.LazyId
        ? new FaithAbilityView(_catalog).Build(BindContextField) : _explorer.Expand(lazyId);

    public IReadOnlyList<QueryableCollection> Collections => _inventory.IsSupported ? [InventoryView().Collection] : [];

    public QueryableCollectionPage Query(string collectionId, QueryableCollectionQuery query)
    {
        if (collectionId != FaithInventoryView.CollectionId || !_inventory.IsSupported)
            throw new KeyNotFoundException($"Unknown inventory collection '{collectionId}'.");
        var view = InventoryView();
        return new FaithInventoryQuery(_bytes, _inventory, _catalog).Query(query,
            item => view.Row(item, "inventory", Quantity, ItemIdentity), view.Collection);
    }

    private FaithInventoryView InventoryView() => new(_bytes, _inventory, _catalog);

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
        if (!CanEdit) return EditOutcome.Rejected("", "This save has no supported writable schema.");
        if (edits.Select(edit => edit.ValueId).Distinct(StringComparer.Ordinal).Count() != edits.Count)
            return EditOutcome.Rejected("", "A batch cannot edit the same field twice.");
        var before = _bytes.ToArray();
        var aliases = FaithDerivedValues.Capture(_bytes, _inventory, _writes);
        var oldCrc = ReadCrc();
        var result = base.ApplyEdits(edits);
        result = result with { Changes = result.Changes.Where(change => change.OldValue != change.NewValue).ToArray() };
        if (!result.Succeeded || result.Changes.Count == 0)
        {
            return result;
        }
        var errors = _writes.ValidateIdentityBatch(_bytes, _inventory, edits);
        if (errors.Count > 0)
        {
            before.CopyTo(_bytes, 0);
            return new EditOutcome { Errors = errors };
        }
        _inventory = FaithInventory.Parse(_bytes, _container);
        var derived = FaithDerivedValues.Capture(_bytes, _inventory, _writes)
            .Where(pair => aliases.TryGetValue(pair.Key, out var old) && old != pair.Value)
            .Select(pair => new ValueChange(pair.Key, aliases[pair.Key], pair.Value));
        result = result with { Changes = [.. result.Changes, .. derived] };
        var newCrc = FaithCrc32.Compute(_bytes.AsSpan(FaithContainer.HeaderSize));
        BinaryPrimitives.WriteUInt32LittleEndian(_bytes.AsSpan(FaithContainer.CrcOffset), newCrc);
        return result with
        {
            StructureChanged = true,
            Warnings = [FaithWriteMap.Warning],
            Changes = oldCrc == newCrc ? result.Changes :
                [.. result.Changes, new ValueChange(CrcId, FieldValue.Of((ulong)oldCrc), FieldValue.Of((ulong)newCrc))],
        };
    }

    public ActionPlan PlanAction(string actionId, IReadOnlyDictionary<string, FieldValue> parameters, ActionScope scope) =>
        new FaithQuantityPlanning(_bytes, _inventory, _catalog).Plan(actionId, parameters, scope);

    public EditOutcome ApplyActionPlan(string actionId, IReadOnlyDictionary<string, FieldValue> parameters, ActionScope scope, ActionPlan plan)
    {
        var current = PlanAction(actionId, parameters, scope);
        if (current.Errors.Count > 0) return new EditOutcome { Errors = current.Errors };
        if (!current.Changes.SequenceEqual(plan.Changes) || !current.SkippedRows.SequenceEqual(plan.SkippedRows) ||
            !current.Warnings.SequenceEqual(plan.Warnings) || plan.Errors.Count > 0)
            return EditOutcome.Rejected(actionId, "The reviewed plan no longer matches this save.");
        _ = BuildView();
        return ApplyEdits(current.Changes.Where(change => !change.Derived)
            .Select(change => new ValueEdit(change.ValueId, change.NewValue)).ToArray()) with { Warnings = current.Warnings };
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
            new TextNode(FaithWriteMap.Warning) { Tone = Tone.Warning },
            Field(CrcId, "CRC32 (payload)", IntegerSpec.UInt32 with { Format = IntegerFormat.Hex },
                () => FieldValue.Of((ulong)ReadCrc())) with { Confidence = Confidence.Verified },
        };
        if (player is not null)
        {
            nodes.Add(new SectionNode("Character", [
                ContextInt("player/level", "Level", player.PayloadOffset),
                ContextInt("player/xp", "Experience", player.PayloadOffset + sizeof(int)),
                ContextInt("player/childLevel", "Child level", player.PayloadOffset + ChildLevelOffset),
                ContextInt("player/childXp", "Child experience", player.PayloadOffset + ChildXpOffset),
                new TextNode("Level/XP are independent stored values. Growth/stat dependencies are not recalculated.") { Tone = Tone.Warning },
            ]));
        }

        if (user is not null)
        {
            nodes.Add(ContextInt("user/playTime", "Play time", user.PayloadOffset,
                IntegerSpec.Int32 with { Format = IntegerFormat.Duration, UnitsPerSecond = 1, Unit = "s" }));
        }

        var currency = _inventory.Items.Where(item => item.Id is 1 or 2 or 3).Select(item => (ViewNode)Quantity(item)).ToArray();
        if (currency.Length > 0)
        {
            nodes.Add(new SectionNode("Resources", currency));
        }

        return nodes;
    }

    private FieldNode Quantity(FaithItem item, string bank = "inventory")
    {
        var offset = item.Offset + QuantityOffset;
        if (bank == "secondary") return BindContextField($"secondary/{item.Slot}/quantity", $"{_catalog.Name(item.Id)} stored stack",
            IntegerSpec.Int32, () => FieldValue.Of(ReadInt(offset)), Confidence.Unknown,
            "Secondary bank role is unconfirmed; this may be historical/stage state. ID zero has unknown meaning and its identity stays read-only. " + _catalog.Provenance(item.Id));
        var writable = !_container.IsSystem && ReadInt(offset) >= 0;
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

    private FieldNode ContextInt(string id, string label, int offset, IntegerSpec? spec = null) =>
        BindContextField(id, label, spec ?? IntegerSpec.Int32, () => FieldValue.Of(ReadInt(offset)), Confidence.Inferred,
            "Supported PlayerData/UserData fixed-width layout; PS5 sample bytes checked. " + FaithCharacterContext.SchemaSource);

    private FieldNode ItemIdentity(string id, int itemId) =>
        BindContextField(id, _catalog.Name(itemId), new ReferenceSpec(FaithCatalog.ItemsId) { AllowCustom = true },
            () => FieldValue.Of(FaithCatalog.Key(itemId)),
            _catalog.Item(itemId) is null ? Confidence.Unknown : Confidence.Inferred, _catalog.Provenance(itemId)) with
        {
            Warning = FaithWriteMap.Warning + "Catalog presence does not establish ownership, slot eligibility or game-build availability. " +
                (_writes.Find(id) is null ? "This reference has no writable storage mapping." : "Only this ID is replaced; no item is allocated or removed."),
        };

    private FieldNode BindContextField(string id, string label, FieldSpec spec, Func<FieldValue> get,
        Confidence confidence, string description)
    {
        var write = _writes.Find(id);
        if (write is null) return Field(id, label, spec, get) with { Confidence = confidence, Description = description };
        var catalog = (spec as ReferenceSpec)?.CatalogId;
        if (spec is ReferenceSpec reference) spec = reference with
        {
            Assess = record => write.Validate(FieldValue.Of(record.Key), catalog, _catalog) is { } error
                ? new ReferenceEligibility { Eligible = false, Reasons = [error] } : new ReferenceEligibility(),
        };
        return Field(id, label, write.Spec(spec), () => write.Get(catalog is not null),
            value => write.Set(value, catalog is not null), value => write.Validate(value, catalog, _catalog)) with
        {
            Confidence = confidence == Confidence.Verified ? Confidence.Inferred : confidence,
            Description = description + $" Fixed-width file range 0x{write.Offset:X}..0x{write.Offset + write.Width:X}. " +
                FaithWriteMap.Warning + $"Conservative observed/type bounds {write.Minimum}..{write.Maximum}; original values are preserved.",
            Warning = FaithWriteMap.Warning,
        };
    }

    private int ReadInt(int offset) => BinaryPrimitives.ReadInt32LittleEndian(_bytes.AsSpan(offset));
    private uint ReadCrc() => BinaryPrimitives.ReadUInt32LittleEndian(_bytes.AsSpan(FaithContainer.CrcOffset));
}
