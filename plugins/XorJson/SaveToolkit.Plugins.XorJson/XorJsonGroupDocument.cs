using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.XorJson;

internal sealed class XorJsonGroupDocument : IGroupedSaveDocument, IQueryableDocument, IActionPlanningDocument
{
    private const string GroupWarning = "Experimental character/global transaction. No shared game dependency or console reload has been verified; every member is staged and exported together, but the game may reject or repair the result.";
    private readonly IReferenceContext _references;
    private XorJsonGroupMember[] _members;

    public XorJsonGroupDocument(XorJsonPlugin plugin, DocumentGroupInput input, IReferenceContext references)
    {
        _references = references;
        _members = input.Members.OrderBy(member => member.Role, StringComparer.Ordinal)
            .Select(member => new XorJsonGroupMember(member.Role,
                (JsonSaveDocument)plugin.Open(member.File, references))).ToArray();
    }

    public string Title => "XorJson character + global";
    public bool CanEdit => _members.All(member => member.Document.CanEdit);
    public IReadOnlyList<QueryableCollection> Collections => _members
        .SelectMany(member => member.Document.Collections.Select(member.Collection)).ToArray();

    public ViewNode BuildView() => new SectionNode(Title,
    [
        new TextNode(GroupWarning) { Tone = Tone.Warning },
        .. _members.Select(member => (ViewNode)new SectionNode(member.Role, [member.Node(member.Document.BuildView())])),
    ]);

    public IReadOnlyList<ViewNode> ExpandNode(string lazyId)
    {
        var member = Member(lazyId);
        return member.Document.ExpandNode(member.Local(lazyId)).Select(node => member.Node(node)).ToArray();
    }

    public FieldValue GetValue(string valueId)
    {
        var member = Member(valueId);
        return member.Document.GetValue(member.Local(valueId));
    }

    public QueryableCollectionPage Query(string collectionId, QueryableCollectionQuery query)
    {
        var member = Member(collectionId);
        return member.Page(member.Document.Query(member.Local(collectionId), query));
    }

    public EditOutcome ApplyEdits(IReadOnlyList<ValueEdit> edits)
    {
        var before = CaptureMemento();
        var changes = new List<ValueChange>();
        var warnings = new List<string> { GroupWarning };
        try
        {
            foreach (var group in edits.GroupBy(edit => Member(edit.ValueId).Role, StringComparer.Ordinal))
            {
                var member = Member(group.Key + "/");
                var outcome = member.Document.ApplyEdits(group.Select(edit => new ValueEdit(member.Local(edit.ValueId), edit.Value)).ToArray());
                if (!outcome.Succeeded) throw new SaveFormatException(string.Join("; ", outcome.Errors.Select(error => error.Message)));
                changes.AddRange(outcome.Changes.Select(change => new ValueChange(member.Prefix(change.ValueId), change.OldValue, change.NewValue)));
                warnings.AddRange(outcome.Warnings);
            }
            _ = Serialize();
            return new EditOutcome { Changes = changes, Warnings = warnings, StructureChanged = changes.Count > 0, Revert = before };
        }
        catch (Exception ex) when (ex is SaveFormatException or InvalidOperationException or KeyNotFoundException)
        {
            _ = RestoreMemento(before);
            return new EditOutcome { Errors = [new FieldError("group", ex.Message)] };
        }
    }

    public ActionOutcome InvokeAction(string actionId, IReadOnlyDictionary<string, FieldValue> parameters)
    {
        var member = Member(actionId);
        if (member.Local(actionId) == "json/inspect")
            return member.Document.InvokeAction("json/inspect", parameters);
        var before = CaptureMemento();
        try
        {
            var outcome = member.Document.InvokeAction(member.Local(actionId), parameters);
            _ = Serialize();
            return outcome with
            {
                Changes = outcome.Changes.Select(change => new ValueChange(member.Prefix(change.ValueId), change.OldValue, change.NewValue)).ToArray(),
                Warnings = [GroupWarning, .. outcome.Warnings],
                Revert = before,
                StructureChanged = true,
            };
        }
        catch
        {
            _ = RestoreMemento(before);
            throw;
        }
    }

