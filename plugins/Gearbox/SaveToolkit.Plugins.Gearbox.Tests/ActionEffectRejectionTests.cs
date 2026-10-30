using System.Text.Json;
using SaveToolkit.Core.Plugins;
using SaveToolkit.Core.References;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;

namespace SaveToolkit.Plugins.Gearbox.Tests;

public sealed class ActionEffectRejectionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DestinationOwnershipCapacityAndEveryMovedIdentityAreValidatedBeforeAnyMutation(bool wonderlands)
    {
        using var group = ActionEffectData.Group(wonderlands);
        var before = ActionEffectData.Bytes(group);
        var cold = group.Character.Inventory.Capture();
        Assert.Throws<SaveFormatException>(() => group.Character.Inventory.EnsureCapacity(GearboxInventory.Bank, 1));
        Assert.Throws<SaveFormatException>(() => group.Profile.Inventory.ValidateDestination(GearboxInventory.Backpack,
            group.Character.Inventory.Find("item-0").Field));
        ActionEffectData.AssertIdentity(cold, group.Character);
        ActionEffectData.AssertBytes(before, group);
        var scope = new ActionScope(["character/inventory_items/item-0", "character/inventory_items/item-1"]);
        var plan = group.PlanAction("transfer/deposit", ActionEffectData.Empty, scope);
        Assert.Empty(plan.Errors);
        // Preserve a full reviewed projection: neither dropping a row nor changing its descriptor is accepted.
        foreach (var tampered in new[] { plan with { Changes = plan.Changes.Skip(1).ToArray() }, plan with
        { Changes = plan.Changes.Select((change, index) => index == 0 ? change with { Effect = null } : change).ToArray() } })
        {
            Assert.False(group.ApplyActionPlan("transfer/deposit", ActionEffectData.Empty, scope, tampered).Succeeded);
            ActionEffectData.AssertBytes(before, group);
            ActionEffectData.AssertIdentity(cold, group.Character);
        }
        group.Profile.Inventory.Append(GearboxInventory.Bank, group.Character.Inventory.Find("item-0").Field, "character:item-1");
        var collisionBytes = ActionEffectData.Bytes(group);
        Assert.NotEmpty(group.PlanAction("transfer/deposit", ActionEffectData.Empty, scope).Errors);
        Assert.False(group.ApplyActionPlan("transfer/deposit", ActionEffectData.Empty, scope, plan).Succeeded);
        ActionEffectData.AssertBytes(collisionBytes, group);
        var item = group.Profile.Inventory.Entries.First().Field;
        while (group.Profile.Root.All(GearboxInventory.Bank).Count() < GearboxWritePolicies.Capacity(GearboxInventory.Bank, 0))
            group.Profile.Inventory.Append(GearboxInventory.Bank, item);
        var full = ActionEffectData.Bytes(group);
        Assert.NotEmpty(group.PlanAction("transfer/deposit", ActionEffectData.Empty, new([scope.RowKeys![0]])).Errors);
        var noOp = group.PlanAction("transfer/deposit", ActionEffectData.Empty, new([]));
        Assert.Empty(noOp.Errors);
        Assert.Empty(noOp.Changes);
        Assert.Null(group.ApplyActionPlan("transfer/deposit", ActionEffectData.Empty, new([]), noOp).Revert);
        ActionEffectData.AssertBytes(full, group);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DuplicateProjectedIdentityAndWrongContainerNeverReachApply(bool wonderlands)
    {
        using var group = ActionEffectData.Group(wonderlands);
        group.Character.Inventory.Append(GearboxInventory.Backpack, group.Character.Inventory.Find("item-0").Field, "character:item-0");
        var before = ActionEffectData.Bytes(group);
        var duplicate = group.PlanAction("transfer/deposit", ActionEffectData.Empty,
            new(["character/inventory_items/item-0", "character/inventory_items/character:item-0"]));
        Assert.Contains("duplicate destination", Assert.Single(duplicate.Errors).Message, StringComparison.Ordinal);
        foreach (var key in new[] { "profile/inventory_items/item-0", "character/bank_inventory_list/item-0", "character/inventory_items/item-99" })
            Assert.NotEmpty(group.PlanAction("transfer/deposit", ActionEffectData.Empty, new([key])).Errors);
        ActionEffectData.AssertBytes(before, group);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void HostDiscardsMutatedCandidateOnExceptionOrMissingReviewedOutcomeRow(bool throws)
    {
        using var seed = MetadataR6Data.Workspace(ActionEffectData.Set(false));
        var observer = new SelectionGroupObserver();
        using var workspace = new Workspace("effect-failure", "Effects", seed.Set,
            new PluginCatalog([observer]), new ReferencePackStore([observer]));
        var file = workspace.Set.AllDiskFiles.Single(file => file.FileName == "1");
        var session = workspace.OpenGroup(Assert.Single(workspace.ProposeGroups(file.Id, "gearbox")).Token).Session;
        _ = session.BuildView();
        var before = ActionEffectData.Bytes(observer.Current);
        var identities = observer.Current.Character.Inventory.Capture();
        var parameters = new Dictionary<string, JsonElement>();
        var scope = new ActionScope(["character/inventory_items/item-0"]);
        var preview = session.PlanAction("transfer/deposit", parameters, scope, null, TestContext.Current.CancellationToken);
        Assert.Empty(preview.Plan.Errors);
        var state = session.State;
        observer.TransformActionOutcome = outcome =>
        {
            Assert.True(outcome.Succeeded);
            Assert.NotNull(outcome.Revert);
            if (throws) throw new InvalidOperationException("Authored failure after production mutation.");
            return outcome with { Changes = outcome.Changes.Skip(1).ToArray() };
        };
        var rejected = session.ApplyActionPlan(preview.PlanId!, preview.Revision, TestContext.Current.CancellationToken);
        Assert.False(rejected.Ok);
        Assert.Equal(state with { Revision = state.Revision + 1 }, session.State);
        ActionEffectData.AssertBytes(before, observer.Current);
        ActionEffectData.AssertIdentity(identities, observer.Current.Character);
        observer.TransformActionOutcome = null;
        var retry = session.PlanAction("transfer/deposit", parameters, scope, null, TestContext.Current.CancellationToken);
        Assert.Empty(retry.Plan.Errors);
        Assert.True(session.ApplyActionPlan(retry.PlanId!, retry.Revision, TestContext.Current.CancellationToken).Ok);
        var after = ActionEffectData.Bytes(observer.Current);
        Assert.True(session.Undo().Ok);
        Assert.False(session.State.CanUndo);
        ActionEffectData.AssertBytes(before, observer.Current);
        Assert.True(session.Redo().Ok);
        ActionEffectData.AssertBytes(after, observer.Current);
    }
}
