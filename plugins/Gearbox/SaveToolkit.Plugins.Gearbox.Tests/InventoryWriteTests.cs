using System.Text.Json;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.Gearbox.Tests;

public sealed class InventoryWriteTests
{
    [Theory]
    [InlineData("PPSA01463")]
    [InlineData("PPSA01493")]
    public void F6BulkLevel_QuerySelectionPreviewApplyStaleUndoRedoAndSkippedRows(string title)
    {
        var plugin = new GearboxPlugin();
        var file = SampleSaves.Load(title).AllDiskFiles.First(f => f.FileName == "1" && !f.Directory.IsBackup);
        using var document = (GearboxDocument)plugin.Open(file);
        using var session = new DocumentSession("bulk", file, plugin, document);
        _ = session.BuildView();
        var original = document.Serialize().Single().Content.ToArray();
        var scope = new ActionScope { QueryScope = new("inventory_items", new() { Text = "", Limit = 1 }, session.State.Revision) };
        var parameters = new Dictionary<string, JsonElement> { ["level"] = JsonSerializer.SerializeToElement("17") };
        var preview = session.PlanAction("items/level", parameters, scope);
        Assert.Empty(preview.Plan.Errors);
        Assert.NotEmpty(preview.Plan.Changes);
        Assert.NotEmpty(preview.Plan.Warnings);
        Assert.Equal(original, document.Serialize().Single().Content.ToArray());
        Assert.True(session.ApplyActionPlan(preview.PlanId!, preview.Revision).Ok);
        var edited = document.Serialize().Single().Content.ToArray();
        Assert.False(session.ApplyActionPlan(preview.PlanId!, preview.Revision).Ok);
        using var reopened = (GearboxDocument)plugin.Open(SerialWriteTests.Overlay(file, edited));
        Assert.All(reopened.Inventory.Entries, entry => Assert.Equal(17, new SerialReader(title == "PPSA01493").Read(entry.Serial).Level));
        SerialWriteTests.AssertUnrelated(GearboxContainer.Parse(original, title, false).Decode(), reopened.Root, "inventory_items");
        Assert.True(session.Undo().Ok);
        Assert.Equal(original, document.Serialize().Single().Content.ToArray());
        Assert.True(session.Redo().Ok);
        Assert.Equal(edited, document.Serialize().Single().Content.ToArray());
        Assert.False(session.PlanAction("items/level", new Dictionary<string, JsonElement> { ["level"] = JsonSerializer.SerializeToElement("999") }).Plan.Errors.Count == 0);
        var entry = document.Inventory.Entries.First();
        document.Inventory.SetSerial(entry.Id, [1, 2, 3]);
        var skipped = document.PlanAction("items/level", new Dictionary<string, FieldValue> { ["level"] = FieldValue.Of(18) }, new());
        Assert.Contains(skipped.SkippedRows, row => row.RowKey == entry.Id);
    }

