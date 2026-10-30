using System.Text;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.Gearbox;

internal sealed class GearboxDocument(string relativePath, string titleId, GearboxContainer container, ProtoMessage root,
    IReferenceContext references) : BoundSaveDocument, IQueryableDocument, IActionEffectDocument
{
    private readonly Dictionary<string, Func<FieldValue>> _readers = new(StringComparer.Ordinal);
    private readonly GearboxLazyViews _lazy = new();
    private readonly GearboxSerialIntegrity _serialIntegrity = new(root, container.Profile);
    private readonly GearboxAmmoRestore _ammoOpening = new(root, container.Profile);
    private GearboxCoverageActions CoverageActions => new(this, _ammoOpening);
    private int _highestLevel = container.Profile ? 0 : root.Schema.Level(checked((int)root.Integer("experience_points")));
    private ItemBrowser? _items;
    private GearboxInventory? _inventory;
    private GearboxPartBindings? _parts;
    internal GearboxPartBindings PartBindings => _parts ??= new(this);
    private GearboxProgressionBindings? _progression;
    internal GearboxInventory Inventory => _inventory ??= new(this);
    private GearboxItemActions? _actions;
    private GearboxItemActions Actions => _actions ??= new(this);
    private ItemBrowser Items => _items ??= new(this, Root, Profile, _lazy, references);
    private GearboxWorldCatalog? _world;
    internal GearboxWorldCatalog World => _world ??= new(this, references);
    private GearboxSkills? _skills;
    private GearboxSkills Skills => _skills ??= new(this, _lazy, references);
    private GearboxMissions? _missions;
    internal GearboxMissions Missions => _missions ??= new(this, _lazy);
    private GearboxTables Tables => new(this, _lazy);
    private GearboxAccountViews Account => new(this, _lazy);
    public IReadOnlyList<QueryableCollection> Collections =>
        [.. Items.Collections, World.Collection, .. Skills.Collections, Missions.Collection, .. Tables.Collections, .. Account.Collections];
    public QueryableCollectionPage Query(string collectionId, QueryableCollectionQuery query) =>
        collectionId == GearboxWorldCatalog.CatalogId ? World.Query(query) :
        collectionId is GearboxSkills.CatalogId or GearboxSkills.AllocationsId ? Skills.Query(collectionId, query) :
        collectionId == GearboxMissions.CollectionId ? Missions.Query(query) :
        collectionId is "slots" or "sdus" or "ammo" ? Tables.Query(collectionId, query) :
        collectionId.StartsWith("account/", StringComparison.Ordinal) ? Account.Query(collectionId, query) : Items.Query(collectionId, query);
    public override ActionOutcome InvokeAction(string actionId, IReadOnlyDictionary<string, FieldValue> parameters) =>
        Items.Inspect(actionId, parameters) ?? base.InvokeAction(actionId, parameters);
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    public ProtoMessage Root { get; private set; } = root;
    public bool Profile => container.Profile;
    internal bool Coordinated { get; init; }
    public override string Title => Profile ? "Gearbox profile" : Root.Text("preferred_character_name");
    public override AssetContent? GetAsset(string assetId) => references.GetAsset(assetId);

    /// <summary>Resolve only requested parsed fields; no view, lazy section or collection page is built.</summary>
    public override IReadOnlyList<FieldMetadata> ResolveFieldMetadata(IReadOnlyList<string> valueIds) =>
        GearboxMetadata.Resolve(this, valueIds);

    internal IReadOnlyList<FieldMetadata> ResolveRegistered(IReadOnlyList<string> valueIds) =>
        base.ResolveFieldMetadata(valueIds);

    public override ViewNode BuildView()
    {
        var overview = new GearboxOverview(this);
        var tables = new GearboxTables(this, _lazy);
        return new TabsNode([
            new Tab(Profile ? "Profile" : "Character", overview.Build()),
            new Tab("Inventory", [.. Items.Build(), .. tables.Inventory()]),
            new Tab("Progression", tables.Progression()),
            new Tab("Skills", Skills.Build()),
            new Tab("Account / character mirrors", new GearboxAccountViews(this, _lazy).Build()),
            new Tab("World / progression catalogue", [World.Build()]),
            new Tab("Raw", [_lazy.RegisterRaw(() => Root)]),
        ]);
    }

    public override IReadOnlyList<ViewNode> ExpandNode(string lazyId) => _lazy.Expand(lazyId);

    internal FieldNode InspectionField(string id, string label, string value, Confidence confidence, string? source) =>
        Bind(id, label, new TextSpec { ReadOnly = true, Multiline = true }, () => FieldValue.Of(value), null,
            confidence: confidence, description: source)
            with { Confidence = confidence, Description = source };

    internal FieldNode InspectionReference(string id, string label, string value, string catalog, Confidence confidence, string? source) =>
        Bind(id, label, new ReferenceSpec(catalog) { ReadOnly = true }, () => FieldValue.Of(value), null,
            confidence: confidence, description: source)
            with { Confidence = confidence, Description = source };

    internal FieldNode ItemNumber(string id, string label, int minimum, int maximum, Func<int> get, Action<int> set,
        string? warning = null, string? description = null) =>
        Bind(id, label, new IntegerSpec(Math.Min(minimum, get()), Math.Max(maximum, get())), () => FieldValue.Of(get()),
            value => set(value.AsInt32()), value => value.Kind != FieldValueKind.Integer || value.AsInteger() < minimum || value.AsInteger() > maximum
                ? "Value is outside the supported editor policy." : null, warning ?? SerialWrites.Warning,
            Confidence.Inferred, description ?? SerialWrites.Warning);

    internal FieldNode DerivedItemNumber(string id, string canonicalId, string label, int minimum, int maximum, Func<int> get)
    {
        var field = Field(id, label, new IntegerSpec(Math.Min(minimum, get()), Math.Max(maximum, get())), () => FieldValue.Of(get())) with
        {
            Confidence = Confidence.Inferred, Description = SerialWrites.Warning, Warning = SerialWrites.Warning,
        };
        return RegisterDerivedField(field, () => FieldValue.Of(get()), canonicalId);
    }

    internal FieldNode RawFlags(string id, Func<ProtoMessage> parent) =>
        ActionObservation(id, "Item flags", IntegerSpec.UInt32,
            () => FieldValue.Of(parent().Integer("flags")),
            "Existing parsed flags are inspectable only. Mark actions change mask 6 and preserve unknown bits; arbitrary raw flag writes are unavailable.");

    internal FieldNode MythPoints(string id, Func<ProtoMessage?> get, int position, int maximum, string description)
    {
        if (!Coordinated)
            return InspectionField(id, "Stored points", GearboxAccountViews.TryIntegerAt(get()!,
                "points_spent_by_index_order", position, out var observed, bounded: false)
                ? observed.ToString(System.Globalization.CultureInfo.InvariantCulture) : "Absent",
                Confidence.Inferred, description);
        return ItemNumber(id, "Stored points", 0, maximum,
            () => GearboxAccountViews.TryIntegerAt(get()!, "points_spent_by_index_order", position,
                out var value, bounded: false) ? value : throw new SaveFormatException("The indexed Myth allocation is absent."),
            next => ProtoArrayEdits.Set(get()!, "points_spent_by_index_order", position, next),
            description: description);
    }

    internal FieldNode PartReference(string id, Func<string> get, Action<string> set, Func<ReferenceRecord, ReferenceEligibility> assess, string source) =>
        Bind(id, "Serial part", new ReferenceSpec(ItemCatalogs.Indices) { Assess = assess }, () => FieldValue.Of(get()),
            value => set(value.AsString()), value => value.Kind == FieldValueKind.Text ? null : "Choose a serial part ID.",
            SerialParts.Warning, Confidence.Inferred, SerialParts.Warning + source);

    internal FieldNode ActionObservation(string id, string label, FieldSpec spec, Func<FieldValue> get, string description)
    {
        var field = Field(id, label, spec with { ReadOnly = true }, get) with
        {
            Confidence = Confidence.Inferred, Description = description, Warning = SerialWrites.Warning,
        };
        return RegisterDerivedField(field, get);
    }

    internal FieldNode AssetLabel(string id, string label, string path)
    {
        var record = references.Catalog(ItemCatalogs.Labels)?.Find(path) ?? SerialDatabase.For(Root.Schema.Wonderlands).Find(ItemCatalogs.Labels, path);
        return InspectionReference(id, label, path, ItemCatalogs.Labels,
            record is null ? Confidence.Unknown : Confidence.Verified, record is null ? null : record.Name + ". " + record.AttributionText);
    }

    internal FieldNode Presence(string id, Func<FieldValue> get)
    {
        var field = Bind(id, "Item presence", new IntegerSpec(0, 2), get, null,
            warning: "Item presence is changed only by its structural action.", confidence: Confidence.Inferred,
            description: "Current item identity is present. Duplicate and removal are structural actions; direct field edits are unavailable.");
        return RegisterDerivedField(field, get);
    }

    internal FieldNode DerivedInteger(string id, string label, Func<ProtoMessage> parent, string name, long maximum, long minimum = -1)
    {
        var field = Bind(id, label, new IntegerSpec(minimum, Math.Max(maximum, parent().Integer(name))),
            () => FieldValue.Of(parent().Integer(name)), null,
            value => value.Kind != FieldValueKind.Integer || value.AsInteger() < minimum || value.AsInteger() > maximum
                ? "Value is outside the supported range." : null);
        return RegisterDerivedField(field, () => FieldValue.Of(parent().Integer(name)));
    }

    private FieldNode RegisterDerivedField(FieldNode field, Func<FieldValue> get, string? canonicalId = null)
    {
        RegisterField(new FieldMetadata(field.ValueId, field.Label, field.Spec)
        {
            BindingKind = FieldBindingKind.Derived,
            CanonicalValueId = canonicalId ?? field.ValueId,
            Presentation = new FieldPresentation
            {
                Confidence = field.Confidence,
                Description = field.Description,
                Warning = field.Warning,
            },
        }, get);
        return field;
    }

    internal FieldNode Integer(string id, string label, Func<ProtoMessage> parent, string name,
        long max = int.MaxValue, bool writable = true, string? warning = null,
        Confidence confidence = Confidence.Verified, string? description = null)
    {
        var current = parent().Integer(name);
        return Bind(id, label, new IntegerSpec(Math.Min(0, current), Math.Max(max, current)), () => FieldValue.Of(parent().Integer(name)),
            writable && current >= 0 && current <= max ? v => parent().SetInteger(name, v.AsInt64()) : null,
            v => v.Kind != FieldValueKind.Integer || v.AsInteger() < 0 || v.AsInteger() > max ? "Value is outside the supported range." : null,
            warning, confidence, description);
    }

    internal FieldNode AccountNumber(string id, string label, Func<ProtoMessage> get, string name,
        long maximum, bool writable = true, string? description = null, string? warning = null) =>
        Integer(id, label, get, name, maximum, Coordinated && writable, warning ?? SerialWrites.Warning,
            Confidence.Inferred, description ?? "Existing raw stored value; observed range plus conservative editor bound. Point budgets, mirror precedence and derived rewards are not inferred. " + SerialWrites.Warning);

    internal FieldNode WorldDifficulty(int index, bool unlocked, bool boundedDiscovery = false)
    {
        var wonderlands = Root.Schema.Wonderlands;
        var label = unlocked ? $"Chaos unlocked (playthrough {index})" :
            wonderlands ? $"Chaos level (playthrough {index})" : $"Mayhem level (playthrough {index})";
        var description = unlocked ? "Raw unlocked ceiling; trial milestones and rewards are unchanged. " + SerialWrites.Warning :
            "Existing playthrough difficulty only. Unlock challenges, modifier seeds and rewards are unchanged. " + SerialWrites.Warning;
        return Integer($"world/{index}/{(unlocked ? "unlocked" : "difficulty")}", label,
            () => GearboxMetadataLookup.RequireChild(Root, "game_state_save_data_for_playthrough", index, boundedDiscovery),
            unlocked ? "mayhem_unlocked_level" : "mayhem_level", wonderlands ? 100 : 11,
            warning: SerialWrites.Warning, confidence: Confidence.Inferred, description: description);
    }

    internal bool EnsureProgressionBindings(bool boundedDiscovery = false)
        => (_progression ??= new(this)).Ensure(boundedDiscovery);

    private static bool Has(ProtoMessage message, string name) => message.Fields.Any(field => field.Spec?.Name == name);

    internal FieldNode Text(string id, string label, Func<ProtoMessage> parent, string name, bool writable = false, int limit = 64)
    {
        return Bind(id, label, new TextSpec { MaxLength = writable ? limit : null }, () => FieldValue.Of(parent().Text(name)),
            writable ? v => parent().SetText(name, v.AsString()) : null,
            v => ValidateName(v, limit), writable ? GearboxWriteWarnings.ConsoleValidation : null);
    }

    private static string? ValidateName(FieldValue value, int limit)
    {
        if (value.Kind != FieldValueKind.Text || value.AsString().Length > limit || value.AsString().Any(char.IsControl))
            return "Use a name within the displayed limit, without control characters.";
        try { _ = StrictUtf8.GetByteCount(value.AsString()); }
        catch (EncoderFallbackException) { return "The name contains invalid Unicode."; }
        return null;
    }

    internal FieldNode Ammo(string id, Func<ProtoMessage> parent)
    {
        const int maximumAmmo = GearboxAmmoRestore.Maximum;
        var current = parent().Float("amount");
        var editable = float.IsFinite(current) && current is >= 0 and <= maximumAmmo;
        return Bind(id, "Ammo", FloatSpec.Single with { Min = Math.Min(0, current), Max = Math.Max(maximumAmmo, current) },
            () => FieldValue.Of(parent().Float("amount")), editable ? v => parent().SetFloat("amount", (float)v.AsDouble()) : null,
            v => !double.IsFinite(v.AsDouble()) || v.AsDouble() is < 0 or > maximumAmmo ? "Unsupported ammo value." : null,
            GearboxWriteWarnings.ConsoleValidation + " The game may clamp ammo to the capacity provided by its SDU.");
    }

    internal FieldNode Level()
    {
        var schema = Root.Schema;
        return Bind("character/level", "Level", new IntegerSpec(1, schema.Experience.Count),
            () => FieldValue.Of(schema.Level(checked((int)Root.Integer("experience_points")))),
            v => SetExperience(schema.Experience[v.AsInt32() - 1]),
            v => v.Kind != FieldValueKind.Integer || v.AsInteger() < 1 || v.AsInteger() > schema.Experience.Count ? "Unsupported level." : null,
            "Adjusts XP and the recorded level; Wonderlands awards skill points when levelling up. Lowering level preserves spent points. Milestone challenges and mission unlocks are unchanged.");
    }

    internal FieldNode Experience() => Bind("character/xp", "Experience points", new IntegerSpec(0, int.MaxValue),
        () => FieldValue.Of(Root.Integer("experience_points")), v => SetExperience(v.AsInt32()),
        v => v.Kind != FieldValueKind.Integer || v.AsInteger() < 0 || v.AsInteger() > int.MaxValue ? "Unsupported XP value." : null,
        "Changing XP can change level. Values beyond the supported level cap remain experimental.");

    internal FieldNode Category(string list, uint hash, string label, long maximum, string id, bool boundedDiscovery = false)
    {
        var current = boundedDiscovery
            ? GearboxMetadataLookup.TryFindMessage(Root, list,
                category => category.Integer("base_category_definition_hash") == hash, out var category)
                ? category.Integer("quantity") : 0
            : Root.CategoryQuantity(list, hash);
        return Bind(id, label, new IntegerSpec(Math.Min(0, current), Math.Max(maximum, current)),
            () => FieldValue.Of(Root.CategoryQuantity(list, hash)),
            current >= 0 && current <= maximum ? v => Root.SetCategoryQuantity(list, hash, v.AsInt64()) : null,
            v => v.Kind != FieldValueKind.Integer || v.AsInteger() < 0 || v.AsInteger() > maximum ? "Unsupported currency amount." : null,
            GearboxWriteWarnings.ConsoleValidation + " The game may clamp large values. Absent currency categories are created only when a nonzero amount is set.");
    }

    private FieldNode Bind(string id, string label, FieldSpec spec, Func<FieldValue> get, Action<FieldValue>? set,
        Func<FieldValue, string?>? validate = null, string? warning = null,
        Confidence confidence = Confidence.Verified, string? description = null)
    {
        _readers[id] = get;
        var field = Field(id, label, spec, get, set, validate) with
        {
            Confidence = confidence,
            Description = description ?? GearboxWriteWarnings.SchemaEvidence,
            Warning = set is null ? warning : GearboxWriteWarnings.ForWrite(warning),
        };
        RegisterField(new FieldMetadata(id, label, field.Spec)
        {
            BindingKind = FieldBindingKind.Primary,
            CanonicalValueId = id,
            Presentation = new FieldPresentation
            {
                Confidence = field.Confidence,
                Description = field.Description,
                Warning = field.Warning,
            },
        }, get, set, validate);
        return field;
    }

    public override EditOutcome ApplyEdits(IReadOnlyList<ValueEdit> edits)
    {
        var before = _readers.ToDictionary(p => p.Key, p => p.Value());
        var previous = Root;
        var previousHighest = _highestLevel;
        var snapshot = CaptureState();
        var committed = false;
        try
        {
            Root = Root.Clone();
            // Replayed derived fields (XP, skill points, recorded level) override automatic level adjustments.
            var ordered = edits.OrderBy(e => e.ValueId == "character/level" ? 0 : e.ValueId == "character/xp" ? 1 : 2).ToArray();
            var result = base.ApplyEdits(ordered);
            if (!result.Succeeded) return result;
            var changes = _readers.Select(p => new ValueChange(p.Key, before[p.Key], p.Value())).Where(c => c.OldValue != c.NewValue).ToArray();
            // A net-zero batch has no history entry. Discard incidental wire normalization and
            // policy-state changes along with its clone, retaining the exact imported state.
            if (changes.Length == 0) return result with { Changes = changes };
            // Scalar edits retain their bindings: direct SDK callers may edit again before rebuilding.
            _items = null;
            _inventory?.Invalidate();
            var outcome = result with { Changes = changes, StructureChanged = true, Revert = snapshot };
            committed = true;
            return outcome;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return EditOutcome.Rejected(string.Empty, ex.Message);
        }
        finally
        {
            // Rollback setters may reject an already over-capacity imported state. The original
            // model is authoritative even when the SDK's value-by-value rollback throws.
            if (!committed)
            {
                Root = previous;
                _highestLevel = previousHighest;
            }
        }
    }

    public ActionPlan PlanAction(string actionId, IReadOnlyDictionary<string, FieldValue> parameters, ActionScope scope) =>
        GearboxCoverageActions.Handles(actionId) ? CoverageActions.Plan(actionId, parameters, scope) : Actions.Plan(actionId, parameters, scope);

    public EditOutcome ApplyActionPlan(string actionId, IReadOnlyDictionary<string, FieldValue> parameters, ActionScope scope, ActionPlan plan)
    {
        var validation = ActionPlans.Revalidate(plan, () => PlanAction(actionId, parameters, scope), new("", "The Gearbox preview changed; preview again before applying."));
        var fresh = validation.Plan;
        if (!validation.Succeeded) return new() { Errors = validation.Errors, Warnings = fresh.Warnings };
        if (fresh.Changes.Count == 0) return new() { Warnings = fresh.Warnings };
        var outcome = Structural(() =>
        {
            if (GearboxCoverageActions.Handles(actionId)) CoverageActions.Apply(actionId, parameters, fresh);
            else Actions.Apply(actionId, parameters, scope);
        }, SerialWrites.Warning);
        return new() { StructureChanged = true, Revert = outcome.Revert, Warnings = fresh.Warnings,
            Changes = fresh.Changes.Select(c => new ValueChange(c.ValueId, c.OldValue, c.NewValue)).ToArray() };
    }

    internal void InvalidateItems()
    {
        _items = null;
        _inventory?.Invalidate();
        _progression?.Invalidate();
        _readers.Clear();
    }
    private void SetExperience(int experience)
    {
        CharacterProgression.SetExperience(Root, experience, _highestLevel);
        _highestLevel = Math.Max(_highestLevel, Root.Schema.Level(experience));
    }

    internal DocumentMemento CaptureState() => new GearboxSnapshot(Root.Clone(), Inventory.Capture(), _highestLevel);
    public DocumentMemento CaptureMemento() => CaptureState();
    protected override DocumentMemento CaptureSnapshot() => CaptureState();
    protected override void LoadSnapshot(DocumentMemento memento)
    {
        if (memento is not GearboxSnapshot snapshot || snapshot.Root.Type != Root.Type || snapshot.Root.Schema != Root.Schema)
            throw new InvalidOperationException("Invalid Gearbox document snapshot.");
        Root = snapshot.Root.Clone();
        Inventory.Restore(snapshot.Identity);
        _highestLevel = snapshot.HighestLevel;
        _readers.Clear();
        InvalidateItems();
    }

    public override IReadOnlyList<FileOutput> Serialize()
    {
        _serialIntegrity.Validate(Root, Profile);
        var output = container.Encode(Root);
        var check = GearboxContainer.Parse(output, titleId, Profile).Decode();
        foreach (var list in Profile ? new[] { GearboxInventory.Bank, GearboxInventory.LostLoot } : [GearboxInventory.Backpack])
            if (check.All(list).Count() != Root.All(list).Count())
                throw new SaveFormatException("Gearbox inventory record count changed during serialization.");
        if (!check.Encode().AsSpan().SequenceEqual(Root.Encode()))
            throw new SaveFormatException("Gearbox output verification failed.");
        return [new FileOutput(relativePath, output)];
    }
}

internal sealed class GearboxSnapshot(ProtoMessage root, InventoryIdentity identity, int highestLevel) : DocumentMemento
{
    internal ProtoMessage Root { get; } = root;
    internal InventoryIdentity Identity { get; } = identity;
    internal int HighestLevel { get; } = highestLevel;
    public override long SizeBytes => Estimate(Root) + Identity.Lists.Sum(p => 128 + p.Value.Sum(id => 64L + id.Length * 2));
    private static long Estimate(ProtoMessage message) => 256 + message.Encode().LongLength * 4 +
        message.Fields.Sum(f => 256L + (f.Message is { } child ? Estimate(child) : 0));
}
