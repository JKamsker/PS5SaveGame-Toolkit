using SaveToolkit.Core.Tests.Fakes;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;
using SaveToolkit.Testing;

namespace SaveToolkit.Core.Tests;

public sealed class SelectionScopeQueryTests
{
    private static SelectionScopeCase Case => new("restore", "items", ["0"])
    {
        Stage = document =>
        {
            _ = ((IQueryableDocument)document).Query("items", new() { Limit = 1 });
            Assert.True(document.ApplyEdits([new("item/0", FieldValue.Of(9))]).Succeeded);
        },
        RowOwnedValueIds = new Dictionary<string, IReadOnlyList<string>> { ["0"] = ["item/0"], ["101"] = ["item/101"] },
        ExpectedChangedValueIds = ["item/0"],
    };

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CompleteQueryTraversalIncludesEmptyRowsAndUnloadedOwnership(bool cursor) =>
        Assert.Equal(SelectionScopeStatus.Passed, SelectionScopeFixture.Evaluate(() => new Document(cursor), Case).Status);

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void PageCutsNeverPass(int pages) => Assert.Equal(SelectionScopeStatus.NotEvaluated,
        SelectionScopeFixture.Evaluate(() => new Document(false), Case, new() { MaxQueryPages = pages }).Status);

    [Fact]
    public void MalformedPagesNeverPass() => Assert.Equal(SelectionScopeStatus.NotEvaluated,
        SelectionScopeFixture.Evaluate(() => new Document(false, malformed: true), Case).Status);

    [Fact]
    public void IncompleteOrAmbiguousOwnershipNeverPasses()
    {
        var ambiguous = new Dictionary<string, IReadOnlyList<string>> { ["0"] = ["item/0"], ["101"] = ["item/0"] };
        Assert.Equal(SelectionScopeStatus.NotEvaluated, SelectionScopeFixture.Evaluate(() => new Document(false), Case with { RowOwnedValueIds = ambiguous }).Status);
        Assert.Equal(SelectionScopeStatus.NotEvaluated, SelectionScopeFixture.Evaluate(() => new Document(false), Case with { RowKeys = ["missing"] }).Status);
        Assert.Equal(SelectionScopeStatus.NotEvaluated, SelectionScopeFixture.Evaluate(() => new Document(false), Case with { Stage = null }).Status);
    }

    private sealed class Document(bool cursor, bool malformed = false) : ISaveDocument, IQueryableDocument, IActionPlanningDocument
    {
        private readonly ConformanceQueryDocument _inner = new(cursor)
        {
            TransformPage = page => malformed ? page with { Rows = [] } : page,
        };
        public string Title => _inner.Title;
        public bool CanEdit => _inner.CanEdit;
        public IReadOnlyList<QueryableCollection> Collections => _inner.Collections;
        public ViewNode BuildView() => new SectionNode(Title,
            [new ActionNode("restore", "Restore") { SupportsPlanning = true, SelectionTableId = "items" }, _inner.BuildView()]);
        public QueryableCollectionPage Query(string collectionId, QueryableCollectionQuery query) => _inner.Query(collectionId, query);
        public FieldValue GetValue(string valueId) => _inner.GetValue(valueId);
        public EditOutcome ApplyEdits(IReadOnlyList<ValueEdit> edits) => _inner.ApplyEdits(edits);
        public IReadOnlyList<FileOutput> Serialize() => _inner.Serialize();
        public ActionPlan PlanAction(string actionId, IReadOnlyDictionary<string, FieldValue> parameters, ActionScope scope) =>
            new() { Changes = [new("item/0", "Item 0", GetValue("item/0"), FieldValue.Of(0))] };
        public EditOutcome ApplyActionPlan(string actionId, IReadOnlyDictionary<string, FieldValue> parameters, ActionScope scope, ActionPlan plan) =>
            throw new InvalidOperationException("Must not apply.");
        public void Dispose() => _inner.Dispose();
    }
}
