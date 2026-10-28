using System.Buffers.Binary;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.References;
using System.Security.Cryptography;

namespace SaveToolkit.Plugins.EldenRing;

internal sealed class EldenRingDocument : BoundSaveDocument, IQueryableDocument, IActionPlanningDocument
{
    private readonly string path;
    private readonly byte[] bytes;
    private readonly ParsedSave save;
    private readonly EldenRingHexExplorer _hex;
    private readonly IReferenceContext _references;
    private readonly ReferenceBrowser _catalog;
    private readonly InventoryBrowser _inventory;
    private readonly SemanticViews _semantic;
    private readonly Lazy<IReadOnlyList<ViewNode>> _regulation;
    private readonly RunePlanning _runes;

    public EldenRingDocument(string path, byte[] bytes, ParsedSave save, IReferenceContext references)
    {
        this.path = path;
        this.bytes = bytes;
        this.save = save;
        _runes = new(bytes, save);
        _references = references;
        _hex = new(bytes, save);
        var characters = save.Slots.Where(slot => slot.Active && slot.Version == EldenRingLayout.WritableSlotVersion)
            .Select(slot => SemanticReader.Read(bytes, slot)).ToArray();
        // An actual content fingerprint, not a guessed application/regulation version number.
        var fingerprint = Convert.ToHexStringLower(SHA256.HashData(
            bytes.AsSpan(EldenRingLayout.RegulationOffset, EldenRingLayout.RegulationSize)));
        _catalog = new(references.Catalog(ReferenceBrowser.CatalogId, fingerprint));
        _inventory = new(characters, _catalog, asset => references.AssetProvenance(asset)?.Attribution.ToShortText());
        _semantic = new(bytes, characters, _catalog);
        _regulation = new(() => RegulationViews.Read(bytes));
    }

    public IReadOnlyList<QueryableCollection> Collections => [.. _inventory.Collections, _catalog.Collection];

    public QueryableCollectionPage Query(string collectionId, QueryableCollectionQuery query) =>
        collectionId == ReferenceBrowser.CollectionId ? _catalog.Query(query) : _inventory.Query(collectionId, query, ItemField);

    public override AssetContent? GetAsset(string assetId) => _references.GetAsset(assetId);

    public override string Title => "Elden Ring · PlayStation character slots";
    public override bool CanEdit => save.CanEdit;

    public override ViewNode BuildView() => new TabsNode(
    [
        new Tab("Profiles",
        [
            new StatsNode(
            [
                new Stat("Characters", $"{save.Slots.Count(slot => slot.Active)} / {EldenRingLayout.SlotCount}"),
                new Stat("Format", "PlayStation memory.dat"),
                new Stat("Editing", CanEdit ? "Held runes only" : "Read-only version"),
            ]),
            new TextNode(CanEdit
                ? "Experimental: held runes can be changed up to the existing lifetime rune total. Changes have not been tested in-game."
                : "This character/profile version has not been verified for writing. All fields are read-only.") { Tone = Tone.Warning },
            new TextNode("Slot indices are zero-based. Level and attributes are read-only: level = sum(attributes) − 79."),
            Profiles(),
        ]),
        new Tab("Characters", save.Slots.Select(slot => (ViewNode)SectionNode.Lazy(
            $"Slot {slot.Index}", $"characters/{slot.Index}", slot.Active ? "Active" : "Inactive")).ToArray()),
        new Tab("Inventory / storage", _inventory.Collections.Select(Table).ToArray()),
        new Tab("Reference catalog", CatalogView()),
        new Tab("Raw", _hex.BuildSections()),
    ]);

    public override IReadOnlyList<ViewNode> ExpandNode(string lazyId)
    {
        if (lazyId == "regulation/metadata") return _regulation.Value;
        foreach (var slot in save.Slots)
        {
            if (lazyId == $"characters/{slot.Index}")
            {
                return CharacterFields(slot);
            }
        }

        return _hex.Expand(lazyId);
    }

    public override ReadOnlyMemory<byte> ReadBlob(string blobId, long offset, int count) => _hex.Read(blobId, offset, count);

