using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Core.Tests.SdkHelpers;

internal sealed class MemberAdapterDocument(string path = "fixture", int initialValue = 1) : ISaveDocument, IQueryableDocument, IActionPlanningDocument
{
    public const string ValueId = "value/~1/%2F 雪";
    public const string RowKey = "row/~0/a";
    public int Value { get; private set; } = initialValue;
    public bool CanEdit { get; init; } = true;
    public string Title => "Fixture";
    public bool Disposed { get; private set; }
    public ActionScope? LastScope { get; private set; }
    public ActionPlan? LastPlan { get; private set; }
    public IReadOnlyList<ValueEdit>? LastEdits { get; private set; }
    public IReadOnlyDictionary<string, FieldValue>? LastParameters { get; private set; }
    public QueryableCollectionQuery? LastQuery { get; private set; }
    public IReadOnlyList<QueryableCollection> Collections => [new("items", "Items", [new("n", "Number", IntegerSpec.Int32)], 1)];
    public ViewNode BuildView() => new FieldNode(ValueId, "Value", IntegerSpec.Int32, FieldValue.Of(Value));
    public FieldValue GetValue(string valueId) => valueId == ValueId ? FieldValue.Of(Value) : throw new KeyNotFoundException();
    public IReadOnlyList<ViewNode> ExpandNode(string lazyId) => lazyId == "lazy" ? [BuildView()] : throw new KeyNotFoundException();
    public ReadOnlyMemory<byte> ReadBlob(string blobId, long offset, int count) => blobId == "blob" ? new byte[] { 42 }.AsMemory((int)offset, count) : throw new KeyNotFoundException();
    public IReadOnlyList<FileOutput> Serialize() => [new(path, new byte[] { (byte)Value })];
    public EditOutcome ApplyEdits(IReadOnlyList<ValueEdit> edits)
    {
        LastEdits = edits;
        var invalid = edits.FirstOrDefault(e => e.ValueId != ValueId || e.Value.AsInt32() < 0);
        if (invalid is not null) return EditOutcome.Rejected(invalid.ValueId, "Rejected");
        var changes = new List<ValueChange>();
        foreach (var edit in edits)
        {
            changes.Add(new(edit.ValueId, FieldValue.Of(Value), edit.Value));
            Value = edit.Value.AsInt32();
        }
        return new() { Changes = changes, Warnings = ["member warning"] };
    }

    public ActionOutcome InvokeAction(string actionId, IReadOnlyDictionary<string, FieldValue> parameters)
    {
        if (actionId != "set") throw new KeyNotFoundException();
        LastParameters = parameters;
        var before = Capture();
        var outcome = ApplyEdits([new(ValueId, parameters["value"])]);
        return new() { Changes = outcome.Changes, Revert = before, Message = "Set", Warnings = ["action warning"], StructureChanged = true };
    }

    public QueryableCollectionPage Query(string collectionId, QueryableCollectionQuery query)
    {
        if (collectionId != "items") throw new KeyNotFoundException();
        LastQuery = query;
        return new(collectionId, Collections[0].Columns, [new(RowKey, new Dictionary<string, ViewNode> { ["n"] = BuildView() })], 1, 1, query.Offset, query.Limit)
        { NextCursor = "opaque/~%", Revision = 7, HiddenEmptyCount = 2 };
    }

    public ActionPlan PlanAction(string actionId, IReadOnlyDictionary<string, FieldValue> parameters, ActionScope scope)
    {
        if (actionId != "set") throw new KeyNotFoundException();
        LastParameters = parameters;
        LastScope = scope;
        return new()
        {
            Changes = [new(ValueId, "Value", FieldValue.Of(Value), parameters["value"]) { Derived = true, NewExists = false }],
            SkippedRows = [new(RowKey, "skip")], Warnings = ["plan warning"],
            Errors = parameters.ContainsKey("errors") ? [new(ValueId, "field"), new("", "general")] : [],
        };
    }

    public EditOutcome ApplyActionPlan(string actionId, IReadOnlyDictionary<string, FieldValue> parameters, ActionScope scope, ActionPlan plan)
    {
        _ = PlanAction(actionId, parameters, scope);
        LastPlan = plan;
        return ApplyEdits(plan.Changes.Select(c => new ValueEdit(c.ValueId, c.NewValue)).ToArray());
    }

    public DocumentMemento Capture() => DocumentMemento.Snapshot(Value, sizeof(int));
    public DocumentMemento RestoreMemento(DocumentMemento memento)
    {
        var inverse = Capture();
        Value = Assert.IsType<SnapshotMemento<int>>(memento).State;
        return inverse;
    }
    public void Dispose() => Disposed = true;
}
