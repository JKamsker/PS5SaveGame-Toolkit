using System.Text.Json;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.FinalFantasy16.Tests;

public sealed class FaithStoredStackHistoryTests
{
    private const int EditedSlot = 1103;
    private const string EditedRow = "inventory/1103";
    private const string EditedQuantity = EditedRow + "/quantity";
    private readonly FinalFantasy16Plugin _plugin = new();

    [Fact]
    public void ManualEditUndoRedoRefreshMembershipWithoutChangingIdentityOrNegativeWriteRules()
    {
        var original = FaithStoredStackData.Save();
        var expected = original.ToArray();
        FaithStoredStackData.WriteQuantity(expected, EditedSlot, 0);
        FaithTestData.FixCrc(expected);
        using var fixture = new FaithSessionFile(original);
        using var document = _plugin.Open(fixture.File);
        using var session = new DocumentSession("stored-stack-edit", fixture.File, _plugin, document);
        _ = session.BuildView();
        var queryable = (IQueryableDocument)document;
        var positive = session.QueryCollection(FaithInventoryView.CollectionId, new()
        {
            Filters = new Dictionary<string, string> { [FaithStoredStackData.ColumnKey] = "Positive", ["slot"] = "1103" },
        }, expectedRevision: null, cancellationToken: TestContext.Current.CancellationToken);
        var before = Assert.Single(positive.Rows);
        Assert.Equal(EditedRow, before.Key);
        var state = session.State;
        Assert.True(session.ApplyEdits([new IncomingEdit(EditedQuantity, JsonSerializer.SerializeToElement(1))],
            expectedRevision: null, cancellationToken: TestContext.Current.CancellationToken).Ok);
        Assert.Equal(state, session.State);
        Assert.Equal(original, Assert.Single(document.Serialize()).Content.ToArray());

        Assert.True(session.ApplyEdits([new IncomingEdit(EditedQuantity, JsonSerializer.SerializeToElement(0))],
            expectedRevision: null, cancellationToken: TestContext.Current.CancellationToken).Ok);
        AssertMembership(queryable, positive: 479, zero: 241);
        var after = queryable.Query(FaithInventoryView.CollectionId, new()
        {
            Filters = new Dictionary<string, string> { [FaithStoredStackData.ColumnKey] = "Zero", ["slot"] = "1103" },
        });
        Assert.Equal(EditedRow, Assert.Single(after.Rows).Key);
        foreach (var column in new[] { "id", "name", "category", "flags", "details", "action" })
            Assert.Equal(JsonSerializer.Serialize(before.Cells[column]), JsonSerializer.Serialize(after.Rows[0].Cells[column]));
        Assert.Equal(expected, Assert.Single(document.Serialize()).Content.ToArray());
        var negatives = session.QueryCollection(FaithInventoryView.CollectionId, StateQuery("Negative") with { Limit = 2 },
            expectedRevision: null, cancellationToken: TestContext.Current.CancellationToken);
        Assert.All(negatives.Rows, row => Assert.True(Assert.IsType<FieldNode>(row.Cells["quantity"]).Spec.ReadOnly));
        var editedState = session.State;
        Assert.False(session.ApplyEdits([new IncomingEdit("inventory/0/quantity", JsonSerializer.SerializeToElement(0))],
            expectedRevision: null, cancellationToken: TestContext.Current.CancellationToken).Ok);
        Assert.Equal(editedState, session.State);
        Assert.Equal(expected, Assert.Single(document.Serialize()).Content.ToArray());

        Assert.True(session.Undo().Ok);
        AssertMembership(queryable, positive: 480, zero: 240);
        Assert.Equal(original, Assert.Single(document.Serialize()).Content.ToArray());
        Assert.False(session.State.CanUndo);
        Assert.True(session.Redo().Ok);
        AssertMembership(queryable, positive: 479, zero: 241);
        Assert.Equal(expected, Assert.Single(document.Serialize()).Content.ToArray());
    }

