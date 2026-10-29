using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.UnrealGvas.Tests;

/// <summary>Test-only planner exercises host selection without introducing a production bulk action.</summary>
internal sealed class InventoryScopeDocument(ISaveDocument inner) : ISaveDocument, IQueryableDocument, IActionPlanningDocument
{
    public const string ActionId = "test/inventory-scope";
    public string Title => inner.Title;
    public bool CanEdit => inner.CanEdit;
    public IReadOnlyList<QueryableCollection> Collections => ((IQueryableDocument)inner).Collections;
    public ViewNode BuildView() => new SectionNode("Selection test", [inner.BuildView(),
        new ActionNode(ActionId, "Inspect test selection") { SupportsPlanning = true }]);
    public QueryableCollectionPage Query(string id, QueryableCollectionQuery query) => ((IQueryableDocument)inner).Query(id, query);
    public IReadOnlyList<ViewNode> ExpandNode(string id) => inner.ExpandNode(id);
    public FieldValue GetValue(string id) => inner.GetValue(id);
    public EditOutcome ApplyEdits(IReadOnlyList<ValueEdit> edits) => inner.ApplyEdits(edits);
    public ActionOutcome InvokeAction(string id, IReadOnlyDictionary<string, FieldValue> parameters) => inner.InvokeAction(id, parameters);
    public IReadOnlyList<FileOutput> Serialize() => inner.Serialize();
    public ActionPlan PlanAction(string id, IReadOnlyDictionary<string, FieldValue> parameters, ActionScope scope) => new();
    public EditOutcome ApplyActionPlan(string id, IReadOnlyDictionary<string, FieldValue> parameters, ActionScope scope, ActionPlan plan) => new();
    public void Dispose() => inner.Dispose();
}
