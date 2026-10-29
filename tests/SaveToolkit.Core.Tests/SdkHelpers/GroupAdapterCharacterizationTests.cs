using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Core.Tests.SdkHelpers;

// These tests call the unchanged public plugin APIs, before any SDK adapter exists.
public sealed class GroupAdapterCharacterizationTests
{
    [Theory]
    [InlineData("xorjson")]
    [InlineData("PPSA01463")]
    [InlineData("PPSA01493")]
    public void ViewsAndQueries_PreserveLocalEscapingAndDistinctRowPolicies(string kind)
    {
        using var fixture = new GroupAdapterPluginFixture(kind);
        var grouped = GroupAdapterPluginFixture.Walk(fixture.Group, fixture.Group.BuildView()).OfType<FieldNode>().ToArray();
        foreach (var member in fixture.Input.Members)
        {
            using var local = fixture.Plugin.Open(member.File);
            var fields = GroupAdapterPluginFixture.Walk(local, local.BuildView()).OfType<FieldNode>().ToArray();
            Assert.NotEmpty(fields);
            foreach (var field in fields)
            {
                var id = member.Role + "/" + field.ValueId;
                var mapped = Assert.Single(grouped, f => f.ValueId == id);
                Assert.Equal(mapped.Value, fixture.Group.GetValue(id));
            }
            if (!fixture.Gearbox)
                Assert.Contains(fields, f => f.Label == "a~/b" && f.ValueId.StartsWith("value/", StringComparison.Ordinal));

            var queryable = Assert.IsAssignableFrom<IQueryableDocument>(local);
            Assert.NotEmpty(queryable.Collections);
            foreach (var collection in queryable.Collections)
            {
                var query = new QueryableCollectionQuery { Limit = 2 };
                var page = queryable.Query(collection.Id, query);
                var mapped = ((IQueryableDocument)fixture.Group).Query(member.Role + "/" + collection.Id, query);
                Assert.Equal(page.TotalCount, mapped.TotalCount);
                Assert.Equal(page.Rows.Select(r => member.Role + "/" + (fixture.Gearbox ? collection.Id + "/" : "") + r.Key),
                    mapped.Rows.Select(r => r.Key));
                foreach (var row in mapped.Rows)
                    foreach (var cell in row.Cells.Values.OfType<FieldNode>())
                        Assert.Equal(cell.Value, fixture.Group.GetValue(cell.ValueId));
            }
        }
        Assert.Throws<KeyNotFoundException>(() => fixture.Group.GetValue("absent/value"));
        Assert.Throws<KeyNotFoundException>(() => fixture.Group.GetValue("Character/value"));
        Assert.Throws<KeyNotFoundException>(() => ((IQueryableDocument)fixture.Group).Query("character/absent", new()));
        Assert.Throws<KeyNotFoundException>(() => fixture.Group.InvokeAction("absent/action", new Dictionary<string, FieldValue>()));
    }

