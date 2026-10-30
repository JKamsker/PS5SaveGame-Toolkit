using System.Globalization;
using System.Text.Json;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;

namespace SaveToolkit.Plugins.Gearbox.Tests;

public sealed class ActionEffectPlanningTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void WarmOpeningBindingsSurviveScalarClonesWithoutReservingPersistentIdentities(bool wonderlands)
    {
        const int index = GearboxInventory.DirectLookupWorkLimit + 1;
        using var document = ActionEffectData.Document(wonderlands, profile: true, count: index + 1);
        _ = document.Inventory.Entries.ToArray();
        var identity = document.Inventory.Capture();
        Assert.False(identity.Initialized);
        var id = "item-" + index.ToString(CultureInfo.InvariantCulture) + "/level";
        Assert.Single(document.ResolveFieldMetadata([id]));
        var result = document.ApplyEdits([new(id, FieldValue.Of(13))]);
        Assert.True(result.Succeeded, string.Join("; ", result.Errors));
        Assert.Equal(13, document.GetValue(id).AsInt32());
        ActionEffectData.AssertIdentity(identity, document);
        _ = document.RestoreMemento(result.Revert!);
        _ = document.Inventory.Entries.ToArray();
        Assert.Single(document.ResolveFieldMetadata([id]));
        Assert.Equal(12, document.GetValue(id).AsInt32());
        ActionEffectData.AssertIdentity(identity, document);
    }

    [Theory]
    [InlineData(false, false, 2)]
    [InlineData(false, true, 2)]
    [InlineData(true, false, 2)]
    [InlineData(true, true, 2)]
    [InlineData(false, false, 0)]
    [InlineData(false, true, 0)]
    [InlineData(true, false, 0)]
    [InlineData(true, true, 0)]
    public void ImportColdWarmRejectedAndCancelledPlanningPreservesCompleteIdentityState(bool wonderlands, bool profile, int count)
    {
        using var document = ActionEffectData.Document(wonderlands, profile, count);
        var identity = document.Inventory.Capture();
        Assert.False(identity.Initialized);
        var bytes = ActionEffectData.Bytes(document);
        var parameters = new Dictionary<string, FieldValue> { ["code"] = FieldValue.Of(Convert.ToBase64String(ActionEffectData.Serial(wonderlands))) };
        var cold = document.PlanAction("items/import", parameters, new());
        Assert.Empty(cold.Errors);
        Assert.Equal(new[] { (profile ? GearboxInventory.Bank : GearboxInventory.Backpack) + "/count", "import/serial" }, cold.Changes.Select(c => c.ValueId));
        Assert.Null(cold.Changes[0].Effect);
        var effect = cold.Changes[1];
        Assert.False(effect.OldExists);
        Assert.True(effect.NewExists);
        Assert.True(effect.OldValue.IsNull);
        Assert.True(effect.NewValue.AsString().Length > 300);
        Assert.Equal(ActionEffectKind.StructuralMemento, effect.Effect!.Kind);
        Assert.True(effect.Effect.Spec.ReadOnly);
        Assert.Empty(document.ResolveFieldMetadata(["import/serial"]));
        Assert.Throws<KeyNotFoundException>(() => document.GetValue("import/serial"));
        _ = document.BuildView();
        var warm = document.PlanAction("items/import", parameters, new());
        Assert.Equal(JsonSerializer.Serialize(cold), JsonSerializer.Serialize(warm));
        Assert.NotEmpty(document.PlanAction("items/import", ActionEffectData.Empty, new()).Errors);
        ActionEffectData.AssertIdentity(identity, document);
        ActionEffectData.AssertBytes(bytes, document);
        var outcome = document.ApplyActionPlan("items/import", parameters, new(), cold);
        Assert.True(outcome.Succeeded);
        Assert.NotNull(outcome.Revert);
        var createdIdentity = document.Inventory.Capture();
        Assert.True(createdIdentity.Initialized);
        Assert.Equal(identity.NextId + count + (profile ? 2 : 1), createdIdentity.NextId);
        Assert.Empty(document.PlanAction("items/import", parameters, new()).Errors);
        ActionEffectData.AssertIdentity(createdIdentity, document);
        var redo = document.RestoreMemento(outcome.Revert!);
        ActionEffectData.AssertIdentity(identity, document);
        ActionEffectData.AssertBytes(bytes, document);
        _ = document.RestoreMemento(redo);
        ActionEffectData.AssertIdentity(createdIdentity, document);
    }

    [Theory]
    [InlineData(false, "transfer/deposit", "character/inventory_items/item-0")]
    [InlineData(true, "transfer/deposit", "character/inventory_items/item-0")]
    [InlineData(false, "transfer/withdraw", "profile/bank_inventory_list/item-0")]
    [InlineData(true, "transfer/withdraw", "profile/bank_inventory_list/item-0")]
    [InlineData(false, "transfer/claim", "profile/lost_loot_inventory_list/item-2")]
    [InlineData(true, "transfer/claim", "profile/lost_loot_inventory_list/item-2")]
    public void TransferColdWarmAndInvalidPlanningNeverReservesIdentities(bool wonderlands, string action, string key)
    {
        using var group = ActionEffectData.Group(wonderlands);
        var character = group.Character.Inventory.Capture();
        var profile = group.Profile.Inventory.Capture();
        var bytes = ActionEffectData.Bytes(group);
        var scope = new ActionScope([key]);
        var cold = group.PlanAction(action, ActionEffectData.Empty, scope);
        Assert.Empty(cold.Errors);
        _ = group.BuildView();
        var warm = group.PlanAction(action, ActionEffectData.Empty, scope);
        Assert.Equal(JsonSerializer.Serialize(cold), JsonSerializer.Serialize(warm));
        Assert.All(cold.Changes.Take(2), change => Assert.Equal(ActionEffectKind.StructuralMemento, change.Effect!.Kind));
        Assert.All(cold.Changes.Skip(2), change => Assert.Null(change.Effect));
        Assert.NotEmpty(group.PlanAction(action, ActionEffectData.Empty, new([key, key])).Errors);
        Assert.NotEmpty(group.PlanAction(action, ActionEffectData.Empty, new([key.Replace("item-", "foreign-", StringComparison.Ordinal)])).Errors);
        Assert.Empty(group.PlanAction(action, ActionEffectData.Empty, new([])).Changes);
        ActionEffectData.AssertIdentity(character, group.Character);
        ActionEffectData.AssertIdentity(profile, group.Profile);
        ActionEffectData.AssertBytes(bytes, group);
        var result = group.ApplyActionPlan(action, ActionEffectData.Empty, scope, cold);
        Assert.True(result.Succeeded);
        var after = ActionEffectData.Bytes(group);
        var movedIds = group.Character.Inventory.Entries.Concat(group.Profile.Inventory.Entries).Select(entry => entry.Id).ToArray();
        var redo = group.RestoreMemento(result.Revert!);
        ActionEffectData.AssertIdentity(character, group.Character);
        ActionEffectData.AssertIdentity(profile, group.Profile);
        ActionEffectData.AssertBytes(bytes, group);
        _ = group.RestoreMemento(redo);
        ActionEffectData.AssertBytes(after, group);
        Assert.Equal(movedIds, group.Character.Inventory.Entries.Concat(group.Profile.Inventory.Entries).Select(entry => entry.Id));
    }
}
