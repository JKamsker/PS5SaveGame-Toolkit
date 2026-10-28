using System.Text.Json;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Saves;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.GenericJson;

internal sealed class JsonSaveDocument(ISaveFile file, JsonTokenTree tree) : BoundSaveDocument, IQueryableDocument, IActionPlanningDocument
{
    private const int PageSize = 128;
    private readonly object _snapshotOwner = new();
    private readonly Dictionary<string, ChildRange> _ranges = new(StringComparer.Ordinal);
    private JsonTokenTree _tree = tree;

    public override string Title => file.FileName;
    public IReadOnlyList<QueryableCollection> Collections => [JsonSearch.Metadata(_tree.Nodes.Count)];

    public QueryableCollectionPage Query(string collectionId, QueryableCollectionQuery query) =>
        collectionId == JsonSearch.CollectionId ? JsonSearch.Query(_tree.Nodes[0], query, ScalarField)
            : throw new KeyNotFoundException("Unknown JSON collection.");

    public override ViewNode BuildView()
    {
        var overlay = JsonOverlay.Match(file, _tree);
        var tabs = new List<Tab>
        {
            new("Overview", [
                new StatsNode([new Stat("Imported bytes", file.Length.ToString()), new Stat("Values", _tree.Nodes.Count.ToString())]),
                new TextNode("Generic JSON workbench. Raw edits and explicit scalar type choices do not establish game compatibility.") { Tone = Tone.Warning },
                new TextNode("The Changes panel compares all exposed fields with the original, including collapsed content. Child-order fields describe structure; use undo for additions, removals and reordering. Framework resource limits can make review incomplete."),
                new TextNode("Current host limitation: the Changes panel may display typed scalar values as [object Object]. Inspect the JSON field or download for exact values; action preview labels include exact JSON transitions.") { Tone = Tone.Warning },
                JsonResetActions.View(),
            ]),
            new("JSON", [NodeView(_tree.Nodes[0])]),
            new("Search", [new QueryableTableNode(JsonSearch.CollectionId, "Whole-tree search", JsonSearch.Columns, _tree.Nodes.Count)
            {
                Description = JsonSearch.Metadata(_tree.Nodes.Count).Description,
            }]),
        };
        if (overlay is not null)
            tabs.Add(new(overlay.Title, [new TextNode(overlay.Evidence) { Tone = Tone.Warning }, new TextNode(JsonOverlay.Provenance),
                .. overlay.Groups.Select(OverlayNode)]));
        return new TabsNode(tabs);
    }

    public override IReadOnlyList<ViewNode> ExpandNode(string lazyId)
    {
        if (!_ranges.TryGetValue(lazyId, out var range)) throw new KeyNotFoundException("Unknown or retired JSON section.");
        if (range.Count <= PageSize)
            return range.Parent.Children.Skip(range.Start).Take(range.Count)
                .Select(n => range.Overlay ? OverlayNode(n) : NodeView(n)).ToArray();
        var children = new List<ViewNode>();
        var chunk = Math.Max(PageSize, (range.Count + PageSize - 1) / PageSize);
        for (var offset = 0; offset < range.Count; offset += chunk)
        {
            var count = Math.Min(chunk, range.Count - offset);
            var start = range.Start + offset;
            children.Add(LazyRange($"Entries {start}–{start + count - 1}", range.Parent, start, count, range.Overlay));
        }
        return children;
    }

    public override IReadOnlyList<FileOutput> Serialize() => [new(file.RelativePath, JsonTreeWriter.Write(_tree))];

    public override EditOutcome ApplyEdits(IReadOnlyList<ValueEdit> edits)
    {
        // Core can replay an older value step after structural undo without reloading its search page.
        // Resolve only live scalar identities; deleted IDs never acquire a binding to another node.
        foreach (var edit in edits)
            if (_tree.FindValue(edit.ValueId) is { IsContainer: false } node)
                _ = ScalarField(node, node.Label);
        var outcome = base.ApplyEdits(edits);
        if (!outcome.Succeeded || outcome.Changes.Count == 0) return outcome;
        try { _ = JsonTreeWriter.Write(_tree); }
        catch (SaveFormatException)
        {
            _ = base.ApplyEdits(outcome.Changes.Reverse().Select(c => new ValueEdit(c.ValueId, c.OldValue)).ToArray());
            return EditOutcome.Rejected(edits[0].ValueId, "Edit exceeds JSON limits or failed token round-trip validation.");
        }
        return outcome with { StructureChanged = outcome.Changes.Any(c => JsonScalar.TypeOf(c.OldValue) != JsonScalar.TypeOf(c.NewValue)) };
    }