    public override IReadOnlyList<FileOutput> Serialize()
    {
        // No PS per-slot MD5 exists. Recheck structure and profile mirrors before returning patched bytes.
        _ = EldenRingParser.Parse(bytes);
        return [new FileOutput(path, bytes.ToArray())];
    }

    private TableNode Profiles() => new(
    [
        new TableColumn("slot", "Slot") { Width = ColumnWidth.Narrow },
        new TableColumn("status", "Status"),
        new TableColumn("name", "Name"),
        new TableColumn("level", "Level"),
        new TableColumn("time", "Play time"),
    ], save.Slots.Select(slot => new TableRow($"slot/{slot.Index}", new Dictionary<string, ViewNode>
    {
        ["slot"] = new TextNode(slot.Index.ToString(System.Globalization.CultureInfo.InvariantCulture)),
        ["status"] = new TextNode(slot.Active ? "Active" : "Inactive"),
        ["name"] = slot.Active ? Name(slot) : new TextNode("—"),
        ["level"] = slot.Active ? Level(slot) : new TextNode("—"),
        ["time"] = slot.Active ? PlayTime(slot) : new TextNode("—"),
    })).ToArray()) { Title = "Character profile summaries", Description = "Active names, levels, play time and rune history are cross-checked against character data." };

    private IReadOnlyList<ViewNode> CharacterFields(CharacterSlot slot)
    {
        if (!slot.Active)
        {
            return [new TextNode("Inactive profile. Any remaining bytes are preserved without interpreting deleted character data.")];
        }

        var player = slot.PlayerOffset!.Value;
        var memory = Read(player + EldenRingLayout.RuneMemoryOffset);
        var maximumRunes = Math.Min(memory, EldenRingLayout.MaximumRunes);
        var fields = new List<ViewNode>
        {
            Name(slot), Level(slot), PlayTime(slot),
            Field($"slots/{slot.Index}/runes", "Runes held", new IntegerSpec(0, maximumRunes),
                () => FieldValue.Of((long)Read(player + EldenRingLayout.RunesOffset)),
                CanEdit ? value => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(player + EldenRingLayout.RunesOffset), value.AsUInt32()) : null,
                value => value.Kind != FieldValueKind.Integer || value.AsInteger() < 0 || value.AsInteger() > maximumRunes
                    ? $"Runes must be a whole number from 0 to {maximumRunes}." : null) with
                {
                    Confidence = Confidence.Verified,
                    Warning = "Limited to the existing lifetime rune total; lifetime history and its profile copy remain unchanged.",
                },
            ReadOnlyInteger($"slots/{slot.Index}/rune-memory", "Lifetime rune total", player + EldenRingLayout.RuneMemoryOffset),
            ReadOnlyInteger($"slots/{slot.Index}/version", "Character format version", slot.Offset, Confidence.Unknown),
        };
        string[] attributeNames = ["Vigor", "Mind", "Endurance", "Strength", "Dexterity", "Intelligence", "Faith", "Arcane"];
        for (var index = 0; index < attributeNames.Length; index++)
        {
            var name = attributeNames[index];
            fields.Add(ReadOnlyInteger($"slots/{slot.Index}/attributes/{name.ToLowerInvariant()}", name,
                player + EldenRingLayout.AttributesOffset + index * sizeof(uint)) with
                {
                    Description = "Read-only until class minima and dependent HP/FP/stamina updates are validated.",
                });
        }

