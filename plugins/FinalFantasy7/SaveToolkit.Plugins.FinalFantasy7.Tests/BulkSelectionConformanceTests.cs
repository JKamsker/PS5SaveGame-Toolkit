using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.FinalFantasy7.Tests;

public sealed class BulkSelectionConformanceTests
{
    [Theory]
    [MemberData(nameof(BulkSelectionPlanningTests.Cases), MemberType = typeof(BulkSelectionPlanningTests))]
    public void StagedBindingAdvisoryPassesOnlyWithMeaningfulKnownRowEffects(string title, string action)
    {
        using var fixture = new BulkSelectionFixture(title, action);
        var evidence = new SelectionScopeCase(action, fixture.CollectionId, [fixture.Key(0)])
        {
            Stage = fixture.Stage,
            Parameters = fixture.Parameters,
            RowOwnedValueIds = new Dictionary<string, IReadOnlyList<string>>
            {
                [fixture.Key(0)] = fixture.OwnedIds(0), [fixture.Key(1)] = fixture.OwnedIds(1),
            },
            ExpectedChangedValueIds = fixture.OwnedIds(0),
        };
        // Traverse the unmodified real tab, including every fixed slot, without the unrelated raw
        // explorer's much larger lazy tree. This is explicitly tab-scoped advisory evidence.
        var count = fixture.CollectionId == "inventory" ? GameplayFields.InventoryCount : GameplayFields.MateriaCount;
        var limits = new SelectionScopeLimits
        {
            MaxQueryPages = (count + QueryableCollectionLimits.DefaultPageSize - 1) / QueryableCollectionLimits.DefaultPageSize,
        };
        ISaveDocument OpenTab() => new TabDocument(fixture.OpenDocument(), fixture.CollectionId);
        var passed = SelectionScopeFixture.Evaluate(OpenTab, evidence, limits);
        Assert.True(passed.Status == SelectionScopeStatus.Passed, string.Join("; ", passed.Diagnostics));
        Assert.NotEqual(SelectionScopeStatus.NotEvaluated, passed.Status);
        Assert.NotEqual(SelectionScopeStatus.Failed, passed.Status);

        // A meaningful plan missing a declared selected-row consequence must be Failed, whereas
        // incomplete evidence must be NotEvaluated. Neither status is accepted as a pass.
        var missingEffect = evidence with
        {
            RowOwnedValueIds = new Dictionary<string, IReadOnlyList<string>>
            {
                [fixture.Key(0)] = [.. fixture.OwnedIds(0), fixture.Key(0) + "/id"],
                [fixture.Key(1)] = fixture.OwnedIds(1),
            },
            ExpectedChangedValueIds = [.. fixture.OwnedIds(0), fixture.Key(0) + "/id"],
        };
        Assert.Equal(SelectionScopeStatus.Failed, SelectionScopeFixture.Evaluate(OpenTab, missingEffect, limits).Status);
        Assert.Equal(SelectionScopeStatus.NotEvaluated,
            SelectionScopeFixture.Evaluate(OpenTab, evidence with { Stage = null }, limits).Status);
        Assert.Equal(SelectionScopeStatus.NotEvaluated,
            SelectionScopeFixture.Evaluate(OpenTab, evidence, limits with { MaxQueryPages = 1 }).Status);
        Assert.Equal(fixture.Original, fixture.Bytes());
    }

    /// <summary>Forwards the real document and tab unchanged; never fabricates actions, rows or effects.</summary>
    private sealed class TabDocument(ISaveDocument inner, string collection) : ISaveDocument, IQueryableDocument, IActionPlanningDocument
    {
        public string Title => inner.Title;
        public bool CanEdit => inner.CanEdit;
        public IReadOnlyList<QueryableCollection> Collections => ((IQueryableDocument)inner).Collections
            .Where(metadata => metadata.Id == collection).ToArray();
        public ViewNode BuildView()
        {
            var tabs = Assert.IsType<TabsNode>(inner.BuildView());
            var tab = Assert.Single(tabs.Tabs, tab => tab.Children.OfType<QueryableTableNode>().Any(table => table.CollectionId == collection));
            return new TabsNode([tab]);
        }
        public QueryableCollectionPage Query(string collectionId, QueryableCollectionQuery query) =>
            ((IQueryableDocument)inner).Query(collectionId, query);
        public IReadOnlyList<ViewNode> ExpandNode(string lazyId) => inner.ExpandNode(lazyId);
        public FieldValue GetValue(string valueId) => inner.GetValue(valueId);
        public EditOutcome ApplyEdits(IReadOnlyList<ValueEdit> edits) => inner.ApplyEdits(edits);
        public IReadOnlyList<FileOutput> Serialize() => inner.Serialize();
        public ActionPlan PlanAction(string actionId, IReadOnlyDictionary<string, FieldValue> parameters, ActionScope scope) =>
            ((IActionPlanningDocument)inner).PlanAction(actionId, parameters, scope);
        public EditOutcome ApplyActionPlan(string actionId, IReadOnlyDictionary<string, FieldValue> parameters, ActionScope scope, ActionPlan plan) =>
            throw new InvalidOperationException("The advisory fixture must never apply a plan.");
        public void Dispose() => inner.Dispose();
    }
}