    public ActionPlan PlanAction(string actionId, IReadOnlyDictionary<string, FieldValue> parameters, ActionScope scope) =>
        actionId == JsonResetActions.ActionId ? JsonResetActions.Plan(_tree, scope) : JsonArrayActions.Plan(_tree, actionId, parameters, scope);

    public EditOutcome ApplyActionPlan(string actionId, IReadOnlyDictionary<string, FieldValue> parameters, ActionScope scope, ActionPlan plan)
    {
        var fresh = PlanAction(actionId, parameters, scope);
        if (fresh.Errors.Count > 0) return new() { Errors = fresh.Errors };
        if (!fresh.Changes.SequenceEqual(plan.Changes)) return EditOutcome.Rejected(actionId, "The preview no longer matches this document.");
        if (actionId == JsonResetActions.ActionId)
            return ApplyEdits(fresh.Changes.Select(c => new ValueEdit(c.ValueId, c.NewValue)).ToArray());
        var result = Structural(() =>
        {
            JsonArrayActions.Mutate(_tree, actionId, parameters);
            _ = JsonTreeWriter.Write(_tree);
            _ranges.Clear();
        });
        return new() { Revert = result.Revert, StructureChanged = true, Warnings = fresh.Warnings };
    }

    protected override DocumentMemento CaptureSnapshot() => DocumentMemento.Snapshot(new Snapshot(_snapshotOwner, _tree.Clone()), _tree.EstimateBytes());

    protected override void LoadSnapshot(DocumentMemento snapshot)
    {
        if (snapshot is not SnapshotMemento<Snapshot> saved || !ReferenceEquals(saved.State.Owner, _snapshotOwner))
            throw new ArgumentException("Snapshot belongs to a different JSON document.");
        var restored = saved.State.Tree.Clone();
        // IDs are never recycled after undo followed by a different edit. Redo restores its saved IDs.
        restored.NextId = Math.Max(restored.NextId, _tree.NextId);
        _tree = restored;
        _ranges.Clear();
    }

    private ViewNode NodeView(JsonValueToken node)
    {
        if (!node.IsContainer) return ScalarField(node, node.Label);
        return new SectionNode(node.Label, [
            new SectionNode("Structure review", [Field(JsonArrayActions.StructureId(node), "Child sequence", new TextSpec(), () => JsonArrayActions.StructureValue(node)) with
            {
                Description = "Tracks membership and order for Changes review, including entries beyond the displayed prefix. Use undo to restore structure.",
                Confidence = Confidence.Verified,
            }]) { Collapsed = true },
            LazyRange("Entries", node, 0, node.Children.Count),
            .. node.Kind == JsonTokenType.StartArray ? JsonArrayActions.View(node) : Array.Empty<ActionNode>(),
        ]) { Description = JsonPaths.ForNode(node) };
    }

    private FieldNode ScalarField(JsonValueToken node, string label)
    {
        var spec = node.Spec ?? throw new InvalidOperationException("Expected a JSON scalar.");
        return Field(node.ValueId, label, spec, () => node.Current,
            spec.ReadOnly ? null : value => node.Current = value, node.Validate) with
        {
            Confidence = Confidence.Unknown,
            Warning = node.Warning ?? "Game-specific constraints and dependencies are unknown.",
            Description = $"{JsonPaths.ForNode(node)}; session identity {node.ValueId}. Source UTF-8 token at 0x{node.Offset:X}. "
                + "Explicit type changes affect JSON syntax only; game compatibility is unknown.",
        };
    }

    private SectionNode LazyRange(string label, JsonValueToken parent, int start, int count, bool overlay = false)
    {
        var id = $"{(overlay ? "overlay" : "children")}/{parent.Index}/{start}/{count}";
        _ranges[id] = new(parent, start, count, overlay);
        return SectionNode.Lazy(label, id, count.ToString());
    }

    private ViewNode OverlayNode(JsonValueToken node)
    {
        if (!node.IsContainer) return new TextNode($"{node.Label}: {node.Current}");
        var groupName = node.Children.FirstOrDefault(n => n.Label == "group_name")?.Current.ToString();
        return LazyRange(groupName ?? node.Label, node, 0, node.Children.Count, overlay: true) with { Description = JsonOverlay.Provenance };
    }

    private sealed record ChildRange(JsonValueToken Parent, int Start, int Count, bool Overlay);
    private sealed record Snapshot(object Owner, JsonTokenTree Tree);
}
