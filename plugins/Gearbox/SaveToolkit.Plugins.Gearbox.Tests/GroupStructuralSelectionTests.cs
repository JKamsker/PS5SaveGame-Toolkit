using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.Gearbox.Tests;

public sealed class GroupStructuralSelectionTests
{
    private static readonly Dictionary<string, FieldValue> Empty = [];
    public static IEnumerable<object[]> Cases => ItemSelectionWiringTests.Cases;

    [Theory]
    [MemberData(nameof(Cases))]
    public void MemberRowAction_UsesCompositeOwnershipAndRestoresBothMembersIntoFreshGroup(bool wonderlands, string list, string actionId)
    {
        using var group = SelectionTestData.Group(wonderlands);
        _ = group.BuildView();
        var role = list == GearboxInventory.Backpack ? "character" : "profile";
        var collection = role + "/" + list;
        var page = group.Query(collection, new() { Limit = 10 });
        var action = Assert.Single(SelectionTestData.Details(group, page.Rows[0]).OfType<ActionNode>(), action => action.ActionId == role + "/" + actionId);
        var row = page.Rows[0].Key;
        var localId = row[(collection.Length + 1)..];
        var member = role == "character" ? group.Character : group.Profile;
        var unselectedId = page.Rows[1].Key[(collection.Length + 1)..];
        var unselectedBytes = member.Inventory.Find(unselectedId).Field.Encode();
        var ids = member.Inventory.Entries.Select(entry => entry.Id).ToArray();
        var idsByList = member.Inventory.Entries
            .GroupBy(entry => entry.List, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Select(entry => entry.Id).ToArray(), StringComparer.Ordinal);
        var before = SelectionTestData.Bytes(group);
        var query = new ActionQueryScope(collection, new() { Limit = 1 }, 23) { ExcludedRowKeys = [page.Rows[1].Key, page.Rows[2].Key] };
        var scope = new ActionScope([row]) { QueryScope = query };
        var plan = group.PlanAction(action.ActionId, Empty, scope);
        Assert.Empty(plan.Errors);
        var presence = Assert.Single(plan.Changes, change => change.ValueId.EndsWith("/presence", StringComparison.Ordinal));
        Assert.True(presence.Derived);
        Assert.Equal(role + "/" + localId + "/presence", presence.ValueId);
        Assert.Equal(FieldValue.Of(actionId == "items/remove" ? 0 : 2), presence.NewValue);
        Assert.DoesNotContain(plan.Changes, change => change.ValueId.StartsWith(role == "character" ? "profile/" : "character/", StringComparison.Ordinal));
        SelectionTestData.AssertBytes(group, before);
        Assert.False(group.ApplyActionPlan(action.ActionId, Empty, scope, plan with { Changes = [] }).Succeeded);
        SelectionTestData.AssertBytes(group, before);
        var applied = group.ApplyActionPlan(action.ActionId, Empty, scope, plan);
        Assert.True(applied.Succeeded);
        Assert.True(applied.StructureChanged);
        Assert.NotNull(applied.Revert);
        member = role == "character" ? group.Character : group.Profile;
        Assert.Equal(unselectedBytes, member.Inventory.Find(unselectedId).Field.Encode());
        var changedIds = member.Inventory.Entries.Select(entry => entry.Id).ToArray();
        foreach (var (container, originalIds) in idsByList)
        {
            var currentIds = member.Inventory.Entries
                .Where(entry => entry.List == container)
                .Select(entry => entry.Id)
                .ToArray();
            if (container != list)
            {
                Assert.Equal(originalIds, currentIds);
                continue;
            }

            if (actionId == "items/remove")
            {
                Assert.Equal(originalIds.Where(id => id != localId), currentIds);
                continue;
            }

            Assert.Equal(originalIds, currentIds.Take(originalIds.Length));
            Assert.Equal(originalIds.Length + 1, currentIds.Length);
            Assert.DoesNotContain(currentIds[^1], originalIds);
        }
        var changed = SelectionTestData.Bytes(group);
        using var fresh = SelectionTestData.Group(wonderlands);
        _ = fresh.RestoreMemento(group.CaptureMemento());
        SelectionTestData.AssertBytes(fresh, changed);
        Assert.Equal(changedIds, (role == "character" ? fresh.Character : fresh.Profile).Inventory.Entries.Select(entry => entry.Id));
        var redo = fresh.RestoreMemento(applied.Revert);
        SelectionTestData.AssertBytes(fresh, before);
        Assert.Equal(ids, (role == "character" ? fresh.Character : fresh.Profile).Inventory.Entries.Select(entry => entry.Id));
        _ = fresh.RestoreMemento(redo);
        SelectionTestData.AssertBytes(fresh, changed);
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void MemberRowAction_RejectsForeignCollectionSuffixesAndPreservesExplicitEmptyScopes(bool wonderlands, string list, string actionId)
    {
        using var group = SelectionTestData.Group(wonderlands);
        _ = group.BuildView();
        var role = list == GearboxInventory.Backpack ? "character" : "profile";
        var collection = role + "/" + list;
        var row = group.Query(collection, new() { Limit = 1 }).Rows[0].Key;
        var local = row[(collection.Length + 1)..];
        var id = role + "/" + actionId;
        var before = SelectionTestData.Bytes(group);
        Assert.NotEmpty(group.PlanAction(id, Empty, new()).Errors);
        Assert.Throws<KeyNotFoundException>(() => group.PlanAction(id, Empty, new(["foreign/" + list + "/" + local])));
        Assert.Throws<SaveFormatException>(() => group.PlanAction(id, Empty, new([role + "/ammo/" + local])));
        Assert.Throws<SaveFormatException>(() => group.PlanAction(id, Empty, new([row]) { QueryScope = new(role + "/catalogue", new(), 0) }));
        if (role == "profile")
        {
            var otherList = list == GearboxInventory.Bank ? GearboxInventory.LostLoot : GearboxInventory.Bank;
            Assert.Throws<SaveFormatException>(() => group.PlanAction(id, Empty, new([role + "/" + otherList + "/" + local])));
            Assert.Throws<SaveFormatException>(() => group.PlanAction(id, Empty,
                new([row]) { QueryScope = new(role + "/" + otherList, new(), 0) }));
            Assert.Throws<SaveFormatException>(() => group.PlanAction(id, Empty, new(["profile/ammo/0"])));
        }
        foreach (var scope in new ActionScope[] { new([]), new([]) { QueryScope = new(collection, new(), 0) { ExcludedRowKeys = [row] } } })
        {
            var plan = group.PlanAction(id, Empty, scope);
            Assert.Empty(plan.Errors);
            Assert.Empty(plan.Changes);
            var outcome = group.ApplyActionPlan(id, Empty, scope, plan);
            Assert.True(outcome.Succeeded);
            Assert.Null(outcome.Revert);
            Assert.False(outcome.StructureChanged);
        }
        SelectionTestData.AssertBytes(group, before);
    }
}