    [Fact]
    public void PositiveQueryActionIncludesUnloadedRowsExcludesExplicitRowAndUndoesExactly()
    {
        var original = FaithStoredStackData.Save();
        var selected = Enumerable.Range(0, FaithInventory.Capacity)
            .Where(slot => slot != EditedSlot && slot % FaithStoredStackData.CycleLength is 3 or 4).ToArray();
        var expected = original.ToArray();
        foreach (var slot in selected) FaithStoredStackData.WriteQuantity(expected, slot, 0);
        FaithTestData.FixCrc(expected);
        using var fixture = new FaithSessionFile(original);
        using var document = _plugin.Open(fixture.File);
        using var session = new DocumentSession("stored-stack-action", fixture.File, _plugin, document);
        _ = session.BuildView();
        _ = session.QueryCollection(FaithInventoryView.CollectionId, StateQuery("Positive") with { Limit = 1 },
            expectedRevision: null, cancellationToken: TestContext.Current.CancellationToken);
        var query = StateQuery("Positive") with { SortColumn = "slot", Descending = true, IncludeEmpty = false };
        var scope = new ActionScope
        {
            QueryScope = new(FaithInventoryView.CollectionId, query, session.State.Revision) { ExcludedRowKeys = [EditedRow] },
        };
        var preview = session.PlanAction(FaithQuantityPlanning.ActionId, Parameters(), scope,
            expectedRevision: null, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Empty(preview.Plan.Errors);
        Assert.NotNull(preview.PlanId);
        Assert.Equal(selected.Reverse().Select(slot => $"inventory/{slot}"), preview.Scope.RowKeys!);
        Assert.Empty(preview.Plan.SkippedRows);
        var changes = preview.Plan.Changes.Where(change => !change.Derived).ToArray();
        Assert.Equal(selected.Reverse().Select(slot => $"inventory/{slot}/quantity"), changes.Select(change => change.ValueId));
        Assert.All(changes, change => Assert.Equal(0, change.NewValue.AsInt32()));
        Assert.Equal(original, Assert.Single(document.Serialize()).Content.ToArray());
        Assert.True(session.ApplyActionPlan(preview.PlanId, preview.Revision, cancellationToken: TestContext.Current.CancellationToken).Ok);
        Assert.Equal(expected, Assert.Single(document.Serialize()).Content.ToArray());
        var queryable = (IQueryableDocument)document;
        AssertMembership(queryable, positive: 1, zero: 719);
        Assert.Equal(EditedRow, Assert.Single(queryable.Query(FaithInventoryView.CollectionId, StateQuery("Positive")).Rows).Key);
        using var reopened = _plugin.Open(FaithTestData.File(expected));
        AssertMembership((IQueryableDocument)reopened, positive: 1, zero: 719);
        Assert.True(session.Undo().Ok);
        AssertMembership(queryable, positive: 480, zero: 240);
        Assert.Equal(original, Assert.Single(document.Serialize()).Content.ToArray());
        Assert.False(session.State.CanUndo);
        Assert.True(session.Redo().Ok);
        AssertMembership(queryable, positive: 1, zero: 719);
        Assert.Equal(expected, Assert.Single(document.Serialize()).Content.ToArray());
    }

    [Theory]
    [InlineData("Negative", 480)]
    [InlineData("Zero", 240)]
    [InlineData("missing", 0)]
    public void NegativeZeroAndZeroMatchQueryActionsRemainByteAndHistoryNoOps(string stateLabel, int count)
    {
        var original = FaithStoredStackData.Save();
        using var fixture = new FaithSessionFile(original);
        using var document = _plugin.Open(fixture.File);
        using var session = new DocumentSession("stored-stack-no-op", fixture.File, _plugin, document);
        _ = session.BuildView();
        var before = session.State;
        var scope = new ActionScope
        {
            QueryScope = new(FaithInventoryView.CollectionId, StateQuery(stateLabel), session.State.Revision),
        };
        var preview = session.PlanAction(FaithQuantityPlanning.ActionId, Parameters(), scope,
            expectedRevision: null, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Empty(preview.Plan.Errors);
        Assert.NotNull(preview.PlanId);
        Assert.Equal(count, preview.Scope.RowKeys!.Count);
        Assert.Equal(count, preview.Plan.SkippedRows.Count);
        Assert.Empty(preview.Plan.Changes);
        if (stateLabel == "Negative")
            Assert.All(preview.Plan.SkippedRows, row => Assert.Contains("Negative stack has unknown semantics", row.Reason));
        Assert.True(session.ApplyActionPlan(preview.PlanId, preview.Revision, cancellationToken: TestContext.Current.CancellationToken).Ok);
        Assert.Equal(before, session.State);
        Assert.Equal(original, Assert.Single(document.Serialize()).Content.ToArray());
        AssertMembership((IQueryableDocument)document, positive: 480, zero: 240);
        var excluded = session.PlanAction(FaithQuantityPlanning.ActionId, Parameters(), new ActionScope
        {
            QueryScope = new(FaithInventoryView.CollectionId, new()
            {
                Filters = new Dictionary<string, string> { [FaithStoredStackData.ColumnKey] = "Positive", ["slot"] = "1103" },
            }, session.State.Revision) { ExcludedRowKeys = [EditedRow] },
        }, expectedRevision: null, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Empty(excluded.Plan.Errors);
        Assert.NotNull(excluded.PlanId);
        Assert.Empty(excluded.Scope.RowKeys!);
        Assert.Empty(excluded.Plan.Changes);
        Assert.True(session.ApplyActionPlan(excluded.PlanId, excluded.Revision, cancellationToken: TestContext.Current.CancellationToken).Ok);
        Assert.Equal(before, session.State);
        Assert.Equal(original, Assert.Single(document.Serialize()).Content.ToArray());
        Assert.False(session.State.CanUndo);
    }

    private static QueryableCollectionQuery StateQuery(string state) => new()
    {
        Filters = new Dictionary<string, string> { [FaithStoredStackData.ColumnKey] = state },
    };

    private static Dictionary<string, JsonElement> Parameters() => new()
    {
        [FaithQuantityPlanning.QuantityParameter] = JsonSerializer.SerializeToElement(0),
    };

    private static void AssertMembership(IQueryableDocument document, int positive, int zero)
    {
        Assert.Equal(positive, document.Query(FaithInventoryView.CollectionId, StateQuery("Positive")).FilteredCount);
        Assert.Equal(zero, document.Query(FaithInventoryView.CollectionId, StateQuery("Zero")).FilteredCount);
        Assert.Equal(480, document.Query(FaithInventoryView.CollectionId, StateQuery("Negative")).FilteredCount);
    }
}
