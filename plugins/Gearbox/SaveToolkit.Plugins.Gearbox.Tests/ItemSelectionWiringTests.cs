using System.Text.Json;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.Gearbox.Tests;

public sealed class ItemSelectionWiringTests
{
    private static readonly Dictionary<string, JsonElement> Empty = [];

    public static IEnumerable<object[]> Cases =>
        from wonderlands in new[] { false, true }
        from list in new[] { GearboxInventory.Backpack, GearboxInventory.Bank, GearboxInventory.LostLoot }
        from action in new[] { "items/duplicate", "items/remove" }
        select new object[] { wonderlands, list, action };

    [Theory]
    [MemberData(nameof(Cases))]
    public void DeclaredRowAction_ExplicitAndQueryScopesPreserveUnselectedRecordsAndHistory(bool wonderlands, string list, string actionId)
    {
        using var workspace = SelectionTestData.Workspace(wonderlands);
        var file = workspace.Set.AllDiskFiles.Single(file => file.FileName == (list == GearboxInventory.Backpack ? "1" : "profile"));
        var session = workspace.Open(file.Id, "gearbox");
        _ = session.BuildView();
        var page = session.QueryCollection(list, new() { Limit = 1 });
        Assert.Single(page.Rows); // Other selected rows are not loaded by this client page.
        var details = session.Expand(Assert.IsType<SectionNode>(page.Rows[0].Cells["detail"]).LazyId!);
        var action = Assert.Single(details.OfType<ActionNode>(), node => node.ActionId == actionId);
        var original = session.Serialize().Single().Content.ToArray();
        var originalKeys = session.QueryCollection(list, new() { Limit = 10 }).Rows.Select(row => row.Key).ToArray();

        foreach (var querySelection in new[] { false, true })
        {
            var current = session.QueryCollection(list, new() { Limit = 1 });
            _ = session.Expand(Assert.IsType<SectionNode>(current.Rows[0].Cells["detail"]).LazyId!);
            var selected = querySelection ? new[] { originalKeys[2], originalKeys[0] } : [originalKeys[0]];
            var scope = querySelection ? new ActionScope
            {
                QueryScope = new(list, new() { Offset = 1, Limit = 1, SortColumn = "index", Descending = true }, session.State.Revision)
                { ExcludedRowKeys = [originalKeys[1]] },
            } : new(selected);
            var preview = session.PlanAction(action.ActionId, Empty, scope);
            Assert.Empty(preview.Plan.Errors);
            Assert.Equal(selected, preview.Scope.RowKeys);
            Assert.Equal(selected.Order(StringComparer.Ordinal), preview.Plan.Changes.Where(change => !change.Derived)
                .Select(change => change.ValueId[..^"/presence".Length]).Order(StringComparer.Ordinal));
            Assert.Equal(original, session.Serialize().Single().Content.ToArray());
            Assert.False(session.State.CanUndo);
            Assert.True(session.ApplyActionPlan(preview.PlanId!, preview.Revision).Ok);
            var changed = session.Serialize().Single().Content.ToArray();
            var changedKeys = session.QueryCollection(list, new() { Limit = 10 }).Rows.Select(row => row.Key).ToArray();
            Assert.Equal(originalKeys.Length + (actionId == "items/duplicate" ? selected.Length : -selected.Length), changedKeys.Length);
            Assert.Contains(originalKeys[1], changedKeys);
            if (actionId == "items/remove") Assert.DoesNotContain(changedKeys, selected.Contains);
            else Assert.Equal(originalKeys, changedKeys.Take(originalKeys.Length));
            using var reopened = (GearboxDocument)new GearboxPlugin().Open(GearboxTestData.Overlay(file, changed));
            using var baseline = (GearboxDocument)new GearboxPlugin().Open(file);
            Assert.Equal(baseline.Inventory.Find(originalKeys[1]).Field.Encode(), reopened.Inventory.Entries
                .Where(entry => entry.List == list).ElementAt(actionId == "items/remove" ? 0 : 1).Field.Encode());
            SerialWriteTests.AssertUnrelated(baseline.Root, reopened.Root, list, "equipped_inventory_list");
            if (list == GearboxInventory.Backpack)
                Assert.Equal(actionId == "items/duplicate" ? new long[] { 0, 2 } : querySelection ? [-1, -1] : [-1, 1],
                    reopened.Root.Children("equipped_inventory_list").Select(slot => slot.Integer("inventory_list_index")));
            Assert.False(session.ApplyActionPlan(preview.PlanId!, preview.Revision).Ok);
            Assert.True(session.Undo().Ok);
            Assert.Equal(original, session.Serialize().Single().Content.ToArray());
            Assert.Equal(originalKeys, session.QueryCollection(list, new() { Limit = 10 }).Rows.Select(row => row.Key));
            Assert.False(session.State.CanUndo);
            Assert.False(session.State.IsDirty);
            Assert.True(session.Redo().Ok);
            Assert.Equal(changed, session.Serialize().Single().Content.ToArray());
            Assert.Equal(changedKeys, session.QueryCollection(list, new() { Limit = 10 }).Rows.Select(row => row.Key));
            Assert.True(session.Undo().Ok);
        }
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void DeclaredRowAction_RejectsMissingScopeAndKeepsEmptySelectionsAsNoOps(bool wonderlands, string list, string actionId)
    {
        using var workspace = SelectionTestData.Workspace(wonderlands);
        var file = workspace.Set.AllDiskFiles.Single(file => file.FileName == (list == GearboxInventory.Backpack ? "1" : "profile"));
        var session = workspace.Open(file.Id, "gearbox");
        _ = session.BuildView();
        var page = session.QueryCollection(list, new() { Limit = 10 });
        _ = session.Expand(Assert.IsType<SectionNode>(page.Rows[0].Cells["detail"]).LazyId!);
        var before = session.Serialize().Single().Content.ToArray();
        var revision = session.State.Revision;
        Assert.NotEmpty(session.PlanAction(actionId, Empty).Plan.Errors);
        Assert.False(session.InvokeAction(actionId, Empty).Ok); // Planning cannot be bypassed.
        Assert.NotEmpty(session.PlanAction(actionId, Empty, new(["foreign"])).Plan.Errors);
        Assert.NotEmpty(session.PlanAction(actionId, Empty, new([page.Rows[0].Key, page.Rows[0].Key])).Plan.Errors);
        foreach (var scope in new ActionScope[]
        {
            new([]),
            new() { QueryScope = new(list, new(), revision) { ExcludedRowKeys = page.Rows.Select(row => row.Key).ToArray() } },
            new() { QueryScope = new(list, new() { Text = "no-such-synthetic-item" }, revision) },
        })
        {
            var preview = session.PlanAction(actionId, Empty, scope);
            Assert.Empty(preview.Plan.Errors);
            Assert.Empty(preview.Scope.RowKeys!);
            Assert.Empty(preview.Plan.Changes);
            Assert.True(session.ApplyActionPlan(preview.PlanId!, preview.Revision).Ok);
            Assert.Equal(revision, session.State.Revision);
            Assert.False(session.State.IsDirty);
            Assert.False(session.State.CanUndo);
            Assert.Equal(before, session.Serialize().Single().Content.ToArray());
        }
    }
}
