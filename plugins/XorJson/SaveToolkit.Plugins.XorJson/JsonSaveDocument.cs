using System.Globalization;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.XorJson;

internal sealed class JsonSaveDocument : BoundSaveDocument, IQueryableDocument, IActionPlanningDocument
{
    private const int PageSize = 128;
    private readonly string _name;
    private readonly string _path;
    private readonly JsonEncoding _encoding;
    private readonly IReferenceContext _references;
    private readonly CharacterProfile? _character;
    private readonly JsonEditModel _model;
    private readonly bool _allowStructure;
    private const string BulkActionId = "json/bulk-set";
    internal const string WriteWarning = "Experimental write: no checksum or console reload has been verified. The game may reject, repair, or clamp this save; keep a backup and validate on a spare slot.";
    private readonly IReadOnlyList<CharacterCollection> _collections;
    private readonly Dictionary<string, ChildRange> _ranges = new(StringComparer.Ordinal);

    public JsonSaveDocument(string name, string path, JsonTokenTree tree, JsonEncoding encoding, IReferenceContext references)
    {
        _name = name;
        _path = path;
        _encoding = encoding;
        _references = references;
        _character = CharacterProfile.Read(tree);
        _model = new(tree);
        _allowStructure = true;
        _collections = _character is null ? [] : CharacterCollections.Build(_model.Root, references);
    }

    public override string Title => _name;
    public override bool CanEdit => _character is null || _character.CanWrite;
    internal bool IsCharacter => _character is not null;
    internal bool IsGlobalSettings => _model.Root.Member("blobs") is { Kind: System.Text.Json.JsonTokenType.StartArray };
    internal DocumentMemento CaptureDocumentMemento() => CaptureSnapshot()!;
    public IReadOnlyList<QueryableCollection> Collections => _collections.Select(collection => collection.Metadata).ToArray();

    public QueryableCollectionPage Query(string collectionId, QueryableCollectionQuery query) =>
        (_collections.SingleOrDefault(collection => collection.Metadata.Id == collectionId)
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
        if (CanEdit) overview.Add(BulkAction());
        var tabs = new List<Tab>
        {
            new("Overview", overview),
        };
        if (_character is null) tabs.Add(new("JSON", [NodeView(_model.Root)]));
        else tabs.AddRange(CharacterViews.Build(_model.Root, NodeView, _collections));
        return new TabsNode(tabs);
    }

    public override IReadOnlyList<ViewNode> ExpandNode(string lazyId)
    {
        if (!_ranges.TryGetValue(lazyId, out var range)) throw new KeyNotFoundException("Unknown or retired JSON section.");
        if (range.Count <= PageSize)
        {
            var children = range.Parent.Children.Skip(range.Start).Take(range.Count).Select(NodeView).ToList();
            if (_allowStructure && range.Start == 0 && range.Count == range.Parent.Children.Count)
                return [new SectionNode("Structural actions", StructureActions(range.Parent).ToArray()), new SectionNode("Values", children)];
            return children;
        }
        var pages = new List<ViewNode>();
        if (_allowStructure) pages.AddRange(StructureActions(range.Parent));
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
        var expected = _model.Nodes;
        if (decoded.Encoding != _encoding || decoded.Tree.Nodes.Count != expected.Count)
            throw new SaveFormatException("Edited JSON changed its encoding or structure unexpectedly.");
        for (var i = 0; i < expected.Count; i++)
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
        var outcome = base.ApplyEdits(edits);
        if (!outcome.Succeeded || edits.Count == 0) return outcome;
        try
        {
            _ = Serialize();
            var changes = outcome.Changes.ToLookup(change => change.ValueId);
            // Catalog replacements change displayed provenance/specs; Core must refresh them on edit and history replay.
            return outcome with { Warnings = [WriteWarning], StructureChanged = _model.Nodes.Any(node => changes[node.ValueId].Any(change =>
                change.OldValue != change.NewValue && (CharacterViews.CatalogFor(node, change.OldValue) is not null ||
                    CharacterViews.CatalogFor(node, change.NewValue) is not null))) };
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
        if (actionId == BulkActionId)
            throw new InvalidOperationException("Bulk writes require an F6 preview and apply.");
        if (!CanEdit) throw new SaveFormatException("Writes are disabled for this character version or online mode.");
        if (!_allowStructure) throw new SaveFormatException("Structural writes are disabled for this document.");
        var parts = actionId.Split('/');
        if (parts.Length != 3 || parts[0] != "json" || parts[1] is not ("add" or "remove" or "replace") ||
            !int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var id))
            throw new KeyNotFoundException("Unknown JSON action.");
        string? Parameter(string name) => parameters.TryGetValue(name, out var value) && value.Kind == FieldValueKind.Text ? value.AsString() : null;
        return Structural(() =>
        {
            _model.Change(parts[1], id, Parameter("json"), Parameter("key"));
            _ = Serialize(); // A transaction must also retain an unambiguous encoding and round-trip values.
        }, "JSON structure changed. Review the export before use.") with { Warnings = [WriteWarning] };
    }

