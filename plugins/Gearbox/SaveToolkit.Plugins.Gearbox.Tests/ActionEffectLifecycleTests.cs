using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;

namespace SaveToolkit.Plugins.Gearbox.Tests;

public sealed class ActionEffectLifecycleTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TransferRejectsStalePayloadAfterScalarRebindingWithoutAllocatingIdentities(bool wonderlands)
    {
        using var group = ActionEffectData.Group(wonderlands);
        var scope = new ActionScope(["character/inventory_items/item-0"]);
        var plan = group.PlanAction("transfer/deposit", ActionEffectData.Empty, scope);
        Assert.Empty(plan.Errors);
        Assert.Single(group.ResolveFieldMetadata(["character/item-0/level"]));
        Assert.True(group.ApplyEdits([new("character/item-0/level", FieldValue.Of(17))]).Succeeded);
        var before = Capture(group);
        var rejected = group.ApplyActionPlan("transfer/deposit", ActionEffectData.Empty, scope, plan);
        Assert.False(rejected.Succeeded);
        Assert.Null(rejected.Revert);
        AssertState(before, group);
        Assert.False(group.Character.Inventory.Capture().Initialized);
        Assert.False(group.Profile.Inventory.Capture().Initialized);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SerializationFailureAfterTransferRestoresBothMembersAndIdentityAllocation(bool wonderlands)
    {
        using var group = ActionEffectData.Group(wonderlands);
        // Inject a separate invalid new serial to fail the final serialization, after movement.
        // This deliberately bypasses supported writers; production writers reject it earlier.
        group.Profile.Inventory.SetSerial("item-0", [0xFF]);
        var characterBytes = group.Character.Root.Encode();
        var profileBytes = group.Profile.Root.Encode();
        var characterIdentity = group.Character.Inventory.Capture();
        var profileIdentity = group.Profile.Inventory.Capture();
        var scope = new ActionScope(["character/inventory_items/item-0", "character/inventory_items/item-1"]);
        var plan = group.PlanAction("transfer/deposit", ActionEffectData.Empty, scope);
        Assert.Empty(plan.Errors);
        Assert.Throws<SaveFormatException>(() => group.ApplyActionPlan("transfer/deposit", ActionEffectData.Empty, scope, plan));
        Assert.Equal(characterBytes, group.Character.Root.Encode());
        Assert.Equal(profileBytes, group.Profile.Root.Encode());
        ActionEffectData.AssertIdentity(characterIdentity, group.Character);
        ActionEffectData.AssertIdentity(profileIdentity, group.Profile);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RepeatedTransfersImportsAndRemovalsPreservePayloadsAndCompleteHistory(bool wonderlands)
    {
        using var group = ActionEffectData.Group(wonderlands);
        // Distinct payloads expose transfers/removals accidentally addressing a shifted row.
        group.Character.Inventory.SetSerial("item-0", SerialWrites.Set(ActionEffectData.Serial(wonderlands), wonderlands, "level", 17));
        group.Character.Inventory.SetSerial("item-1", SerialWrites.Set(ActionEffectData.Serial(wonderlands), wonderlands, "level", 19));
        var history = new List<DocumentMemento>();
        var states = new List<State> { Capture(group) };
        Apply("transfer/deposit", ["character/inventory_items/item-1", "character/inventory_items/item-0"]);
        Apply("transfer/withdraw", ["profile/bank_inventory_list/character:item-0"]);
        Apply("transfer/deposit", ["character/inventory_items/character:item-0"]);
        Apply("transfer/withdraw", ["profile/bank_inventory_list/character:item-1", "profile/bank_inventory_list/character:item-0"]);
        Apply("transfer/claim", ["profile/lost_loot_inventory_list/item-2"]);
        Apply("character/items/import", null, new Dictionary<string, FieldValue>
        { ["code"] = FieldValue.Of(Convert.ToBase64String(ActionEffectData.Serial(wonderlands))) });
        var created = group.Character.Inventory.Entries.Last().Id;
        Assert.StartsWith("item-", created, StringComparison.Ordinal);
        Apply("character/items/remove", ["character/inventory_items/" + created, "character/inventory_items/character:item-0"]);
        Assert.Equal(19, group.Character.Inventory.Info("character:item-1").Level);
        Assert.Equal(12, group.Character.Inventory.Info("profile:item-2").Level);

        var redo = new List<DocumentMemento>();
        for (var index = history.Count - 1; index >= 0; index--)
        {
            redo.Add(group.RestoreMemento(history[index]));
            AssertState(states[index], group);
        }
        for (var index = 0; index < history.Count; index++)
        {
            _ = group.RestoreMemento(redo[history.Count - index - 1]);
            AssertState(states[index + 1], group);
        }

        void Apply(string action, string[]? keys, IReadOnlyDictionary<string, FieldValue>? parameters = null)
        {
            parameters ??= ActionEffectData.Empty;
            var scope = new ActionScope(keys);
            var before = Capture(group);
            var plan = group.PlanAction(action, parameters, scope);
            Assert.Empty(plan.Errors);
            AssertState(before, group);
            var result = group.ApplyActionPlan(action, parameters, scope, plan);
            Assert.True(result.Succeeded);
            history.Add(Assert.IsAssignableFrom<DocumentMemento>(result.Revert));
            states.Add(Capture(group));
            AssertState(states.Last(), group);
        }
    }

    private static State Capture(GearboxGroupDocument group) => new(ActionEffectData.Bytes(group),
        group.Character.Inventory.Capture(), group.Profile.Inventory.Capture());

    private static void AssertState(State state, GearboxGroupDocument group)
    {
        ActionEffectData.AssertBytes(state.Bytes, group);
        ActionEffectData.AssertIdentity(state.Character, group.Character);
        ActionEffectData.AssertIdentity(state.Profile, group.Profile);
        foreach (var member in new[] { group.Character, group.Profile })
        {
            var entries = member.Inventory.Entries.ToArray();
            Assert.Equal(entries.Length, entries.Select(entry => entry.Id).Distinct(StringComparer.Ordinal).Count());
            foreach (var entry in entries)
            {
                var id = entry.Id + "/level";
                Assert.Single(member.ResolveFieldMetadata([id]));
                Assert.Equal(member.Inventory.Info(entry.Id).Level, member.GetValue(id).AsInt32());
            }
            var output = member.Serialize().Single();
            var container = GearboxContainer.Parse(output.Content.ToArray(), ActionEffectData.Title(member.Root.Schema.Wonderlands), member.Profile);
            Assert.Equal(output.Content.ToArray(), container.Encode(container.Decode()));
        }
    }

    private sealed record State(Dictionary<string, byte[]> Bytes, InventoryIdentity Character, InventoryIdentity Profile);
}