    [Theory]
    [InlineData("xorjson")]
    [InlineData("PPSA01463")]
    [InlineData("PPSA01493")]
    public void EditsAcrossMembers_RollBackAndRestoreFreshReusableSnapshots(string kind)
    {
        using var fixture = new GroupAdapterPluginFixture(kind);
        var group = fixture.Group;
        var before = GroupAdapterPluginFixture.Bytes(group);
        var fields = GroupAdapterPluginFixture.Walk(group, group.BuildView()).OfType<FieldNode>().ToArray();
        var edits = fixture.Gearbox
            ? new ValueEdit[] { new("character/character/xp", FieldValue.Of(123)), new("profile/keys/2420693941", FieldValue.Of(1)) }
            : fields.Where(f => f.Label is "Level" or "volume").Select(f => new ValueEdit(f.ValueId, FieldValue.ExactNumber("2"))).ToArray();
        Assert.Equal(2, edits.Length);
        Assert.False(group.ApplyEdits([edits[0], new(edits[1].ValueId.Split('/')[0] + "/absent", FieldValue.Of(1))]).Succeeded);
        GroupAdapterPluginFixture.EqualBytes(before, group);
        _ = GroupAdapterPluginFixture.Walk(group, group.BuildView()).ToArray();
        var snapshot = group.CaptureMemento();
        var applied = group.ApplyEdits(edits);
        Assert.True(applied.Succeeded);
        Assert.NotNull(applied.Revert);
        Assert.All(edits, edit => Assert.Equal(edit.Value, group.GetValue(edit.ValueId)));
        var changed = GroupAdapterPluginFixture.Bytes(group);
        using var fresh = fixture.OpenGroup();
        _ = fresh.RestoreMemento(group.CaptureMemento());
        GroupAdapterPluginFixture.EqualBytes(changed, fresh);
        var redo = group.RestoreMemento(applied.Revert);
        GroupAdapterPluginFixture.EqualBytes(before, group);
        _ = group.RestoreMemento(redo);
        GroupAdapterPluginFixture.EqualBytes(changed, group);
        _ = group.RestoreMemento(snapshot);
        _ = group.RestoreMemento(snapshot);
        GroupAdapterPluginFixture.EqualBytes(before, group);
    }

    [Fact]
    public void XorJson_ActionsAndPlansKeepParameterNamesLocalAndMapResults()
    {
        using var fixture = new GroupAdapterPluginFixture("xorjson");
        var group = fixture.Group;
        var before = GroupAdapterPluginFixture.Bytes(group);
        var field = GroupAdapterPluginFixture.Walk(group, group.BuildView()).OfType<FieldNode>().Single(f => f.Label == "volume");
        Assert.NotNull(group.InvokeAction("global/json/inspect", new Dictionary<string, FieldValue>()).Message);
        var planner = (IActionPlanningDocument)group;
        var parameters = new Dictionary<string, FieldValue> { ["value"] = FieldValue.Of("49.00E+00") };
        var scope = new ActionScope([field.ValueId]);
        var plan = planner.PlanAction("global/json/bulk-set", parameters, scope);
        Assert.Empty(plan.Errors);
        Assert.Equal(field.ValueId, Assert.Single(plan.Changes).ValueId);
        var outcome = planner.ApplyActionPlan("global/json/bulk-set", parameters, scope, plan);
        Assert.True(outcome.Succeeded);
        Assert.Equal(field.ValueId, Assert.Single(outcome.Changes).ValueId);
        Assert.Equal(FieldValue.ExactNumber("49.00E+00"), group.GetValue(field.ValueId));
        _ = group.RestoreMemento(outcome.Revert!);
        GroupAdapterPluginFixture.EqualBytes(before, group);
    }

    [Theory]
    [InlineData("PPSA01463")]
    [InlineData("PPSA01493")]
    public void Gearbox_QuerySelectionDispatchesToMemberPlanAndRejectsForeignCollection(string kind)
    {
        using var fixture = new GroupAdapterPluginFixture(kind);
        var group = fixture.Group;
        var page = ((IQueryableDocument)group).Query("character/inventory_items", new() { Limit = 1 });
        var key = Assert.Single(page.Rows).Key;
        var planner = (IActionPlanningDocument)group;
        var scope = new ActionScope([key]) { QueryScope = new(page.CollectionId, new(), 3) };
        var plan = planner.PlanAction("character/items/remove", new Dictionary<string, FieldValue>(), scope);
        Assert.Empty(plan.Errors);
        Assert.NotEmpty(plan.Changes);
        Assert.All(plan.Changes, change => Assert.StartsWith("character/", change.ValueId));
        Assert.Throws<SaveFormatException>(() => planner.PlanAction("character/items/remove", new Dictionary<string, FieldValue>(),
            scope with { QueryScope = new("character/bank_inventory_list", new(), 3) }));
        Assert.Throws<KeyNotFoundException>(() => planner.PlanAction("character/items/remove", new Dictionary<string, FieldValue>(), new(["profile/row"])));
    }
}