        fields.AddRange(_semantic.Character(slot, (id, label, spec, get) => Field(id, label, spec, get)));
        fields.Add(_runes.Action(slot));
        return fields;
    }

    private static QueryableTableNode Table(QueryableCollection collection) =>
        new(collection.Id, collection.Title, collection.Columns, collection.TotalCount) { Description = collection.Description };

    private FieldNode ItemField(InventoryEntry entry, int slot)
    {
        var record = _catalog.Find(entry.RawId);
        return Field(entry.Key(slot) + "/item", record?.Name ?? entry.RawId,
            new ReferenceSpec(ReferenceBrowser.CatalogId), () => FieldValue.Of(entry.RawId)) with
        {
            Confidence = entry.ItemId.HasValue ? Confidence.Inferred : Confidence.Unknown,
            Description = record?.AttributionText ?? "Unknown name; raw identity preserved. No compatible reference record.",
            Warning = "Read-only. Item replacement, quantities and equipment relationships need controlled captures.",
        };
    }

    private IReadOnlyList<ViewNode> CatalogView()
    {
        var nodes = new List<ViewNode> { Table(_catalog.Collection), SectionNode.Lazy("Embedded regulation directory", "regulation/metadata") };
        // A reference lookup has no save effects and reports no changes, preserving undo history.
        if (CanEdit && _catalog.Collection.TotalCount > 0)
            nodes.Add(new ActionNode("references/inspect", "Inspect an item reference")
            {
                Description = "Search the shared reference picker and read its provenance. This changes no save bytes.",
                Parameters = [new FieldNode("item", "Reference item", new ReferenceSpec(ReferenceBrowser.CatalogId), FieldValue.Of("0x400050F0"))],
            });
        return nodes;
    }

    public override ActionOutcome InvokeAction(string actionId, IReadOnlyDictionary<string, FieldValue> parameters)
    {
        if (save.Slots.Any(slot => slot.Active && RunePlanning.ActionId(slot) == actionId))
            throw new InvalidOperationException("Review and apply this action through F6 planning.");
        if (actionId != "references/inspect") return base.InvokeAction(actionId, parameters);
        if (parameters.Count != 1 || !parameters.TryGetValue("item", out var value) || value.Kind != FieldValueKind.Text
            || _catalog.Find(value.AsString()) is not { } record)
            throw new ArgumentException("Choose a known reference item; unknown ids are not lookup candidates.", nameof(parameters));
        return new ActionOutcome { Message = $"{record.Name} ({record.Key}) · {ReferenceBrowser.Details(record)}" };
    }

    public ActionPlan PlanAction(string actionId, IReadOnlyDictionary<string, FieldValue> parameters, ActionScope scope) =>
        _runes.Plan(actionId, parameters, scope);

    public EditOutcome ApplyActionPlan(string actionId, IReadOnlyDictionary<string, FieldValue> parameters, ActionScope scope, ActionPlan plan)
    {
        var current = _runes.Plan(actionId, parameters, scope);
        if (current.Errors.Count > 0) return new EditOutcome { Errors = current.Errors };
        if (plan.Errors.Count > 0 || !current.Changes.SequenceEqual(plan.Changes)
            || !current.Warnings.SequenceEqual(plan.Warnings) || plan.SkippedRows.Count > 0)
            return EditOutcome.Rejected(actionId, "The preview no longer matches the requested rune change.");
        _ = CharacterFields(_runes.Slot(actionId)); // Register scalar undo bindings even if opened through a direct API request.
        return ApplyEdits(current.Changes.Select(change => new ValueEdit(change.ValueId, change.NewValue)).ToArray());
    }

    private FieldNode Name(CharacterSlot slot) => Field($"slots/{slot.Index}/name", "Name",
        new TextSpec { MaxLength = EldenRingLayout.NameCharacters },
        () => FieldValue.Of(EldenRingParser.ReadName(bytes, slot.ProfileOffset))) with { Confidence = Confidence.Verified };

    private FieldNode Level(CharacterSlot slot) => ReadOnlyInteger($"slots/{slot.Index}/level", "Level",
        slot.PlayerOffset!.Value + EldenRingLayout.LevelOffset) with { Description = "Sum of the eight attributes minus 79; matched to the profile summary." };

    private FieldNode PlayTime(CharacterSlot slot) => Field($"slots/{slot.Index}/playtime", "Play time",
        IntegerSpec.UInt64 with { Format = IntegerFormat.Duration, UnitsPerSecond = (long)EldenRingLayout.MillisecondsPerSecond },
        () => FieldValue.Of(EldenRingParser.ReadUInt64(bytes, slot.Offset + EldenRingLayout.SlotPlayTimeOffset))) with
        {
            Confidence = Confidence.Verified,
            Description = "Milliseconds in character data; whole seconds are cross-checked against the profile summary.",
        };

    private FieldNode ReadOnlyInteger(string id, string label, int offset, Confidence confidence = Confidence.Verified) =>
        Field(id, label, IntegerSpec.UInt32, () => FieldValue.Of((long)Read(offset))) with { Confidence = confidence };

    private uint Read(int offset) => EldenRingParser.ReadUInt32(bytes, offset);
}
