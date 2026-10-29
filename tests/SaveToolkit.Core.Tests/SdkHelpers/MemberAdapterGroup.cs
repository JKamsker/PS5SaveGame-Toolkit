using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Core.Tests.SdkHelpers;

// Transaction/snapshot policy deliberately stays in the composed group, not the SDK adapter.
internal sealed class MemberAdapterGroup : IGroupedSaveDocument, IQueryableDocument, IActionPlanningDocument
{
    private readonly DocumentMemberAdapter[] _members;
    private readonly DocumentMemberRouter _router;
    public MemberAdapterGroup(DocumentGroupInput input)
    {
        _members = input.Members.Select(file => new DocumentMemberAdapter(file.Role,
            new MemberAdapterDocument(file.File.RelativePath, file.File.ReadAllBytes()[0]))).ToArray();
        _router = new(_members);
    }

    public string Title => "Adapter group";
    public bool CanEdit => true;
    public ViewNode BuildView() => new SectionNode(Title, _members.SelectMany(member => new ViewNode[]
    {
        member.Node(member.Document.BuildView()),
        member.Node(new ActionNode("set", "Set") { SupportsPlanning = true,
            Parameters = [new("value", "Value", IntegerSpec.Int32, FieldValue.Of(1))] }),
        new QueryableTableNode(member.Prefix("items"), "Items", member.Collections[0].Columns, 1),
    }).ToArray());
    public IReadOnlyList<QueryableCollection> Collections => _members.SelectMany(member => member.Collections).ToArray();
    public QueryableCollectionPage Query(string collectionId, QueryableCollectionQuery query) =>
        _router.Resolve(collectionId).Query(collectionId, query) with { HiddenEmptyCount = 0, NextCursor = null };
    public FieldValue GetValue(string valueId) => _router.Resolve(valueId).GetValue(valueId);
    public IReadOnlyList<FileOutput> Serialize() => _members.SelectMany(member => member.Document.Serialize()).ToArray();

    public EditOutcome ApplyEdits(IReadOnlyList<ValueEdit> edits) => Transact(() =>
    {
        var changes = new List<ValueChange>();
        foreach (var batch in _router.PartitionEdits(edits))
        {
            var outcome = batch.Member.ApplyEdits(batch.Edits);
            if (!outcome.Succeeded) return outcome;
            changes.AddRange(outcome.Changes);
        }
        return new() { Changes = changes };
    });

    public ActionPlan PlanAction(string actionId, IReadOnlyDictionary<string, FieldValue> parameters, ActionScope scope) =>
        _router.Resolve(actionId).PlanAction(actionId, parameters, scope);

    public EditOutcome ApplyActionPlan(string actionId, IReadOnlyDictionary<string, FieldValue> parameters, ActionScope scope, ActionPlan plan) =>
        Transact(() => _router.Resolve(actionId).ApplyActionPlan(actionId, parameters, scope, plan));

    private EditOutcome Transact(Func<EditOutcome> apply)
    {
        var before = CaptureMemento();
        try
        {
            var outcome = apply();
            if (!outcome.Succeeded) { _ = RestoreMemento(before); return outcome; }
            return outcome with { Revert = before, StructureChanged = true };
        }
        catch { _ = RestoreMemento(before); throw; }
    }

    public DocumentMemento CaptureMemento() => DocumentMemento.Snapshot(_members.ToDictionary(member => member.Role,
        member => ((MemberAdapterDocument)member.Document).Capture(), StringComparer.Ordinal), _members.Length * sizeof(int));

    public DocumentMemento RestoreMemento(DocumentMemento memento)
    {
        var states = Assert.IsType<SnapshotMemento<Dictionary<string, DocumentMemento>>>(memento).State;
        var inverse = CaptureMemento();
        foreach (var member in _members) _ = member.Document.RestoreMemento(states[member.Role]);
        return inverse;
    }

    public void Dispose()
    {
        foreach (var member in _members) member.Document.Dispose();
    }
}