    public ActionPlan PlanAction(string actionId, IReadOnlyDictionary<string, FieldValue> parameters, ActionScope scope)
    {
        if (actionId != BulkActionId) return new ActionPlan { Errors = [new FieldError(actionId, "Unknown planned action.")] };
        if (!CanEdit) return new ActionPlan { Errors = [new FieldError(actionId, "This document is read-only for its character version or online mode.")] };
        var value = ParseScalar(parameters);
        if (value.Error is not null) return new ActionPlan { Errors = [new FieldError("value", value.Error)] };
        var scalar = value.Value!.Value;
        var ids = scope.RowKeys ?? _model.Nodes.Where(n => !n.IsContainer).Select(n => n.ValueId).ToArray();
        var changes = new List<PlannedChange>();
        var skipped = new List<SkippedActionRow>();
        foreach (var id in ids)
        {
            if (!TryFindValue(id, out var node))
            {
                skipped.Add(new(id, "Unknown or non-scalar value."));
                continue;
            }
            var error = node.Validate(scalar);
            if (error is not null)
            {
                skipped.Add(new(id, error));
                continue;
            }
            changes.Add(new PlannedChange(id, node.TypedPath, node.Current, scalar));
        }
        return new ActionPlan
        {
            Changes = changes,
            SkippedRows = skipped,
            Warnings = [WriteWarning, "Bulk values are applied only to selected scalar tokens; cross-field game rules are unknown."],
        };
    }

    public EditOutcome ApplyActionPlan(string actionId, IReadOnlyDictionary<string, FieldValue> parameters, ActionScope scope, ActionPlan plan)
    {
        var fresh = PlanAction(actionId, parameters, scope);
        if (fresh.Errors.Count > 0) return new EditOutcome { Errors = fresh.Errors };
        if (fresh.Changes.Count != plan.Changes.Count || fresh.Changes.Select((change, index) => change != plan.Changes[index]).Any(mismatch => mismatch))
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
    }

    private ViewNode NodeView(JsonValueToken node)
    {
        if (node.IsContainer)
        {
            var label = node.Member("name")?.Scalar is { Kind: FieldValueKind.Text } blobName ? blobName.AsString() : node.Label;
            var section = LazyRange(label, node, 0, node.Children.Count);
            return _character is not null && node.Parent?.Label == "Entries" && node.Parent.Parent?.Label == "Inventory"
                ? section with { Description = InventoryIdentity.Describe(node) } : section;
        }
        var spec = node.Spec!;
        ReferenceRecord? record = null;
        var catalogId = CharacterViews.CatalogFor(node);
        if (catalogId is not null && node.Scalar.Kind == FieldValueKind.Text)
        {
            spec = new ReferenceSpec(catalogId) { AllowCustom = false, ReadOnly = !CanEdit };
            record = _references.Catalog(catalogId)?.Find(node.Scalar.AsString());
        }
        var field = Field(node.ValueId, node.Label, spec, () => node.Current,
            CanEdit && !spec.ReadOnly ? value => node.Current = value : null, node.Validate) with
        {
            Confidence = record is null ? Confidence.Unknown : Confidence.Inferred,
            Warning = node.Warning ?? (_character is not null ? CharacterWriteWarning : WriteWarning),
            Description = $"{node.TypedPath}; original UTF-8 token at 0x{node.Offset:X}." +
                (record is null ? "" : $" {record.Name} ({record.Category}). {record.AttributionText}. PC catalog; PS5 semantics unverified."),
        };
        // Character collection cells remain direct fields so F8 tables can edit them without
        // expanding a per-cell wrapper. Raw container expansion still exposes F5 actions.
        return _allowStructure && _character is null ? new SectionNode(node.Label, [field, .. StructureActions(node)]) : field;
    }