    public ActionPlan PlanAction(string actionId, IReadOnlyDictionary<string, FieldValue> parameters, ActionScope scope)
    {
        var member = Member(actionId);
        if (member.Document is not IActionPlanningDocument planner)
            return new ActionPlan { Errors = [new FieldError(actionId, "The selected member has no F6 planner.")] };
        var local = LocalScope(member, scope);
        var plan = planner.PlanAction(member.Local(actionId), parameters, local);
        return PrefixPlan(member, plan);
    }

    public EditOutcome ApplyActionPlan(string actionId, IReadOnlyDictionary<string, FieldValue> parameters, ActionScope scope, ActionPlan plan)
    {
        var member = Member(actionId);
        if (member.Document is not IActionPlanningDocument planner)
            return EditOutcome.Rejected(actionId, "The selected member has no F6 planner.");
        var before = CaptureMemento();
        try
        {
            var localPlan = UnprefixPlan(member, plan);
            var outcome = planner.ApplyActionPlan(member.Local(actionId), parameters, LocalScope(member, scope), localPlan);
            if (!outcome.Succeeded) return outcome;
            _ = Serialize();
            return outcome with
            {
                Changes = outcome.Changes.Select(change => new ValueChange(member.Prefix(change.ValueId), change.OldValue, change.NewValue)).ToArray(),
                Warnings = [GroupWarning, .. outcome.Warnings],
                Revert = before,
                StructureChanged = true,
            };
        }
        catch
        {
            _ = RestoreMemento(before);
            throw;
        }
    }

    public IReadOnlyList<FileOutput> Serialize() => _members.SelectMany(member => member.Document.Serialize()).ToArray();
    public AssetContent? GetAsset(string assetId) => _references.GetAsset(assetId);

    public ReadOnlyMemory<byte> ReadBlob(string blobId, long offset, int count)
    {
        var member = Member(blobId);
        return member.Document.ReadBlob(member.Local(blobId), offset, count);
    }

    public DocumentMemento CaptureMemento() => new XorJsonGroupSnapshot(_members.ToDictionary(
        member => member.Role, member => member.Document.CaptureDocumentMemento(), StringComparer.Ordinal));

    public DocumentMemento RestoreMemento(DocumentMemento memento)
    {
        if (memento is not XorJsonGroupSnapshot snapshot || !snapshot.HasRoles(_members.Select(member => member.Role)))
            throw new InvalidOperationException("The snapshot does not belong to this XorJson group.");
        var inverse = CaptureMemento();
        foreach (var member in _members) _ = member.Document.RestoreMemento(snapshot.For(member.Role));
        return inverse;
    }

    public void Dispose()
    {
        foreach (var member in _members) member.Dispose();
    }

    private XorJsonGroupMember Member(string id) => _members.SingleOrDefault(member => id.StartsWith(member.Role + "/", StringComparison.Ordinal))
        ?? throw new KeyNotFoundException("Unknown XorJson group member identity.");

    private static ActionScope LocalScope(XorJsonGroupMember member, ActionScope scope) => new(
        scope.RowKeys?.Select(member.Local).ToArray())
    {
        QueryScope = scope.QueryScope is { } query ? query with
        {
            CollectionId = member.Local(query.CollectionId),
            ExcludedRowKeys = query.ExcludedRowKeys.Select(member.Local).ToArray(),
        } : null,
    };

    private static ActionPlan PrefixPlan(XorJsonGroupMember member, ActionPlan plan) => plan with
    {
        Changes = plan.Changes.Select(change => change with { ValueId = member.Prefix(change.ValueId) }).ToArray(),
        SkippedRows = plan.SkippedRows.Select(row => row with { RowKey = member.Prefix(row.RowKey) }).ToArray(),
    };

    private static ActionPlan UnprefixPlan(XorJsonGroupMember member, ActionPlan plan) => plan with
    {
        Changes = plan.Changes.Select(change => change with { ValueId = member.Local(change.ValueId) }).ToArray(),
        SkippedRows = plan.SkippedRows.Select(row => row with { RowKey = member.Local(row.RowKey) }).ToArray(),
    };
}
