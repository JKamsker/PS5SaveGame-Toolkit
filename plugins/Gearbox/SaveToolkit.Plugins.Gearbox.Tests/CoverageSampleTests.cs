using System.Text.Json;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.Gearbox.Tests;

public sealed class CoverageSampleTests
{
    [Theory]
    [InlineData("PPSA01463", "items/match-level")]
    [InlineData("PPSA01493", "items/match-level")]
    [InlineData("PPSA01463", "items/mark")]
    [InlineData("PPSA01493", "items/mark")]
    [InlineData("PPSA01463", "ammo/restore-imported")]
    [InlineData("PPSA01493", "ammo/restore-imported")]
    public void HostPreviewCancelApplyExportUndoRedo_UsesRealSamples(string title, string action)
    {
        var plugin = new GearboxPlugin();
        var file = GearboxTestData.PrimaryCharacter(title);
        using var document = (GearboxDocument)plugin.Open(file);
        using var session = new DocumentSession("coverage", file, plugin, document);
        _ = session.BuildView();
        Assert.Equal(file.ReadAllBytes(), document.Serialize().Single().Content.ToArray());
        var parameters = new Dictionary<string, JsonElement>();
        ActionScope scope;
        if (action == "ammo/restore-imported")
        {
            _ = session.QueryCollection("ammo", new() { Limit = 200 }, expectedRevision: null, cancellationToken: TestContext.Current.CancellationToken);
            var index = document.Root.Children("resource_pools").Select((pool, i) => (pool, i))
                .First(p => p.pool.Float("amount") > 0 && GearboxAmmoRestore.Eligible(p.pool.Float("amount")) &&
                    SerialDatabase.For(title == "PPSA01493").Find(ItemCatalogs.Labels, p.pool.Text("resource_path"))?.Category == "ammo").i;
            Assert.True(session.ApplyEdits([new("ammo/" + index, JsonSerializer.SerializeToElement("0"))], expectedRevision: null, cancellationToken: TestContext.Current.CancellationToken).Ok);
            scope = new([index.ToString(System.Globalization.CultureInfo.InvariantCulture)]);
        }
        else
        {
            var entry = document.Inventory.Entries.First(e => document.Inventory.Info(e.Id).Complete &&
                e.Field.Message is { } message && GearboxItemMarks.Supported(message));
            scope = new([entry.Id]);
            if (action == "items/match-level")
                Assert.True(session.ApplyEdits([new("character/level", JsonSerializer.SerializeToElement("17"))], expectedRevision: null, cancellationToken: TestContext.Current.CancellationToken).Ok);
            else parameters["mark"] = JsonSerializer.SerializeToElement(GearboxItemMarks.Get(entry.Field.Message!) == 1 ? "2" : "1");
            _ = session.QueryCollection("inventory_items", new() { Limit = 200 }, expectedRevision: null, cancellationToken: TestContext.Current.CancellationToken);
            _ = session.Expand("detail/" + entry.Id);
            if (action == "items/match-level")
                scope = new() { QueryScope = new("inventory_items", new() { Limit = 1 }, session.State.Revision) };
        }
        var before = document.Serialize().Single().Content.ToArray();
        var originalRoot = document.Root.Clone();
        var cancelled = session.PlanAction(action, parameters, scope, expectedRevision: null, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Empty(cancelled.Plan.Errors);
        Assert.Equal(before, document.Serialize().Single().Content.ToArray());
        var preview = session.PlanAction(action, parameters, scope, expectedRevision: null, cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotEmpty(preview.Plan.Changes);
        Assert.True(session.ApplyActionPlan(preview.PlanId!, preview.Revision, cancellationToken: TestContext.Current.CancellationToken).Ok);
        var after = session.Serialize(cancellationToken: TestContext.Current.CancellationToken).Single().Content.ToArray();
        using var reopened = (GearboxDocument)plugin.Open(GearboxTestData.Overlay(file, after));
        Assert.Equal(after, reopened.Serialize().Single().Content.ToArray());
        SerialWriteTests.AssertUnrelated(originalRoot, reopened.Root, action == "ammo/restore-imported" ? "resource_pools" : "inventory_items");
        foreach (var entry in reopened.Inventory.Entries)
        {
            var prior = originalRoot.Children("inventory_items").ElementAt(entry.Index).Last("item_serial_number")!.Value as byte[];
            Assert.NotNull(prior);
            if (!entry.Serial.AsSpan().SequenceEqual(prior))
            {
                _ = IndependentSerialProbe.Decode(entry.Serial);
                Assert.True(reopened.Inventory.Info(entry.Id).ChecksumValid);
            }
            if (action == "items/mark") Assert.Equal(prior, entry.Serial);
        }
        Assert.True(session.Undo().Ok);
        Assert.Equal(before, document.Serialize().Single().Content.ToArray());
        Assert.True(session.Redo().Ok);
        Assert.Equal(after, document.Serialize().Single().Content.ToArray());
        Assert.False(session.ApplyActionPlan(preview.PlanId!, preview.Revision, cancellationToken: TestContext.Current.CancellationToken).Ok);
    }

    [Theory]
    [InlineData("PPSA01463")]
    [InlineData("PPSA01493")]
    public void GroupedActions_LocalizePlansAndKeepOpeningAmmoAcrossFreshHistoryMembers(string title)
    {
        var plugin = new GearboxPlugin();
        var set = SampleSaves.Load(title);
        var file = GearboxTestData.PrimaryCharacter(title);
        var input = GearboxGroupTests.Input(set, Assert.Single(plugin.ProposeGroups(file)));
        using var group = (GearboxGroupDocument)plugin.OpenGroup(input, IReferenceContext.Empty);
        _ = group.BuildView();
        var empty = new Dictionary<string, FieldValue>();
        _ = group.Query("character/ammo", new() { Limit = 200 });
        var pool = group.Character.Root.Children("resource_pools").Select((p, i) => (p, i))
            .First(p => p.p.Float("amount") > 0 && GearboxAmmoRestore.Eligible(p.p.Float("amount")) &&
                SerialDatabase.For(title == "PPSA01493").Find(ItemCatalogs.Labels, p.p.Text("resource_path"))?.Category == "ammo");
        var baseline = pool.p.Float("amount");
        var ammoId = "character/ammo/" + pool.i;
        Assert.True(group.ApplyEdits([new(ammoId, FieldValue.Of(0.0))]).Succeeded);
        var before = group.Serialize().ToDictionary(o => o.RelativePath, o => o.Content.ToArray());
        var scope = new ActionScope([ammoId]);
        var plan = group.PlanAction("character/ammo/restore-imported", empty, scope);
        Assert.Empty(plan.Errors);
        Assert.Equal(FieldValue.Of(baseline), Assert.Single(plan.Changes).NewValue);
        var applied = group.ApplyActionPlan("character/ammo/restore-imported", empty, scope, plan);
        Assert.True(applied.Succeeded);
        Assert.NotNull(applied.Revert);
        var restored = group.Serialize().ToDictionary(o => o.RelativePath, o => o.Content.ToArray());
        var redo = group.RestoreMemento(applied.Revert);
        Assert.All(group.Serialize(), o => Assert.Equal(before[o.RelativePath], o.Content.ToArray()));
        Assert.True(ActionPlans.ContentEquals(plan, group.PlanAction("character/ammo/restore-imported", empty, scope)));
        _ = group.RestoreMemento(redo);
        Assert.All(group.Serialize(), o => Assert.Equal(restored[o.RelativePath], o.Content.ToArray()));

        _ = group.BuildView();
        Assert.True(group.ApplyEdits([new("character/character/level", FieldValue.Of(17))]).Succeeded);
        var item = group.Character.Inventory.Entries.First(e => group.Character.Inventory.Info(e.Id).Complete &&
            e.Field.Message is { } message && GearboxItemMarks.Supported(message));
        scope = new(["character/inventory_items/" + item.Id]);
        var match = group.PlanAction("character/items/match-level", empty, scope);
        Assert.NotEmpty(match.Changes);
        Assert.True(group.ApplyActionPlan("character/items/match-level", empty, scope, match).Succeeded);
        var marks = new Dictionary<string, FieldValue> { ["mark"] = FieldValue.Of(GearboxItemMarks.Get(group.Character.Inventory.Find(item.Id).Field.Message!) == 1 ? 2 : 1) };
        var markPlan = group.PlanAction("character/items/mark", marks, scope);
        Assert.NotEmpty(markPlan.Changes);
        Assert.False(group.ApplyActionPlan("character/items/mark", marks, scope, markPlan with { SkippedRows = [new("foreign", "forged")] }).Succeeded);
        Assert.True(group.ApplyActionPlan("character/items/mark", marks, scope, markPlan).Succeeded);
    }
}
