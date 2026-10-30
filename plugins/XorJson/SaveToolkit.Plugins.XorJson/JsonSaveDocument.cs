using System.Globalization;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.XorJson;

internal sealed class JsonSaveDocument : BoundSaveDocument, IQueryableDocument, IActionPlanningDocument,
    IFieldMetadataDocument
{
    private const int PageSize = 128;
    private readonly string _name;
    private readonly string _path;
    private readonly JsonEncoding _encoding;
    private readonly IReferenceContext _references;
    private readonly CharacterProfile? _character;
    private readonly JsonEditModel _model;
    internal const string WriteWarning = "Experimental write: no checksum or console reload has been verified. The game may reject, repair, or clamp this save; keep a backup and validate on a spare slot.";
    private IReadOnlyList<CharacterCollection>? _collections;
    private readonly JsonActionPlanner _planner;
    private IReadOnlyList<CharacterCollection> Projections => _collections ??= JsonCollections.Build(_model.Nodes, _references, _character is not null);
    private readonly Dictionary<string, ChildRange> _ranges = new(StringComparer.Ordinal);
    private readonly Dictionary<JsonValueToken, InventoryIdentity> _inventoryIdentities = [];

    public JsonSaveDocument(string name, string path, JsonTokenTree tree, JsonEncoding encoding, IReferenceContext references)
    {
        _name = name;
        _path = path;
        _encoding = encoding;
        _references = references;
        _character = CharacterProfile.Read(tree);
        _model = new(tree);
        _planner = new JsonActionPlanner(_model, references, () => CanEdit);
    }

    public override string Title => _name;
    public override IReadOnlyList<FieldMetadata> ResolveFieldMetadata(IReadOnlyList<string> valueIds)
    {
        JsonMetadataRequest.Validate(valueIds);
        foreach (var valueId in valueIds.Distinct(StringComparer.Ordinal))
        {
            if (!JsonActions.TryFind(_model, valueId, out var node) || node.IsContainer) continue;
            var binding = JsonScalarBindingPolicy.Create(node, _references, CanEdit, WriteWarningForDocument);
            var spec = FieldSpecMetadata.Snapshot(binding.Spec);
            var index = node.Index;
            RegisterField(new FieldMetadata(valueId, node.Label, spec)
            {
                BindingKind = FieldBindingKind.Primary,
                CanonicalValueId = valueId,
                Presentation = binding.Presentation,
            },
            () => _model.Find(index).Current,
            CanEdit && !spec.ReadOnly ? value => _model.Find(index).Current = value : null,
            binding.Validate);
        }

        return base.ResolveFieldMetadata(valueIds);
    }

    public override AssetContent? GetAsset(string assetId) => _references.GetAsset(assetId);
    public override bool CanEdit => _character is null || _character.CanWrite;
    internal bool IsCharacter => _character is not null;
    internal bool IsGlobalSettings => _model.Root.Member("blobs") is { Kind: System.Text.Json.JsonTokenType.StartArray };
    internal DocumentMemento CaptureDocumentMemento() => CaptureSnapshot()!;
    public IReadOnlyList<QueryableCollection> Collections => Projections.Select(collection => collection.Metadata).ToArray();

    public QueryableCollectionPage Query(string collectionId, QueryableCollectionQuery query) =>
        (Projections.SingleOrDefault(collection => collection.Metadata.Id == collectionId)
            ?? throw new KeyNotFoundException("Unknown character collection.")).Query(query, NodeView);

    public override ViewNode BuildView()
    {
        _ranges.Clear();
        var overview = new List<ViewNode>
        {
            new StatsNode([new Stat("Values", _model.Nodes.Count.ToString(CultureInfo.InvariantCulture))]),
            new TextNode(_encoding.Label),
            new TextNode(_character?.Warning ?? "Experimental raw settings edits. Game-specific constraints and console acceptance are unknown.") { Tone = Tone.Warning },
        };
        overview.Add(new ActionNode("json/inspect", "Inspect format and integrity") { Mutates = false });
        if (CanEdit) overview.AddRange(_planner.Actions(_character is not null));
        var tabs = new List<Tab>
        {
            new("Overview", overview),
        };
        if (_character is null) tabs.Add(new("JSON", [NodeView(_model.Root)]));
        else tabs.AddRange(CharacterViews.Build(_model.Root, NodeView, Projections.Where(c => c.TabTitle != "Raw").ToArray()));
        if (_character is not null) tabs[3] = tabs[3] with { Children = [.. tabs[3].Children, CharacterSpatialViews.Quests(_model.Root, _references, NodeView)] };
        if (_character is not null) tabs[4] = tabs[4] with { Children = [.. tabs[4].Children, CharacterMapView.Build(_model.Root, _references, NodeView)] };
        return new TabsNode(tabs);
    }

    public override IReadOnlyList<ViewNode> ExpandNode(string lazyId)
    {
        if (!_ranges.TryGetValue(lazyId, out var range)) throw new KeyNotFoundException("Unknown or retired JSON section.");
        if (range.Count <= PageSize)
        {
            var children = range.Parent.Children.Skip(range.Start).Take(range.Count).Select(NodeView).ToList();
            if (range.Start == 0 && range.Count == range.Parent.Children.Count &&
                Projections.FirstOrDefault(c => c.Metadata.Id == JsonCollections.Id(range.Parent)) is { } queryable)
                children.Insert(0, queryable.View());
            if (range.Start == 0 && range.Count == range.Parent.Children.Count)
                return [new SectionNode("Structural actions", StructureActions(range.Parent).ToArray()), new SectionNode("Values", children)];
            return children;
        }
        var pages = new List<ViewNode>();
        pages.AddRange(StructureActions(range.Parent));
        if (range.Start == 0 && range.Count == range.Parent.Children.Count &&
            Projections.FirstOrDefault(c => c.Metadata.Id == JsonCollections.Id(range.Parent)) is { } paged)
            pages.Add(paged.View());
        // Reserve room for the bounded action controls as well as pages.
        var pageSlots = PageSize - pages.Count;
        var chunk = Math.Max(PageSize, (range.Count + pageSlots - 1) / pageSlots);
        for (var offset = 0; offset < range.Count; offset += chunk)
        {
            var count = Math.Min(chunk, range.Count - offset);
            var start = range.Start + offset;
            pages.Add(LazyRange($"Entries {start}–{start + count - 1}", range.Parent, start, count));
        }
        return pages;
    }

    public override IReadOnlyList<FileOutput> Serialize()
    {
        var plain = JsonTokenWriter.Write(_model.Root);
        var result = _encoding.Transform(plain, false);
        var decoded = JsonEncoding.Decode(result);
        if (_character is not null && CharacterProfile.Read(decoded.Tree) != _character)
            throw new SaveFormatException("Character format, version and offline mode must remain unchanged.");
        var expected = JsonEditModel.Walk(_model.Root).ToArray();
        if (decoded.Encoding != _encoding || decoded.Tree.Nodes.Count != expected.Length)
            throw new SaveFormatException("Edited JSON changed its encoding or structure unexpectedly.");
        for (var i = 0; i < expected.Length; i++)
        {
            var before = expected[i];
            var after = decoded.Tree.Nodes[i];
            if (before.Label != after.Label || before.IsContainer != after.IsContainer ||
                (before.IsContainer ? before.Kind != after.Kind : before.Scalar != after.Scalar))
                throw new SaveFormatException("Edited JSON failed value round-trip validation.");
        }
        return [new FileOutput(_path, result)];
    }

    public override EditOutcome ApplyEdits(IReadOnlyList<ValueEdit> edits)
    {
        if (!CanEdit) return EditOutcome.Rejected("document", "Writes are disabled for this character version or online mode.");
        foreach (var edit in edits)
        {
            if (!JsonActions.TryFind(_model, edit.ValueId, out var node) || node.IsContainer)
                return EditOutcome.Rejected(edit.ValueId, "Unknown or retired scalar value.");
            _ = NodeView(node); // Register primary bindings even for unloaded F8 rows and history replay.
        }
        var outcome = base.ApplyEdits(edits);
        if (!outcome.Succeeded || edits.Count == 0) return outcome;
        try
        {
            _ = Serialize();
            InvalidateProjections();
            return outcome with { Warnings = [WriteWarning], StructureChanged = outcome.Changes.Any(c => c.OldValue != c.NewValue) };
        }
        catch (Exception ex) when (ex is SaveFormatException or ArgumentException)
        {
            // Scalar rollback keeps the same objects/bindings and their original escape spellings.
            _ = base.ApplyEdits(outcome.Changes.Reverse().Select(c => new ValueEdit(c.ValueId, c.OldValue)).ToArray());
            return EditOutcome.Rejected(edits[0].ValueId, ex.Message);
        }
    }

    public override ActionOutcome InvokeAction(string actionId, IReadOnlyDictionary<string, FieldValue> parameters)
    {
        if (actionId == "json/inspect")
            return new ActionOutcome { Message = $"{_encoding.Label}; {_model.Nodes.Count} values. No checksum is observed in the JSON; external integrity remains unknown.", Warnings = [WriteWarning] };
        if (JsonActions.IsPlanned(actionId))
            throw new InvalidOperationException("Bulk writes require an F6 preview and apply.");
        if (!CanEdit) throw new SaveFormatException("Writes are disabled for this character version or online mode.");
        var parts = actionId.Split('/');
        if (parts.Length != 3 || parts[0] != "json" || parts[1] is not ("add" or "remove" or "replace") ||
            !int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var id))
            throw new KeyNotFoundException("Unknown JSON action.");
        string? Parameter(string name) => parameters.TryGetValue(name, out var value) && value.Kind == FieldValueKind.Text ? value.AsString() : null;
        return Structural(() =>
        {
            _model.Change(parts[1], id, Parameter("json"), Parameter("key"));
            _ = Serialize(); // A transaction must also retain an unambiguous encoding and round-trip values.
            InvalidateProjections();
        }, "JSON structure changed. Review the export before use.") with { Warnings = [WriteWarning] };
    }

    public ActionPlan PlanAction(string actionId, IReadOnlyDictionary<string, FieldValue> parameters, ActionScope scope) =>
        _planner.Plan(actionId, parameters, scope);

    public EditOutcome ApplyActionPlan(string actionId, IReadOnlyDictionary<string, FieldValue> parameters, ActionScope scope, ActionPlan plan)
    {
        var fresh = PlanAction(actionId, parameters, scope);
        if (fresh.Errors.Count > 0) return new EditOutcome { Errors = fresh.Errors };
        if (!fresh.Changes.SequenceEqual(plan.Changes) || !fresh.SkippedRows.SequenceEqual(plan.SkippedRows) || !fresh.Warnings.SequenceEqual(plan.Warnings))
            return EditOutcome.Rejected(actionId, "The planned values are stale; preview the action again.");
        var result = ApplyEdits(fresh.Changes.Select(c => new ValueEdit(c.ValueId, c.NewValue)).ToArray());
        return result with { Warnings = [.. result.Warnings, .. fresh.Warnings] };
    }

    protected override DocumentMemento CaptureSnapshot()
    {
        var state = _model.Snapshot();
        return DocumentMemento.Snapshot(state, state.SizeBytes);
    }

    protected override void LoadSnapshot(DocumentMemento snapshot)
    {
        _model.Restore(((SnapshotMemento<JsonModelSnapshot>)snapshot).State);
        _ranges.Clear();
        InvalidateProjections();
    }

    private void InvalidateProjections()
    {
        _collections = null;
        _inventoryIdentities.Clear();
    }

    private string InventoryDescription(JsonValueToken node)
    {
        var parent = node.Parent!;
        if (!_inventoryIdentities.TryGetValue(parent, out var identities))
            _inventoryIdentities.Add(parent, identities = new InventoryIdentity(parent.Children));
        return identities.Describe(node);
    }

    private ViewNode NodeView(JsonValueToken node)
    {
        if (node.IsContainer)
        {
            var label = node.Member("name")?.Scalar is { Kind: FieldValueKind.Text } blobName ? blobName.AsString() : node.Label;
            var section = LazyRange(label, node, 0, node.Children.Count);
            return _character is not null && node.Parent?.Label == "Entries" && node.Parent.Parent?.Label == "Inventory"
                ? section with { Description = InventoryDescription(node) } : section;
        }
        var binding = JsonScalarBindingPolicy.Create(node, _references, CanEdit, WriteWarningForDocument);
        var field = Field(node.ValueId, node.Label, binding.Spec, () => _model.Find(node.Index).Current,
            CanEdit && !binding.Spec.ReadOnly ? value => _model.Find(node.Index).Current = value : null,
            binding.Validate) with
        {
            Confidence = binding.Presentation.Confidence,
            Warning = binding.Presentation.Warning,
            Description = binding.Presentation.Description,
        };
        // Character collection cells remain direct fields so F8 tables can edit them without
        // expanding a per-cell wrapper. Raw container expansion still exposes F5 actions.
        return _character is null ? new SectionNode(node.Label, [field, .. StructureActions(node)]) : field;
    }

    private IEnumerable<ActionNode> StructureActions(JsonValueToken node)
    {
        const string warning = "Advanced raw JSON operation. Unknown game dependencies may make the save invalid.";
        var json = new FieldNode("json", "Complete JSON value", new TextSpec { Multiline = true, MaxLength = JsonTokenTree.MaxBytes }, FieldValue.Of("null"));
        if (node.IsContainer)
            yield return new ActionNode($"json/add/{node.Index}", "Add row/member")
            {
                Description = WriteWarning,
                Confirm = warning,
                Parameters = node.Kind == System.Text.Json.JsonTokenType.StartObject
                    ? [new FieldNode("key", "New member name", new TextSpec(), FieldValue.Of("")), json] : [json],
            };
        if (node.Parent is null) yield break;
        yield return new ActionNode($"json/remove/{node.Index}", "Remove row/member") { Description = WriteWarning, Confirm = warning };
        yield return new ActionNode($"json/replace/{node.Index}", "Replace row/member") { Description = WriteWarning, Confirm = warning, Parameters = [json] };
    }

    private SectionNode LazyRange(string label, JsonValueToken parent, int start, int count)
    {
        var id = start == 0 && count == parent.Children.Count
            ? $"children/{parent.Index}" : $"children/{parent.Index}/page/{start}/{count}";
        _ranges[id] = new(parent, start, count);
        return SectionNode.Lazy(label, id, count.ToString(CultureInfo.InvariantCulture));
    }

    private string WriteWarningForDocument => _character is not null ? CharacterWriteWarning : WriteWarning;

    private const string CharacterWriteWarning = "Experimental character write. Not validated on console. " +
        "The FCharacterSaveV1 storage layout is observed; gameplay meanings and dependencies remain unverified. " +
        "The game may reject, repair or clamp the save. No checksum or dependency behavior is known.";

    private sealed record ChildRange(JsonValueToken Parent, int Start, int Count);
}
