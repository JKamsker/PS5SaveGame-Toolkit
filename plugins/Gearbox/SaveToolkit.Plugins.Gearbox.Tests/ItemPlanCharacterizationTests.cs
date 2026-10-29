using SaveToolkit.Sdk;
using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Values;

namespace SaveToolkit.Plugins.Gearbox.Tests;

public sealed class ItemPlanCharacterizationTests
{
    private const string StructuralWarning = "Removal unequips matching slots and rebases surviving equipment indices. Import assigns seen/pickup metadata; duplicates retain existing metadata except for a new backpack pickup order. Account cloud mirrors are not reconciled.";
    private static readonly string[] LevelIds = ["item-0/level", "item-2/level"];
    private static readonly string[] PresenceIds = ["item-0/presence", "item-2/presence"];
    private static readonly string[] RemovalIds = [.. PresenceIds, "slots/1/index", "slots/2/index", "slots/3/index", "slots/4/index"];
    private static readonly long[] RemovedValues = [0, 0, -1, 0, -1, 1];

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SelectedLevelAndDuplicatePlans_KeepStoredOrderSkipsWarningsAndBytes(bool wonderlands)
    {
        using var document = Create(wonderlands);
        var before = document.Serialize().Single().Content.ToArray();
        var entries = document.Inventory.Entries.ToArray();
        var scope = new ActionScope(entries.Reverse().Select(e => e.Id).ToArray());
        var parameters = new Dictionary<string, FieldValue> { ["level"] = FieldValue.Of(17) };

        var levels = document.PlanAction("items/level", parameters, scope);

        Assert.Empty(levels.Errors);
        Assert.Equal(LevelIds, levels.Changes.Select(c => c.ValueId));
        Assert.All(levels.Changes, change =>
        {
            Assert.Equal(FieldValue.Of(12), change.OldValue);
            Assert.Equal(FieldValue.Of(17), change.NewValue);
            Assert.False(change.Derived);
        });
        Assert.Equal(new SkippedActionRow("item-1", "This serial property has no safely located bit range; original bytes are preserved."), Assert.Single(levels.SkippedRows));
        Assert.Equal(new[] { SerialWrites.Warning, StructuralWarning }, levels.Warnings);

        var duplicates = document.PlanAction("items/duplicate", new Dictionary<string, FieldValue>(), scope);

        Assert.Empty(duplicates.Errors);
        Assert.Equal(PresenceIds, duplicates.Changes.Select(c => c.ValueId));
        Assert.All(duplicates.Changes, change =>
        {
            Assert.Equal(FieldValue.Of(1), change.OldValue);
            Assert.Equal(FieldValue.Of(2), change.NewValue);
        });
        Assert.Equal(new SkippedActionRow("item-1", "Cannot create an item with an unknown or invalid layout."), Assert.Single(duplicates.SkippedRows));
        Assert.Equal(levels.Warnings, duplicates.Warnings);
        Assert.Equal(before, document.Serialize().Single().Content.ToArray());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RemovePlan_KeepsPresenceBeforeDerivedSlotChangesAndDoesNotMutate(bool wonderlands)
    {
        using var document = Create(wonderlands);
        foreach (var index in new[] { -1, 0, 1, 2, 3 })
            GearboxTestData.AddMessage(document.Root, "equipped_inventory_list").SetInteger("inventory_list_index", index);
        var before = document.Serialize().Single().Content.ToArray();

        var plan = document.PlanAction("items/remove", new Dictionary<string, FieldValue>(), new(["item-2", "item-0"]));

        Assert.Empty(plan.Errors);
        Assert.Empty(plan.SkippedRows);
        Assert.Equal(RemovalIds, plan.Changes.Select(c => c.ValueId));
        Assert.Equal(new long[] { 1, 1, 0, 1, 2, 3 }, plan.Changes.Select(c => c.OldValue.AsInt64()));
        Assert.Equal(RemovedValues, plan.Changes.Select(c => c.NewValue.AsInt64()));
        Assert.All(plan.Changes.Take(2), change => Assert.False(change.Derived));
        Assert.All(plan.Changes.Skip(2), change => Assert.True(change.Derived));
        Assert.Equal(new[] { SerialWrites.Warning, StructuralWarning }, plan.Warnings);
        Assert.Equal(before, document.Serialize().Single().Content.ToArray());
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void ImportPlan_KeepsCountThenSerialAndScopeValidationPrecedence(bool wonderlands, bool profile)
    {
        using var document = Create(wonderlands, profile);
        var before = document.Serialize().Single().Content.ToArray();
        var code = Convert.ToBase64String(document.Inventory.Entries.First().Serial);
        var parameters = new Dictionary<string, FieldValue> { ["code"] = FieldValue.Of("BL3(" + code + ")") };

        var plan = document.PlanAction("items/import", parameters, new());

        Assert.Empty(plan.Errors);
        Assert.Empty(plan.SkippedRows);
        Assert.Equal(new[] { (profile ? GearboxInventory.Bank : GearboxInventory.Backpack) + "/count", "import/serial" }, plan.Changes.Select(c => c.ValueId));
        Assert.Equal(FieldValue.Of(3), plan.Changes[0].OldValue);
        Assert.Equal(FieldValue.Of(4), plan.Changes[0].NewValue);
        Assert.Equal(FieldValue.Null, plan.Changes[1].OldValue);
        Assert.Equal(FieldValue.Of(code), plan.Changes[1].NewValue);
        Assert.False(plan.Changes[1].OldExists);
        Assert.Equal(new[] { SerialWrites.Warning, StructuralWarning }, plan.Warnings);
        var rejected = document.PlanAction("items/import", new Dictionary<string, FieldValue>(), new([]));
        Assert.Equal("Import uses document scope.", Assert.Single(rejected.Errors).Message);
        Assert.Empty(rejected.Changes);
        Assert.Equal(new[] { SerialWrites.Warning }, rejected.Warnings);
        Assert.Equal(before, document.Serialize().Single().Content.ToArray());
    }

    private static GearboxDocument Create(bool wonderlands, bool profile = false)
    {
        var title = wonderlands ? "PPSA01493" : "PPSA01463";
        var container = GearboxContainer.Parse(GearboxTestData.CreateContainer(wonderlands, profile), title, profile);
        var root = container.Decode();
        var list = profile ? GearboxInventory.Bank : GearboxInventory.Backpack;
        var spec = root.Schema.Spec(root.Type, list);
        var serial = IndependentSerialProbe.Encode(SyntheticItem.Payload(wonderlands), wonderlands ? 5 : 4, -1);
        foreach (var bytes in new[] { serial, new byte[] { 4, 0 }, serial })
        {
            if (root.Schema.IsMessage(spec.Type))
            {
                var item = GearboxTestData.AddMessage(root, list);
                item.Add(new ProtoField(root.Schema.Tag(item.Type, "item_serial_number"), 2,
                    root.Schema.Spec(item.Type, "item_serial_number"), [], bytes, null));
            }
            else root.Add(new ProtoField(root.Schema.Tag(root.Type, list), 2, spec, [], bytes, null));
        }
        return new GearboxDocument(profile ? "profile" : "1", title, container, root, IReferenceContext.Empty);
    }
}
