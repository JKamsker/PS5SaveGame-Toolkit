using System.Buffers.Binary;
using System.Text.Json;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;

namespace SaveToolkit.Plugins.Witcher3.Tests;

public sealed class WitcherRefillTests
{
    [Fact]
    public void EverySampleRefillsExistingStacksWithExactByteDiffReopenAndSingleUndoRedo()
    {
        foreach (var file in EnabledWriteFixture.Files())
        {
            var input = file.ReadAllBytes();
            var before = EnabledWriteFixture.Read(input);
            var food = before.Player.Inventory!.Items.Where(i => i.Name is "Bread" or "Bottled water")
                .Where(i => !i.Equipped && i.Slot.Length == 0 && i.Tags.Count == 0).ToArray();
            Assert.NotEmpty(food);
            Assert.All(food, i => Assert.True(i.Quantity > 1));
            var plugin = new Witcher3Plugin();
            using var document = plugin.Open(file);
            using var session = new DocumentSession("refill", file, plugin, document);
            _ = session.BuildView();
            _ = session.QueryCollection(InventoryViews.CollectionId, new() { Limit = 200 });
            Assert.True(session.ApplyEdits(food.Select(i => EnabledWriteFixture.Incoming(i.ValueId, FieldValue.Of(1))).ToArray()).Ok);
            var reduced = Assert.Single(document.Serialize()).Content.ToArray();
            var scope = new ActionScope { QueryScope = new(InventoryViews.CollectionId, new() { Limit = 1 }, session.State.Revision) };
            var preview = session.PlanAction(RefillPlanning.ActionId, new Dictionary<string, JsonElement>(), scope);
            Assert.NotNull(preview.PlanId);
            Assert.Empty(preview.Plan.Errors);
            Assert.Equal(food.Length, preview.Plan.Changes.Count);
            Assert.NotEmpty(preview.Plan.SkippedRows);
            Assert.Contains(preview.Plan.Warnings, w => w.Contains("game may reject", StringComparison.Ordinal));
            Assert.Equal(reduced, Assert.Single(document.Serialize()).Content.ToArray());
            Assert.True(session.ApplyActionPlan(preview.PlanId, preview.Revision).Ok);
            var output = Assert.Single(document.Serialize()).Content.ToArray();
            Assert.Equal(input, output);
            var expected = WitcherContainer.Parse(reduced).Payload.ToArray();
            foreach (var item in food) { BinaryPrimitives.WriteUInt16LittleEndian(expected.AsSpan(item.QuantityOffset), item.OriginalQuantity); }
            Assert.Equal(expected, WitcherContainer.Parse(output).Payload);
            EnabledWriteFixture.SemanticChanges(reduced, output, food.Select(i => $"entity/{before.Player.EntityId}/{i.ValueId}").ToArray());
            Assert.True(session.Undo().Ok);
            Assert.Equal(reduced, Assert.Single(document.Serialize()).Content.ToArray());
            Assert.True(session.Redo().Ok);
            Assert.Equal(input, Assert.Single(document.Serialize()).Content.ToArray());
            // Reopening independently confirms the actual persisted native quantities.
            var reopened = EnabledWriteFixture.Read(output);
            Assert.All(food, i => Assert.Equal(i.OriginalQuantity, reopened.Player.Inventory!.Items.Single(n => n.Id == i.Id).Quantity));
        }
    }

    [Fact]
    public void RefillRejectsStaleForgedInvalidAndDirectRequestsAndHonorsExplicitSelection()
    {
        var file = EnabledWriteFixture.Files()[0];
        using var document = new Witcher3Plugin().Open(file);
        _ = document.BuildView();
        var items = EnabledWriteFixture.Read(file.ReadAllBytes()).Player.Inventory!.Items;
        var bread = items.Single(i => i.Name == "Bread");
        var water = items.Single(i => i.Name == "Bottled water");
        var planner = (IActionPlanningDocument)document;
        var parameters = new Dictionary<string, FieldValue>();
        var scope = new ActionScope([InventoryActionSelection.Key(bread)]);
        Assert.True(document.ApplyEdits([new(bread.ValueId, FieldValue.Of(1)), new(water.ValueId, FieldValue.Of(1))]).Succeeded);
        var preview = planner.PlanAction(RefillPlanning.ActionId, parameters, scope);
        Assert.Single(preview.Changes);
        var forged = preview with { Changes = [preview.Changes[0] with { NewValue = FieldValue.Of(65535) }] };
        Assert.False(planner.ApplyActionPlan(RefillPlanning.ActionId, parameters, scope, forged).Succeeded);
        Assert.True(planner.ApplyActionPlan(RefillPlanning.ActionId, parameters, scope, preview).Succeeded);
        Assert.Equal(bread.OriginalQuantity, document.GetValue(bread.ValueId).AsInteger());
        Assert.Equal(1, document.GetValue(water.ValueId).AsInteger());
        Assert.False(planner.ApplyActionPlan(RefillPlanning.ActionId, parameters, scope, preview).Succeeded);
        Assert.Empty(planner.PlanAction(RefillPlanning.ActionId, parameters, new([])).Changes);
        Assert.NotEmpty(planner.PlanAction(RefillPlanning.ActionId, parameters, new(["unknown"])).Errors);
        Assert.NotEmpty(planner.PlanAction(RefillPlanning.ActionId, parameters, new([scope.RowKeys![0], scope.RowKeys[0]])).Errors);
        Assert.NotEmpty(planner.PlanAction(RefillPlanning.ActionId, new Dictionary<string, FieldValue> { ["x"] = FieldValue.Of(1) }, scope).Errors);
        Assert.NotEmpty(planner.PlanAction(RefillPlanning.ActionId, parameters, new() { QueryScope = new("wrong", new(), 0) }).Errors);
        Assert.Throws<KeyNotFoundException>(() => document.InvokeAction(RefillPlanning.ActionId, parameters));
    }

    [Fact]
    public void RefillProtectsQuestVariantsTaggedEquippedCurrencyAndHigherQuantities()
    {
        InventoryItem[] items = [new("Bread", 1, 0, 5, false), new("Bread", 2, 0, 5, true),
            new("q401_bread", 3, 0, 5, false), new("Crowns", 4, 0, 5, false),
            new("Bread", 5, 0, 5, false) { Tags = ["quest"] }, new("Bread", 6, 0, 5, false) { Slot = "food" }];
        foreach (var item in items) { item.Quantity = 1; }
        var plan = new RefillPlanning(items, true).Plan(new Dictionary<string, FieldValue>(), new());
        Assert.Empty(plan.Errors);
        var change = Assert.Single(plan.Changes);
        Assert.Equal(items[0].ValueId, change.ValueId);
        Assert.Equal(FieldValue.Of(1), change.OldValue);
        Assert.Equal(FieldValue.Of(5), change.NewValue);
        Assert.Equal(["2", "3", "4", "5", "6"], plan.SkippedRows.Select(row => row.RowKey));
        Assert.All(plan.SkippedRows, row => Assert.NotEmpty(row.Reason));
        Assert.All(items, item => Assert.Equal(1, item.Quantity));
        items[0].Quantity = 8;
        Assert.Empty(new RefillPlanning(items, true).Plan(new Dictionary<string, FieldValue>(), new()).Changes);
        Assert.NotEmpty(new RefillPlanning(items, false).Plan(new Dictionary<string, FieldValue>(), new()).Errors);
    }
}
