using System.Text.Json;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;

namespace SaveToolkit.Plugins.FinalFantasy7.Tests;

public sealed class BulkSelectionPlanningTests
{
    public static IEnumerable<object[]> Cases()
    {
        foreach (var title in new[] { BulkSelectionFixture.Remake, BulkSelectionFixture.Rebirth, BulkSelectionFixture.Demo })
            foreach (var action in new[] { InventoryPlanning.ActionId, MateriaPlanning.ActionId, MateriaPlanning.StarActionId })
                yield return [title, action];
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void ExplicitUnloadedSelectionPlansAppliesAndUndoesOnlyItsEligibleRows(string title, string action)
    {
        using var fixture = new BulkSelectionFixture(title, action);
        var preview = fixture.Preview(new([fixture.Key(0), fixture.Key(BulkSelectionFixture.UnloadedSlot)]));
        Assert.Equal(new[] { fixture.Key(0), fixture.Key(BulkSelectionFixture.UnloadedSlot) }, preview.Scope.RowKeys);
        AssertEffects(fixture, preview.Plan, 0, BulkSelectionFixture.UnloadedSlot);
        fixture.AssertApplyAndHistory(preview, 0, BulkSelectionFixture.UnloadedSlot);
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void MatchingQueryRetainsExactMetadataAndExclusionsAndIncludesUnloadedRows(string title, string action)
    {
        using var fixture = new BulkSelectionFixture(title, action);
        var query = fixture.MatchingQuery(includeEmpty: true);
        var loaded = fixture.Session.QueryCollection(fixture.CollectionId, query with { Offset = 0 },
            expectedRevision: null, cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotEqual(fixture.Key(BulkSelectionFixture.UnloadedSlot), Assert.Single(loaded.Rows).Key);
        var exclusions = new[] { fixture.Key(1), "not-in-this-result" };
        var preview = fixture.Preview(new()
        {
            QueryScope = new(fixture.CollectionId, query, fixture.Session.State.Revision) { ExcludedRowKeys = exclusions },
        });
        Assert.Empty(preview.Plan.Errors);
        var scope = Assert.IsType<ActionQueryScope>(preview.Scope.QueryScope);
        Assert.Equal(fixture.CollectionId, scope.CollectionId);
        Assert.Equal(fixture.Session.State.Revision, scope.Revision);
        Assert.Equal(exclusions, scope.ExcludedRowKeys);
        Assert.Equal(query.Text, scope.Query.Text);
        Assert.Equal(query.Filters!.ToArray(), scope.Query.Filters!.ToArray());
        Assert.Equal(query.SortColumn, scope.Query.SortColumn);
        Assert.Equal(query.Descending, scope.Query.Descending);
        Assert.Equal(query.IncludeEmpty, scope.Query.IncludeEmpty);
        Assert.Equal(SaveToolkit.Sdk.Collections.QueryableCollectionLimits.MaxPageSize, scope.Query.Limit);
        Assert.Equal(0, scope.Query.Offset);
        Assert.Null(scope.Query.Cursor);
        var selected = fixture.MatchingSlots.Where(slot => slot != 1).OrderDescending().ToArray();
        Assert.Equal(selected.Select(fixture.Key), preview.Scope.RowKeys);
        AssertEffects(fixture, preview.Plan, 0, BulkSelectionFixture.UnloadedSlot);
        Assert.Equal(selected.Except([0, BulkSelectionFixture.UnloadedSlot]).Select(fixture.Key).Order(StringComparer.Ordinal),
            preview.Plan.SkippedRows.Select(row => row.RowKey));
        fixture.AssertApplyAndHistory(preview, 0, BulkSelectionFixture.UnloadedSlot);
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void DocumentScopeAndEveryEmptySelectionRemainDistinct(string title, string action)
    {
        using var fixture = new BulkSelectionFixture(title, action);
        var all = fixture.Preview(new());
        Assert.Null(all.Scope.RowKeys);
        Assert.Null(all.Scope.QueryScope);
        AssertEffects(fixture, all.Plan, 0, 1, BulkSelectionFixture.UnloadedSlot);
        ActionScope[] emptyScopes =
        [
            new([]),
            new() { QueryScope = new(fixture.CollectionId, fixture.MatchingQuery(), fixture.Session.State.Revision)
                { ExcludedRowKeys = fixture.MatchingSlots.Select(fixture.Key).ToArray() } },
            new() { QueryScope = new(fixture.CollectionId, fixture.MatchingQuery() with { Text = "no matching row" }, fixture.Session.State.Revision) },
        ];
        foreach (var empty in emptyScopes)
        {
            var preview = fixture.Preview(empty);
            Assert.Empty(preview.Plan.Errors);
            Assert.NotNull(preview.Scope.RowKeys);
            Assert.Empty(preview.Scope.RowKeys);
            Assert.Empty(preview.Plan.Changes);
            Assert.Empty(preview.Plan.SkippedRows);
            Assert.NotNull(preview.PlanId);
            Assert.True(fixture.Session.ApplyActionPlan(preview.PlanId, preview.Revision, cancellationToken: TestContext.Current.CancellationToken).Ok);
            Assert.Equal(fixture.Original, fixture.Bytes());
            Assert.False(fixture.Session.State.CanUndo);
        }
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void IneligibleAndForeignRowsAreSkippedWithoutBroadeningSelection(string title, string action)
    {
        using var fixture = new BulkSelectionFixture(title, action);
        int[] ineligible = [BulkSelectionFixture.IneligibleSlot, BulkSelectionFixture.ZeroOrSummonSlot,
            BulkSelectionFixture.ForeignTypeSlot, BulkSelectionFixture.UnknownSlot, BulkSelectionFixture.EquipmentSlot];
        var keys = ineligible.Select(fixture.Key).Concat(["foreign/0", fixture.CollectionId + "/01"]);
        var preview = fixture.Preview(new(keys.ToArray()));
        Assert.Empty(preview.Plan.Errors);
        Assert.Empty(preview.Plan.Changes);
        Assert.Equal(7, preview.Plan.SkippedRows.Count);
        Assert.All(preview.Plan.SkippedRows, row => Assert.False(string.IsNullOrWhiteSpace(row.Reason)));
        Assert.Equal(fixture.Original, fixture.Bytes());
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void StaleQueryRevisionAndPreviewCannotApplyAfterAnInterveningEdit(string title, string action)
    {
        using var fixture = new BulkSelectionFixture(title, action);
        var scope = new ActionScope { QueryScope = new(fixture.CollectionId, fixture.MatchingQuery(), fixture.Session.State.Revision) };
        var preview = fixture.Preview(scope);
        Assert.NotNull(preview.PlanId);
        Assert.True(fixture.Session.ApplyEdits([new(fixture.PrimaryId(1), JsonSerializer.SerializeToElement("2"))],
            expectedRevision: null, cancellationToken: TestContext.Current.CancellationToken).Ok);
        var edited = fixture.Bytes();
        Assert.True(fixture.Session.ApplyActionPlan(preview.PlanId, preview.Revision, cancellationToken: TestContext.Current.CancellationToken).Stale);
        var stale = fixture.Session.PlanAction(action, fixture.WireParameters, scope, fixture.Session.State.Revision,
            TestContext.Current.CancellationToken);
        Assert.True(stale.Stale);
        Assert.Null(stale.PlanId);
        Assert.Equal(edited, fixture.Bytes());
        Assert.True(fixture.Session.Undo().Ok);
        Assert.Equal(fixture.Original, fixture.Bytes());
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void TamperedOrRescopedPlansAndDirectInvocationCannotBypassReview(string title, string action)
    {
        using var fixture = new BulkSelectionFixture(title, action);
        var scope = new ActionScope([fixture.Key(0), fixture.Key(BulkSelectionFixture.UnloadedSlot)]);
        var plan = fixture.Planner.PlanAction(action, fixture.Parameters, scope);
        Assert.NotEmpty(plan.Changes);
        var tampered = plan with { Changes = [plan.Changes[0] with { NewValue = FieldValue.Of(1L) }, .. plan.Changes.Skip(1)] };
        Assert.False(fixture.Planner.ApplyActionPlan(action, fixture.Parameters, scope, tampered).Succeeded);
        Assert.False(fixture.Planner.ApplyActionPlan(action, fixture.Parameters, new([fixture.Key(0)]), plan).Succeeded);
        var foreign = fixture.CollectionId == "inventory" ? "materia" : "inventory";
        Assert.NotEmpty(fixture.Planner.PlanAction(action, fixture.Parameters, scope with
            { QueryScope = new(foreign, new(), fixture.Session.State.Revision) }).Errors);
        Assert.NotEmpty(fixture.Planner.PlanAction(action, fixture.Parameters, new()
            { QueryScope = new(fixture.CollectionId, new(), fixture.Session.State.Revision) }).Errors);
        Assert.False(fixture.Session.InvokeAction(action, fixture.WireParameters, expectedRevision: null,
            cancellationToken: TestContext.Current.CancellationToken).Ok);
        Assert.Equal(fixture.Original, fixture.Bytes());
        Assert.False(fixture.Session.State.CanUndo);
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void QueryBudgetCountsAllMatchesBeforeExclusions(string title, string action)
    {
        var maximumRows = action == InventoryPlanning.ActionId ? 4 : 3;
        using var fixture = new BulkSelectionFixture(title, action, new DocumentSessionOptions { MaxQueryActionRows = maximumRows });
        var preview = fixture.Preview(new()
        {
            QueryScope = new(fixture.CollectionId, fixture.MatchingQuery(), fixture.Session.State.Revision)
                { ExcludedRowKeys = [fixture.Key(1)] },
        });
        Assert.Null(preview.PlanId);
        Assert.Contains(preview.Plan.Errors, error => error.Message.Contains("matching rows", StringComparison.Ordinal));
        Assert.Equal(fixture.Original, fixture.Bytes());
        Assert.False(fixture.Session.State.CanUndo);
        AssertEffects(fixture, fixture.Preview(new([fixture.Key(0)])).Plan, 0);
    }

    private static void AssertEffects(BulkSelectionFixture fixture, ActionPlan plan, params int[] slots)
    {
        Assert.Empty(plan.Errors);
        Assert.Equal(slots.SelectMany(fixture.OwnedIds).Order(StringComparer.Ordinal),
            plan.Changes.Select(change => change.ValueId).Order(StringComparer.Ordinal));
        Assert.All(plan.Changes, change =>
        {
            Assert.NotEqual(change.OldValue, change.NewValue);
            var stars = change.ValueId.EndsWith("/level", StringComparison.Ordinal);
            Assert.Equal(fixture.CollectionId == "materia" &&
                (fixture.ActionId == MateriaPlanning.StarActionId ? !stars : stars), change.Derived);
            Assert.Equal(stars ? BulkSelectionFixture.TargetStars : fixture.CollectionId == "inventory"
                ? BulkSelectionFixture.TargetQuantity : (long)fixture.TargetAp, change.NewValue.AsInt64());
        });
    }
}