    [Theory]
    [InlineData("PPSA01463")]
    [InlineData("PPSA01493")]
    public void F5RemoveDuplicateAndImport_ArePlannedAtomicAndKeepSurvivingIdentities(string title)
    {
        var plugin = new GearboxPlugin();
        var file = SampleSaves.Load(title).AllDiskFiles.First(f => f.FileName == "1" && !f.Directory.IsBackup);
        using var document = (GearboxDocument)plugin.Open(file);
        using var session = new DocumentSession("structural", file, plugin, document);
        _ = session.BuildView();
        var original = document.Serialize().Single().Content.ToArray();
        var entries = document.Inventory.Entries.ToArray();
        var supported = entries.First(e => new SerialReader(title == "PPSA01493").Read(e.Serial).Complete);
        var remove = entries.Last(e => e.Id != supported.Id);
        var preview = session.PlanAction("items/remove", new Dictionary<string, JsonElement>(), new([remove.Id]));
        Assert.Empty(preview.Plan.Errors);
        Assert.True(session.ApplyActionPlan(preview.PlanId!, preview.Revision).Ok);
        Assert.Equal(entries.Where(e => e.Id != remove.Id).Select(e => e.Id), document.Inventory.Entries.Select(e => e.Id));
        var removed = document.Serialize().Single().Content.ToArray();
        using var reopened = (GearboxDocument)plugin.Open(SerialWriteTests.Overlay(file, removed));
        Assert.Equal(entries.Length - 1, reopened.Inventory.Entries.Count());
        Assert.Equal(entries.Where(e => e.Id != remove.Id).Select(e => Convert.ToHexString(e.Field.Encode())), reopened.Inventory.Entries.Select(e => Convert.ToHexString(e.Field.Encode())));
        SerialWriteTests.AssertUnrelated(GearboxContainer.Parse(original, title, false).Decode(), reopened.Root, "inventory_items", "equipped_inventory_list");
        foreach (var action in new[] { "items/duplicate", "items/import" })
        {
            var parameters = action == "items/import" ? new Dictionary<string, JsonElement> { ["code"] = JsonSerializer.SerializeToElement("BL3(" + Convert.ToBase64String(supported.Serial) + ")") } : new();
            var scope = action == "items/import" ? new ActionScope() : new([supported.Id]);
            var create = session.PlanAction(action, parameters, scope);
            Assert.Empty(create.Plan.Errors);
            Assert.Equal(removed, document.Serialize().Single().Content.ToArray());
            Assert.True(session.ApplyActionPlan(create.PlanId!, create.Revision).Ok);
            var created = document.Serialize().Single().Content.ToArray();
            var newId = document.Inventory.Entries.Last().Id;
            Assert.DoesNotContain(newId, entries.Select(e => e.Id));
            Assert.Equal(supported.Serial, document.Inventory.Entries.Last().Serial);
            using var added = (GearboxDocument)plugin.Open(SerialWriteTests.Overlay(file, created));
            Assert.Equal(entries.Length, added.Inventory.Entries.Count());
            Assert.Equal(reopened.Inventory.Entries.Select(e => Convert.ToHexString(e.Field.Encode())), added.Inventory.Entries.Take(entries.Length - 1).Select(e => Convert.ToHexString(e.Field.Encode())));
            Assert.True(session.Undo().Ok);
            Assert.Equal(removed, document.Serialize().Single().Content.ToArray());
            Assert.True(session.Redo().Ok);
            Assert.Equal(created, document.Serialize().Single().Content.ToArray());
            Assert.Equal(newId, document.Inventory.Entries.Last().Id);
            Assert.True(session.Undo().Ok);
        }
        Assert.True(session.Undo().Ok);
        Assert.Equal(original, document.Serialize().Single().Content.ToArray());
        Assert.Equal(entries.Select(e => e.Id), document.Inventory.Entries.Select(e => e.Id));
        Assert.NotEmpty(session.PlanAction("items/remove", new Dictionary<string, JsonElement>()).Plan.Errors);
        Assert.NotEmpty(session.PlanAction("items/remove", new Dictionary<string, JsonElement>(), new(["foreign"])).Plan.Errors);
        Assert.NotEmpty(session.PlanAction("items/import", new Dictionary<string, JsonElement> { ["code"] = JsonSerializer.SerializeToElement("BL3(bad)") }).Plan.Errors);
        Assert.Equal(original, document.Serialize().Single().Content.ToArray());
    }

    [Theory]
    [InlineData("PPSA01463")]
    [InlineData("PPSA01493")]
    public void EquipmentRemoval_RebasesIndicesAndRejectsOverflowWithoutMutation(string title)
    {
        var plugin = new GearboxPlugin();
        var file = SampleSaves.Load(title).AllDiskFiles.First(f => f.FileName == "1" && !f.Directory.IsBackup);
        using var document = (GearboxDocument)plugin.Open(file);
        _ = document.BuildView();
        var entries = document.Inventory.Entries.ToArray();
        var before = document.CaptureState();
        var slot = document.Root.Children("equipped_inventory_list").First();
        slot.SetInteger("inventory_list_index", 1);
        var other = document.Root.Children("equipped_inventory_list").Skip(1).First();
        other.SetInteger("inventory_list_index", 2);
        var plan = document.PlanAction("items/remove", new Dictionary<string, FieldValue>(), new([entries[1].Id]));
        Assert.True(plan.Changes.Count(c => c.Derived) >= 2);
        Assert.True(document.ApplyActionPlan("items/remove", new Dictionary<string, FieldValue>(), new([entries[1].Id]), plan).Succeeded);
        Assert.Equal(-1, document.Root.Children("equipped_inventory_list").First().Integer("inventory_list_index"));
        Assert.Equal(1, document.Root.Children("equipped_inventory_list").Skip(1).First().Integer("inventory_list_index"));
        _ = document.RestoreMemento(before);
        _ = document.BuildView();
        var sdu = document.Root.Children("sdu_list").Single(m => m.Text("sdu_data_path").Contains("SDU_Backpack.", StringComparison.Ordinal));
        sdu.SetInteger("sdu_level", 0);
        var bytes = document.Serialize().Single().Content.ToArray();
        var full = document.PlanAction("items/duplicate", new Dictionary<string, FieldValue>(), new(entries.Select(e => e.Id).ToArray()));
        Assert.NotEmpty(full.Errors);
        Assert.False(document.ApplyActionPlan("items/duplicate", new Dictionary<string, FieldValue>(), new(entries.Select(e => e.Id).ToArray()), full).Succeeded);
        Assert.Equal(bytes, document.Serialize().Single().Content.ToArray());
    }
}