    private IEnumerable<ActionNode> StructureActions(JsonValueToken node)
    {
        const string warning = "Advanced raw JSON operation. Unknown game dependencies may make the save invalid.";
        var json = new FieldNode("json", "Complete JSON value", new TextSpec { Multiline = true, MaxLength = JsonTokenTree.MaxBytes }, FieldValue.Of("null"));
        if (node.IsContainer)
            yield return new ActionNode($"json/add/{node.Index}", "Add row/member")
            {
                Confirm = warning,
                Parameters = node.Kind == System.Text.Json.JsonTokenType.StartObject
                    ? [new FieldNode("key", "New member name", new TextSpec(), FieldValue.Of("")), json] : [json],
            };
        if (node.Parent is null) yield break;
        yield return new ActionNode($"json/remove/{node.Index}", "Remove row/member") { Confirm = warning };
        yield return new ActionNode($"json/replace/{node.Index}", "Replace row/member") { Confirm = warning, Parameters = [json] };
    }

    private SectionNode LazyRange(string label, JsonValueToken parent, int start, int count)
    {
        var id = start == 0 && count == parent.Children.Count
            ? $"children/{parent.Index}" : $"children/{parent.Index}/page/{start}/{count}";
        _ranges[id] = new(parent, start, count);
        return SectionNode.Lazy(label, id, count.ToString(CultureInfo.InvariantCulture));
    }

    private ActionNode BulkAction() => new(BulkActionId, "Set selected scalar values")
    {
        SupportsPlanning = true,
        Tone = Tone.Warning,
        Confirm = WriteWarning,
        Description = "F6 preview/apply is required. Select value IDs in the raw JSON view or apply to the whole document; incompatible and non-scalar rows are skipped.",
        Parameters = [new FieldNode("value", "JSON scalar", new TextSpec { MaxLength = JsonTokenTree.MaxBytes }, FieldValue.Of("null"))],
    };

    private static string CharacterWriteWarning => "Experimental character write. Field meaning is inferred from the PS5 FCharacterSaveV1 sample and has not been validated on console; no checksum or dependency behavior is known.";

    private (FieldValue? Value, string? Error) ParseScalar(IReadOnlyDictionary<string, FieldValue> parameters)
    {
        if (!parameters.TryGetValue("value", out var raw) || raw.Kind != FieldValueKind.Text)
            return (null, "Provide one JSON scalar literal in the value parameter.");
        try
        {
            var tree = JsonTokenTree.Parse(System.Text.Encoding.UTF8.GetBytes(raw.AsString()));
            var node = tree.Nodes[0];
            return node.IsContainer ? (null, "Bulk writes accept one scalar JSON literal, not an object or array.") : (node.Current, null);
        }
        catch (SaveFormatException) { return (null, "The value parameter is not a valid JSON scalar literal."); }
    }

    private bool TryFindValue(string id, out JsonValueToken node)
    {
        node = null!;
        if (!id.StartsWith("value/", StringComparison.Ordinal) || !int.TryParse(id[6..], NumberStyles.None, CultureInfo.InvariantCulture, out var index))
            return false;
        try { node = _model.Find(index); return !node.IsContainer; }
        catch (KeyNotFoundException) { return false; }
    }

    private sealed record ChildRange(JsonValueToken Parent, int Start, int Count);
}
