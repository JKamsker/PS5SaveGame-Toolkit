using System.Buffers.Binary;
using System.Globalization;
using System.Text.Json;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.Witcher3.Tests;

public sealed class WitcherRepairPlanningTests
{
    [Fact]
    public void RepairPreviewIncludesSkippedRowsAndOneUndoRestoresTheWholeBatchOnEverySample()
    {
        foreach (var file in EnabledWriteFixture.Files())
        {
            var input = file.ReadAllBytes();
            var before = EnabledWriteFixture.Read(input);
            var items = before.Player.Inventory!.Items;
            var damaged = items.Where(i => i.CanEditDurability).Take(2).ToArray();
            Assert.Equal(2, damaged.Length);
            var plugin = new Witcher3Plugin();
            using var document = plugin.Open(file);
            using var session = new DocumentSession("repair-planning", file, plugin, document);
            _ = session.BuildView();
            _ = session.QueryCollection(InventoryViews.CollectionId, new() { Limit = 200 });
            Assert.True(session.ApplyEdits(damaged.Select((i, index) => EnabledWriteFixture.Incoming(i.DurabilityId,
                FieldValue.Of(20.0 + index))).ToArray()).Ok);
            var damagedBytes = Assert.Single(document.Serialize()).Content.ToArray();
            var preview = session.PlanAction(RepairPlanning.ActionId, new Dictionary<string, JsonElement>());
            Assert.NotNull(preview.PlanId);
            Assert.Empty(preview.Plan.Errors);
            Assert.True(preview.Plan.Changes.Count >= 2);
            Assert.NotEmpty(preview.Plan.SkippedRows);
            Assert.Contains(preview.Plan.Warnings, w => w.Contains("Not validated on console", StringComparison.Ordinal));
            Assert.All(preview.Plan.Changes, c => Assert.Equal(FieldValue.Of(100.0), c.NewValue));
            Assert.Equal(damagedBytes, Assert.Single(document.Serialize()).Content.ToArray());
            var outcome = session.ApplyActionPlan(preview.PlanId, preview.Revision);
            Assert.True(outcome.Ok);
            Assert.Equal(preview.Plan.Changes.Count, outcome.Changes.Count);
            Assert.Equal(preview.Plan.Warnings, outcome.Warnings);
            var repairedBytes = Assert.Single(document.Serialize()).Content.ToArray();
            var expected = WitcherContainer.Parse(damagedBytes).Payload.ToArray();
            foreach (var change in preview.Plan.Changes)
            {
                var item = items.Single(i => i.DurabilityId == change.ValueId);
                BinaryPrimitives.WriteSingleLittleEndian(expected.AsSpan(item.DurabilityOffset), 100);
            }
            Assert.Equal(expected, WitcherContainer.Parse(repairedBytes).Payload);
            EnabledWriteFixture.SemanticChanges(damagedBytes, repairedBytes,
                preview.Plan.Changes.Select(c => $"entity/{before.Player.EntityId}/{c.ValueId}").ToArray());
            Assert.True(session.Undo().Ok);
            Assert.Equal(damagedBytes, Assert.Single(document.Serialize()).Content.ToArray());
            Assert.True(session.Redo().Ok);
            Assert.Equal(repairedBytes, Assert.Single(document.Serialize()).Content.ToArray());
            Assert.True(session.Undo().Ok);
            Assert.True(session.Undo().Ok);
            Assert.Equal(input, Assert.Single(document.Serialize()).Content.ToArray());
        }
    }

