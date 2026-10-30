using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;

namespace SaveToolkit.Plugins.Gearbox.Tests;

internal static class MetadataR7Data
{
    internal const string Money = "currency/618814354";
    internal static GearboxDocument Character(bool wonderlands) => EditCoverageData.Create(wonderlands, root =>
    {
        foreach (var (field, index) in root.All(GearboxInventory.Backpack).Take(2).Select((field, index) => (field, index)))
            field.Message!.Last("item_serial_number")!.Set(SerialWrites.Set(InventoryEntry.SerialOf(field), wonderlands, "level", 10 + index * 10));
    });

    internal static void Prepare(GearboxDocument document)
    {
        _ = document.Category("inventory_category_list", 618814354, "Money", 2_000_000_000, Money);
        Assert.True(document.ApplyEdits([new(Money, FieldValue.Of(100))]).Succeeded);
        var scope = new ActionScope(["item-0", "item-1"]);
        var plan = document.PlanAction("items/duplicate", new Dictionary<string, FieldValue>(), scope);
        Assert.Empty(plan.Errors);
        Assert.True(document.ApplyActionPlan("items/duplicate", new Dictionary<string, FieldValue>(), scope, plan).Succeeded);
        Assert.Equal(5, document.Inventory.Entries.Count());
        Assert.Empty(Assert.Single(document.Root.All("inventory_category_list")).Original);
    }

    internal static ValueEdit[] Edits(bool moneyFirst, string prefix = "")
    {
        ValueEdit money = new(prefix + Money, FieldValue.Of(0));
        ValueEdit item = new(prefix + "item-3/level", FieldValue.Of(15));
        return moneyFirst ? [money, item] : [item, money];
    }

    internal static void AssertChanges(EditOutcome outcome, string prefix = "")
    {
        Assert.True(outcome.Succeeded);
        var item = Assert.Single(outcome.Changes, change => change.ValueId == prefix + "item-3/level");
        Assert.Equal(FieldValue.Of(10), item.OldValue);
        Assert.Equal(FieldValue.Of(15), item.NewValue);
        Assert.DoesNotContain(outcome.Changes, change => change.ValueId == prefix + "item-4/level");
        var money = Assert.Single(outcome.Changes, change => change.ValueId == prefix + Money);
        Assert.Equal(FieldValue.Of(100), money.OldValue);
        Assert.Equal(FieldValue.Of(0), money.NewValue);
    }

    internal static void AssertWholeOutput(byte[] before, byte[] changed, bool wonderlands)
    {
        var container = GearboxContainer.Parse(before, wonderlands ? "PPSA01493" : "PPSA01463", false);
        var expected = container.Decode();
        expected.Remove(Assert.Single(expected.All("inventory_category_list")));
        var intended = expected.All(GearboxInventory.Backpack).ElementAt(3);
        intended.Message!.Last("item_serial_number")!.Set(SerialWrites.Set(InventoryEntry.SerialOf(intended), wonderlands, "level", 15));
        Assert.Equal(container.Encode(expected), changed);
    }

    internal static void AssertReopened(byte[] output, bool wonderlands, byte[] sibling)
    {
        var root = GearboxContainer.Parse(output, wonderlands ? "PPSA01493" : "PPSA01463", false).Decode();
        Assert.Empty(root.All("inventory_category_list"));
        Assert.Equal(5, root.All(GearboxInventory.Backpack).Count());
        var intended = root.All(GearboxInventory.Backpack).ElementAt(3);
        var actualSibling = root.All(GearboxInventory.Backpack).ElementAt(4);
        Assert.Equal(sibling, actualSibling.Encode());
        foreach (var (field, level) in new[] { (intended, 15), (actualSibling, 20) })
        {
            _ = IndependentSerialProbe.Decode(InventoryEntry.SerialOf(field));
            Assert.Equal(level, new SerialReader(wonderlands).Read(InventoryEntry.SerialOf(field)).Level);
        }
    }

    internal static void UnknownTail(ProtoMessage message)
    {
        for (var index = 0; index < 5000; index++) message.Add(new ProtoField(1900, 0, null, [], 0UL, null));
    }
}
