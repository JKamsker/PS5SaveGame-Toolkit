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
    private readonly RegulationViews _regulation;
    private readonly RunePlanning _runes;
    private readonly ValueWrites _writes;
    private readonly WritePlanning _writePlans;
    private readonly ProgressViews _progress;
    private readonly CommunityReferences _community;

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
        _writes = new(bytes, save, characters);
        // An actual content fingerprint, not a guessed application/regulation version number.
        var fingerprint = Convert.ToHexStringLower(SHA256.HashData(
            bytes.AsSpan(EldenRingLayout.RegulationOffset, EldenRingLayout.RegulationSize)));
        _community = new(references, fingerprint, bytes, characters);
        _catalog = new(references.Catalog(ReferenceBrowser.CatalogId, fingerprint), _community.Items);
        _inventory = new(characters, _catalog, asset => references.AssetProvenance(asset)?.Attribution.ToShortText(), _writes.Current);
        _writePlans = new(bytes, save, characters, _writes, _catalog);
        _semantic = new(bytes, characters, _catalog, references.Catalog(ProgressReferences.ClassCatalogId, fingerprint));
        _progress = new(bytes, characters, references.Catalog(ProgressReferences.CatalogId, fingerprint), references: references);
        _regulation = new(bytes, _community.Layouts);
    }

    public IReadOnlyList<QueryableCollection> Collections =>
        [.. _inventory.Collections, _catalog.Collection, .. _progress.Collections, .. _regulation.Collections, .. _community.Collections];

    public QueryableCollectionPage Query(string collectionId, QueryableCollectionQuery query) =>
        collectionId == ReferenceBrowser.CollectionId ? _catalog.Query(query)
            : collectionId == RegulationViews.CollectionId ? _regulation.Query(query)
            : _community.OwnsCatalog(collectionId) ? _community.Query(collectionId, query)
            : _community.Progress.OwnsCollection(collectionId) ? _community.Progress.Query(collectionId, query, (id, label, spec, get) => Field(id, label, spec, get))
            : _progress.OwnsCollection(collectionId) ? _progress.Query(collectionId, query, (id, label, spec, get) => Field(id, label, spec, get))
            : _inventory.Query(collectionId, query, ItemField, (entry, slot) => _writes.Quantity(entry, slot, Field));

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
                new Stat("Editing", CanEdit ? "Experimental names, runes/history and ordinary stacks" : "Read-only version"),
            ]),
            new TextNode(CanEdit
                ? ValueWrites.Warning
                : "This character/profile version has not been verified for writing. All fields are read-only.") { Tone = Tone.Warning },
            new TextNode("Slot indices are zero-based. Level and attributes are read-only: level = sum(attributes) − 79."),
            Profiles(),
        ]),
        new Tab("Characters", save.Slots.Select(slot => (ViewNode)SectionNode.Lazy(
            $"Slot {slot.Index}", $"characters/{slot.Index}", slot.Active ? "Active" : "Inactive")).ToArray()),
        new Tab("Inventory / storage", _inventory.Collections.Select(Table).ToArray()),
        new Tab("Reference catalog", CatalogView()),
        new Tab(ProgressViews.TabTitle, [.. _progress.Root(), .. _community.ProgressSections]),
        new Tab("Raw", _hex.BuildSections()),
    ]);

    public override IReadOnlyList<ViewNode> ExpandNode(string lazyId)
    {
        if (lazyId == "regulation/metadata") return _regulation.BuildView();
        if (_community.OwnsCatalog(lazyId)) return _community.ExpandCatalog(lazyId);
        if (lazyId == CommunityReferences.ProgressSectionId) return _community.Progress.Root();
        if (lazyId.StartsWith(CommunityReferences.ProgressSectionId + "/", StringComparison.Ordinal))
            return _community.Progress.Expand(lazyId, (id, label, spec, get) => Field(id, label, spec, get));
        if (lazyId.StartsWith("progress/", StringComparison.Ordinal))
            return _progress.Expand(lazyId, (id, label, spec, get) => Field(id, label, spec, get));
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
        ValidateOutput();
        return [new FileOutput(path, bytes.ToArray())];
    }

    public override EditOutcome ApplyEdits(IReadOnlyList<ValueEdit> edits)
    {
        if (!CanEdit) return EditOutcome.Rejected("document", "This character/profile version is read-only.");
        if (edits.Count == 0) return new EditOutcome();
        if (edits.Select(edit => edit.ValueId).Distinct(StringComparer.Ordinal).Count() != edits.Count)
            return EditOutcome.Rejected("document", "Duplicate field edits are not supported.");
        var original = bytes.ToArray();
        try
        {
            var result = base.ApplyEdits(edits);
            if (result.Succeeded) ValidateOutput();
            else original.CopyTo(bytes, 0);
            return result;
        }
        catch (SaveFormatException exception)
        {
            original.CopyTo(bytes, 0);
            return EditOutcome.Rejected("document", exception.Message);
        }
    }

    private void ValidateOutput()
    {
        // PS has no known per-slot MD5. Check all modeled structure/mirrors before publication/export.
        var parsed = EldenRingParser.Parse(bytes);
        foreach (var slot in parsed.Slots.Where(slot => slot.Active && slot.Version == EldenRingLayout.WritableSlotVersion))
            _ = SemanticReader.Read(bytes, slot);
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
    }) { IsEmpty = !slot.Active }).ToArray())
    {
        Title = "Character profile summaries", EmptyRows = new("name"),
        Description = "Active names, levels, play time and rune history are cross-checked against character data.",
    };

    private IReadOnlyList<ViewNode> CharacterFields(CharacterSlot slot)
    {
        if (!slot.Active)
        {
            return [new TextNode("Inactive profile. Any remaining bytes are preserved without interpreting deleted character data.")];
        }

        var player = slot.PlayerOffset!.Value;
        var maximumRunes = _writes.RuneCeiling(slot);
        var fields = new List<ViewNode>
        {
            Name(slot), Level(slot), PlayTime(slot),
            Field($"slots/{slot.Index}/runes", "Runes held", new IntegerSpec(0, maximumRunes),
                () => FieldValue.Of((long)Read(player + EldenRingLayout.RunesOffset)),
                CanEdit ? value => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(player + EldenRingLayout.RunesOffset), value.AsUInt32()) : null,
                value => value.Kind != FieldValueKind.Integer || value.AsInteger() < 0 || value.AsInteger() > maximumRunes
                    ? $"Runes must be a whole number from 0 to {maximumRunes}." : null) with
                {
                    Confidence = Confidence.Inferred,
                    Description = ValueWrites.Warning + " Must not exceed lifetime history. Use linked F6 planning to raise history when needed.",
                    Warning = ValueWrites.Warning,
                },
            _writes.History(slot, Field),
            ReadOnlyInteger($"slots/{slot.Index}/version", "Character format version", slot.Offset, Confidence.Unknown),
        };
        string[] attributeNames = ["Vigor", "Mind", "Endurance", "Strength", "Dexterity", "Intelligence", "Faith", "Arcane"];
        var attributes = new List<FieldNode>();
        for (var index = 0; index < attributeNames.Length; index++)
        {
            var name = attributeNames[index];
            attributes.Add(ReadOnlyInteger($"slots/{slot.Index}/attributes/{name.ToLowerInvariant()}", name,
                player + EldenRingLayout.AttributesOffset + index * sizeof(uint), Confidence.Inferred) with
                {
                    Description = "Read-only: class minima, modifier-aware HP/FP/stamina, equip load and integrity updates are unverified. "
                        + "Source: " + SemanticViews.Provenance,
                });
        }
        fields.AddRange(attributes);
        fields.Add(CharacterGraph.Build(slot.Index, attributes));
        fields.AddRange(_semantic.Character(slot, (id, label, spec, get) => Field(id, label, spec, get)));
        fields.Add(_runes.Action(slot));
        fields.AddRange(_writePlans.Actions(slot));
        return fields;
    }

    private static QueryableTableNode Table(QueryableCollection collection) =>
        new(collection.Id, collection.Title, collection.Columns, collection.TotalCount)
        { Description = collection.Description, EmptyRows = collection.EmptyRows };

    private FieldNode ItemField(InventoryEntry entry, int slot)
    {
        var record = _catalog.Find(entry.RawId);
        return Field(entry.Key(slot) + "/item", entry.IsEmpty ? InventoryBrowser.EmptySlotLabel : record?.Name ?? entry.RawId,
            new ReferenceSpec(_catalog.FieldCatalogId(entry.RawId)), () => FieldValue.Of(entry.RawId)) with
        {
            Confidence = entry.ItemId.HasValue ? Confidence.Inferred : Confidence.Unknown,
            Description = _catalog.ItemDetails(entry.RawId),
            Warning = "Read-only identity. Only allowlisted positive ordinary-stack quantities can change; equipment relationships remain protected.",
        };
    }

    private IReadOnlyList<ViewNode> CatalogView()
    {
        List<ViewNode> nodes = [Table(_catalog.Collection), SectionNode.Lazy("Embedded regulation directory", "regulation/metadata"),
            .. _community.CatalogSections];
        // A reference lookup has no save effects and reports no changes, preserving undo history.
        if (_catalog.Collection.TotalCount > 0)
            nodes.Add(new ActionNode("references/inspect", "Inspect an item reference")
            {
                Mutates = false,
                Description = "Search the shared reference picker and read its provenance. This changes no save bytes.",
                Parameters = [new FieldNode("item", "Reference item", new ReferenceSpec(ReferenceBrowser.CatalogId), FieldValue.Of("0x400050F0"))],
            });
        return nodes;
    }

    public override ActionOutcome InvokeAction(string actionId, IReadOnlyDictionary<string, FieldValue> parameters)
    {
        if (_writePlans.Owns(actionId) || save.Slots.Any(slot => slot.Active && RunePlanning.ActionId(slot) == actionId))
            throw new InvalidOperationException("Review and apply this action through F6 planning.");
        if (actionId != "references/inspect") return base.InvokeAction(actionId, parameters);
        if (parameters.Count != 1 || !parameters.TryGetValue("item", out var value) || value.Kind != FieldValueKind.Text
            || _catalog.Find(value.AsString()) is not { } record)
            throw new ArgumentException("Choose a known reference item; unknown ids are not lookup candidates.", nameof(parameters));
        return new ActionOutcome { Message = $"{record.Name} ({record.Key}) · {ReferenceBrowser.Details(record)}" };
    }

    public ActionPlan PlanAction(string actionId, IReadOnlyDictionary<string, FieldValue> parameters, ActionScope scope) =>
        _writePlans.Owns(actionId) ? _writePlans.Plan(actionId, parameters, scope) : _runes.Plan(actionId, parameters, scope);

    public EditOutcome ApplyActionPlan(string actionId, IReadOnlyDictionary<string, FieldValue> parameters, ActionScope scope, ActionPlan plan) =>
        ActionPlans.ApplyScalar(plan, () => PlanAction(actionId, parameters, scope),
            new(actionId, "The preview no longer matches the requested changes."),
            _ =>
            {
                CharacterFields(_writePlans.Owns(actionId) ? _writePlans.Slot(actionId) : _runes.Slot(actionId));
                _writes.RegisterQuantities(Field); // Bind unopened rows for complete preview/apply/undo.
            },
            current => current.Changes.Select(change => new ValueEdit(change.ValueId, change.NewValue)).ToArray(),
            ApplyEdits, ActionPlanWarningPolicy.PreserveApplication);

    private FieldNode Name(CharacterSlot slot) => _writes.Name(slot, Field);

    private FieldNode Level(CharacterSlot slot) => ReadOnlyInteger($"slots/{slot.Index}/level", "Level",
        slot.PlayerOffset!.Value + EldenRingLayout.LevelOffset, Confidence.Inferred) with
        { Description = "Sum of the eight attributes minus 79; matched to the profile summary. Source: " + SemanticViews.Provenance };

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
