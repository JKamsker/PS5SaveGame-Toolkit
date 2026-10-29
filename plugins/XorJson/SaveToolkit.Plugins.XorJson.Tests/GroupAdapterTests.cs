using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.XorJson.Tests;

public sealed class GroupAdapterTests
{
    private const string ActionId = "global/json/bulk-set";
    private static readonly IReadOnlyDictionary<string, FieldValue> Parameters =
        new Dictionary<string, FieldValue> { ["value"] = FieldValue.Of("49") };

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void QueryScopeRetainsRoleKeyIdentityAndRejectsForeignSelections(bool xor)
    {
        using var fixture = new GroupIdentityFixture(xor);
        var queryable = Assert.IsAssignableFrom<IQueryableDocument>(fixture.Group);
        var planner = Assert.IsAssignableFrom<IActionPlanningDocument>(fixture.Group);
        var collection = queryable.Collections.Single(collection => collection.Id.StartsWith("global/", StringComparison.Ordinal));
        var query = new QueryableCollectionQuery { Offset = 1, Limit = 2 };
        var page = queryable.Query(collection.Id, query);
        var keys = page.Rows.Select(row => row.Key).ToArray();
        Assert.All(keys, key => Assert.StartsWith("global/value/", key));
        var queryScope = new ActionQueryScope(collection.Id, query, 17) { ExcludedRowKeys = [keys[1]] };
        var scope = new ActionScope([keys[0]]) { QueryScope = queryScope };
        var plan = planner.PlanAction(ActionId, Parameters, scope);
        Assert.Empty(plan.Errors);
        Assert.Equal(keys[0], Assert.Single(plan.Changes).ValueId);
        Assert.Empty(planner.PlanAction(ActionId, Parameters, scope with { RowKeys = [] }).Changes);
        Assert.NotEmpty(planner.PlanAction(ActionId, Parameters, new()).Changes);
        Assert.NotEmpty(planner.PlanAction(ActionId, Parameters, scope with { RowKeys = null }).Errors);

        var foreignScopes = new[]
        {
            scope with { RowKeys = ["character/value/1"] },
            scope with { QueryScope = queryScope with { CollectionId = "character/character/inventory" } },
            scope with { QueryScope = queryScope with { ExcludedRowKeys = ["character/value/1"] } },
        };
        foreach (var foreign in foreignScopes)
        {
            Assert.Equal("Unknown XorJson group member identity.",
                Assert.Throws<KeyNotFoundException>(() => planner.PlanAction(ActionId, Parameters, foreign)).Message);
            Assert.Equal("Unknown XorJson group member identity.",
                Assert.Throws<KeyNotFoundException>(() => planner.ApplyActionPlan(ActionId, Parameters, foreign, plan)).Message);
            fixture.AssertOriginalBytes(fixture.Group);
        }
        Assert.Same(query, queryScope.Query);
        Assert.Equal(17, queryScope.Revision);
        Assert.Equal(keys[1], Assert.Single(queryScope.ExcludedRowKeys));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RejectedMixedMemberEditsRestoreEveryMemberAndBindings(bool xor)
    {
        using var fixture = new GroupIdentityFixture(xor);
        var fields = ViewTraversal.Nodes(fixture.Group).OfType<FieldNode>().ToArray();
        var level = fields.Single(field => field.Label == "Level");
        foreach (var badId in new[] { "global/value/999999", "foreign/value/1" })
        {
            var rejected = fixture.Group.ApplyEdits([
                new(level.ValueId, FieldValue.ExactNumber("2")), new(badId, FieldValue.ExactNumber("3")),
            ]);
            Assert.False(rejected.Succeeded);
            Assert.Equal("group", Assert.Single(rejected.Errors).ValueId);
            if (badId.StartsWith("foreign/", StringComparison.Ordinal))
                Assert.Equal("Unknown XorJson group member identity.", rejected.Errors[0].Message);
            fixture.AssertOriginalBytes(fixture.Group);
            _ = ViewTraversal.Nodes(fixture.Group);
            Assert.Equal(level.Value, fixture.Group.GetValue(level.ValueId));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FreshGroupCanReuseWholeGroupSnapshotsAfterMixedEditsAndStructureChanges(bool xor)
    {
        using var fixture = new GroupIdentityFixture(xor);
        var fields = ViewTraversal.Nodes(fixture.Group).OfType<FieldNode>().ToArray();
        var level = fields.Single(field => field.Label == "Level");
        var volume = fields.Single(field => field.Label == "volume");
        var original = fixture.Group.CaptureMemento();
        var outcome = fixture.Group.ApplyEdits([
            new(volume.ValueId, FieldValue.ExactNumber("48")),
            new(level.ValueId, FieldValue.ExactNumber("2")),
            new(volume.ValueId, FieldValue.ExactNumber("49")),
        ]);
        Assert.True(outcome.Succeeded);
        Assert.NotNull(outcome.Revert);
        Assert.Equal(new[] { volume.ValueId, volume.ValueId, level.ValueId }, outcome.Changes.Select(change => change.ValueId));
        var changed = fixture.Group.CaptureMemento();
        using var fresh = fixture.Open();
        for (var attempt = 0; attempt < 2; attempt++)
        {
            _ = fresh.RestoreMemento(changed);
            var nodes = ViewTraversal.Nodes(fresh);
            Assert.Equal(FieldValue.ExactNumber("2"), fresh.GetValue(level.ValueId));
            Assert.Equal(FieldValue.ExactNumber("49"), fresh.GetValue(volume.ValueId));
            var remove = nodes.OfType<ActionNode>().First(action => action.ActionId.StartsWith("global/json/remove/", StringComparison.Ordinal));
            var structural = fresh.InvokeAction(remove.ActionId, new Dictionary<string, FieldValue>());
            Assert.NotNull(structural.Revert);
            _ = fresh.RestoreMemento(structural.Revert);
            _ = ViewTraversal.Nodes(fresh);
            Assert.Equal(FieldValue.ExactNumber("49"), fresh.GetValue(volume.ValueId));
            _ = fresh.RestoreMemento(original);
            fixture.AssertOriginalBytes(fresh);
            _ = fresh.RestoreMemento(outcome.Revert);
            fixture.AssertOriginalBytes(fresh);
        }
    }
}