    [Fact]
    public void RepairRejectsForgedStaleAndInvalidRequestsAndRetainsSentinelRecords()
    {
        var file = EnabledWriteFixture.Files()[0];
        var before = EnabledWriteFixture.Read(file.ReadAllBytes());
        using var document = new Witcher3Plugin().Open(file);
        _ = document.BuildView();
        var queryable = (IQueryableDocument)document;
        var page = queryable.Query(InventoryViews.CollectionId, new() { Limit = 200 });
        var sentinel = before.Player.Inventory!.Items.First(i => i.OriginalDurability == -1);
        var sentinelField = Assert.IsType<FieldNode>(page.Rows.Single(r => r.Key == Key(sentinel)).Cells["durability"]);
        Assert.True(sentinelField.Spec.ReadOnly);
        Assert.False(document.ApplyEdits([new(sentinelField.ValueId, FieldValue.Of(100.0))]).Succeeded);
        var planner = Assert.IsAssignableFrom<IActionPlanningDocument>(document);
        var parameters = new Dictionary<string, FieldValue>();
        var empty = planner.PlanAction(RepairPlanning.ActionId, parameters, new([]));
        Assert.Empty(empty.Changes);
        Assert.Empty(empty.SkippedRows);
        Assert.True(planner.ApplyActionPlan(RepairPlanning.ActionId, parameters, new([]), empty).Succeeded);
        var skipped = planner.PlanAction(RepairPlanning.ActionId, parameters, new([Key(sentinel)]));
        Assert.Single(skipped.SkippedRows);
        Assert.Empty(skipped.Changes);
        Assert.NotEmpty(planner.PlanAction(RepairPlanning.ActionId, parameters, new(["unknown"])).Errors);
        Assert.NotEmpty(planner.PlanAction(RepairPlanning.ActionId, parameters, new([Key(sentinel), Key(sentinel)])).Errors);
        Assert.NotEmpty(planner.PlanAction(RepairPlanning.ActionId, new Dictionary<string, FieldValue> { ["unexpected"] = FieldValue.Of(1) }, new()).Errors);
        Assert.NotEmpty(planner.PlanAction(RepairPlanning.ActionId, parameters, new() {
            QueryScope = new("wrong-collection", new(), 0),
        }).Errors);
        Assert.Throws<KeyNotFoundException>(() => planner.PlanAction("unknown", parameters, new()));
        Assert.Throws<KeyNotFoundException>(() => document.InvokeAction(RepairPlanning.ActionId, parameters));
        var item = before.Player.Inventory.Items.First(i => i.CanEditDurability);
        Assert.True(document.ApplyEdits([new(item.DurabilityId, FieldValue.Of(20.0))]).Succeeded);
        var scope = new ActionScope([Key(item)]);
        var preview = planner.PlanAction(RepairPlanning.ActionId, parameters, scope);
        Assert.Single(preview.Changes);
        var forged = preview with { Changes = [preview.Changes[0] with { NewValue = FieldValue.Of(99.0) }] };
        Assert.False(planner.ApplyActionPlan(RepairPlanning.ActionId, parameters, scope, forged).Succeeded);
        Assert.Equal(20, document.GetValue(item.DurabilityId).AsDouble());
        Assert.True(document.ApplyEdits([new(item.DurabilityId, FieldValue.Of(30.0))]).Succeeded);
        Assert.False(planner.ApplyActionPlan(RepairPlanning.ActionId, parameters, scope, preview).Succeeded);
        Assert.Equal(30, document.GetValue(item.DurabilityId).AsDouble());
    }

    [Fact]
    public void QuerySelectionRepairsUnloadedRowsAndCoreRejectsDirectInvocationAndStaleTokens()
    {
        var file = EnabledWriteFixture.Files()[0];
        var plugin = new Witcher3Plugin();
        using var document = plugin.Open(file);
        using var session = new DocumentSession("repair-query", file, plugin, document);
        _ = session.BuildView();
        var before = EnabledWriteFixture.Read(file.ReadAllBytes());
        var item = before.Player.Inventory!.Items.First(i => i.CanEditDurability);
        // Query only this item; Core must resolve the later full selection, including unloaded rows.
        _ = session.QueryCollection(InventoryViews.CollectionId, new() {
            Filters = new Dictionary<string, string> { ["raw"] = item.Name }, Limit = 1,
        });
        Assert.True(session.ApplyEdits([EnabledWriteFixture.Incoming(item.DurabilityId, FieldValue.Of(25.0))]).Ok);
        var scope = new ActionScope { QueryScope = new(InventoryViews.CollectionId, new() { Limit = 1 }, session.State.Revision) };
        var preview = session.PlanAction(RepairPlanning.ActionId, new Dictionary<string, JsonElement>(), scope);
        Assert.NotNull(preview.PlanId);
        Assert.Equal(before.Player.Inventory.Items.Count, preview.Scope.RowKeys!.Count);
        Assert.Contains(preview.Plan.Changes, c => c.ValueId == item.DurabilityId);
        Assert.False(session.InvokeAction(RepairPlanning.ActionId, new Dictionary<string, JsonElement>()).Ok);
        Assert.True(session.ApplyEdits([EnabledWriteFixture.Incoming(item.DurabilityId, FieldValue.Of(26.0))]).Ok);
        Assert.True(session.ApplyActionPlan(preview.PlanId, preview.Revision).Stale);
        var fresh = session.PlanAction(RepairPlanning.ActionId, new Dictionary<string, JsonElement>(), scope with {
            QueryScope = scope.QueryScope with { Revision = session.State.Revision },
        });
        Assert.True(session.ApplyActionPlan(fresh.PlanId!, fresh.Revision).Ok);
        Assert.Equal(100, document.GetValue(item.DurabilityId).AsDouble());
    }

    private static string Key(InventoryItem item) => item.Id.ToString(CultureInfo.InvariantCulture);
}
