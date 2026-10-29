using System.Text.Json;
using SaveToolkit.Core.Export;
using SaveToolkit.Core.Plugins;
using SaveToolkit.Core.References;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Saves;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.Gearbox.Tests;

public sealed class GroupWriteTests
{
    [Theory]
    [InlineData("PPSA01463")]
    [InlineData("PPSA01493")]
    public void F7TransferPreviewAtomicExportFreshSnapshotsAndOneGroupUndo(string title)
    {
        var plugin = new GearboxPlugin();
        var set = SampleSaves.Load(title);
        using var workspace = new Workspace("gearbox-write", "Transfers", set, new PluginCatalog([plugin]), new ReferencePackStore([plugin]));
        var file = set.AllDiskFiles.First(f => f.FileName == "1" && !f.Directory.IsBackup && f.Directory.Generation == 0);
        var offer = Assert.Single(workspace.ProposeGroups(file.Id, "gearbox"));
        var opened = workspace.OpenGroup(offer.Token);
        var session = opened.Session;
        _ = session.BuildView();
        Assert.True(session.ApplyEdits([new("character/currency/618814354", JsonSerializer.SerializeToElement("100")),
            new("profile/keys/2420693941", JsonSerializer.SerializeToElement("1"))]).Ok);
        Assert.Equal(2, session.Serialize().Count);
        Assert.True(session.Undo().Ok);
        Assert.False(session.State.IsDirty);
        Assert.False(session.State.CanUndo);
        var page = session.QueryCollection("character/inventory_items", new() { Limit = 200 });
        var originalBankCount = session.QueryCollection("profile/bank_inventory_list", new() { Limit = 1 }).TotalCount;
        ActionPreview? preview = null;
        foreach (var row in page.Rows)
        {
            var candidate = session.PlanAction("transfer/deposit", new Dictionary<string, JsonElement>(), new([row.Key]));
            if (candidate.Plan.Errors.Count == 0) { preview = candidate; break; }
        }
        Assert.NotNull(preview);
        Assert.Contains(preview.Plan.Changes, c => c.ValueId.StartsWith("character/", StringComparison.Ordinal));
        Assert.Contains(preview.Plan.Changes, c => c.ValueId.StartsWith("profile/", StringComparison.Ordinal));
        Assert.NotEmpty(preview.Plan.Warnings);
        Assert.False(session.State.IsDirty);
        Assert.True(session.ApplyActionPlan(preview.PlanId!, preview.Revision).Ok);
        var changed = session.Serialize().ToDictionary(o => o.RelativePath, o => o.Content.ToArray());
        Assert.Equal(2, changed.Count);
        foreach (var output in changed)
        {
            var source = ((ISaveSet)set).FindFile(output.Key)!;
            using var reopened = (GearboxDocument)plugin.Open(GearboxTestData.Overlay(source, output.Value));
            Assert.Equal(output.Value, reopened.Serialize().Single().Content.ToArray());
            Assert.Equal(source.FileName == "profile" ? originalBankCount + 1 : page.TotalCount - 1, reopened.Inventory.Entries
                .Count(entry => entry.List == (source.FileName == "profile" ? GearboxInventory.Bank : GearboxInventory.Backpack)));
            SerialWriteTests.AssertUnrelated(GearboxContainer.Parse(source.ReadAllBytes(), title, source.FileName == "profile").Decode(),
                GearboxContainer.Parse(output.Value, title, source.FileName == "profile").Decode(),
                source.FileName == "profile" ? "bank_inventory_list" : "inventory_items", "equipped_inventory_list");
        }
        var export = WorkspaceExporter.Plan(workspace, new() { Scope = ExportScope.Everything });
        Assert.Equal(2, export.Overrides.Count);
        Assert.True(session.Undo().Ok);
        Assert.False(session.State.IsDirty);
        Assert.False(session.State.CanUndo);
        Assert.True(session.Redo().Ok);
        Assert.All(session.Serialize(), output => Assert.Equal(changed[output.RelativePath], output.Content.ToArray()));
        var bank = session.QueryCollection("profile/bank_inventory_list", new() { Limit = 200 });
        var last = bank.Rows.OrderBy(row => int.Parse(((TextNode)row.Cells["index"]).Text, System.Globalization.CultureInfo.InvariantCulture)).Last();
        var withdrawal = session.PlanAction("transfer/withdraw", new Dictionary<string, JsonElement>(), new([last.Key]));
        Assert.Empty(withdrawal.Plan.Errors);
        Assert.True(session.ApplyActionPlan(withdrawal.PlanId!, withdrawal.Revision).Ok);
        Assert.True(session.Undo().Ok);
        Assert.All(session.Serialize(), output => Assert.Equal(changed[output.RelativePath], output.Content.ToArray()));
        Assert.NotEmpty(session.PlanAction("transfer/deposit", new Dictionary<string, JsonElement>(), new([last.Key])).Plan.Errors);

        // The full snapshot, including inserted duplicate identities, must load into a fresh instance.
        var input = GearboxGroupTests.Input(set, Assert.Single(plugin.ProposeGroups(file)));
        using var group = (GearboxGroupDocument)plugin.OpenGroup(input, IReferenceContext.Empty);
        _ = group.BuildView();
        var id = group.Character.Inventory.Entries.First(e => new SerialReader(title == "PPSA01493").Read(e.Serial).Complete).Id;
        var remove = group.PlanAction("character/items/remove", new Dictionary<string, FieldValue>(), new(["character/inventory_items/" + id]));
        Assert.True(group.ApplyActionPlan("character/items/remove", new Dictionary<string, FieldValue>(), new(["character/inventory_items/" + id]), remove).Succeeded);
        using var restored = (GearboxGroupDocument)plugin.OpenGroup(input, IReferenceContext.Empty);
        _ = restored.RestoreMemento(group.CaptureMemento());
        Assert.Equal(group.Character.Inventory.Entries.Select(e => e.Id), restored.Character.Inventory.Entries.Select(e => e.Id));
        Assert.All(group.Serialize(), output => Assert.Equal(output.Content.ToArray(), restored.Serialize().Single(o => o.RelativePath == output.RelativePath).Content.ToArray()));
    }

    [Theory]
    [InlineData("PPSA01463")]
    [InlineData("PPSA01493")]
    public void EveryExistingAccountField_GroupOnlyWriteReopenUndoAndInvalidBatch(string title)
    {
        var plugin = new GearboxPlugin();
        var set = SampleSaves.Load(title);
        var tested = 0;
        foreach (var file in set.AllDiskFiles.Where(f => f.FileName == "1" && !f.Directory.IsBackup))
        {
            var input = GearboxGroupTests.Input(set, Assert.Single(plugin.ProposeGroups(file)));
            using var group = (GearboxGroupDocument)plugin.OpenGroup(input, IReferenceContext.Empty);
            var baseline = group.Serialize().ToDictionary(o => o.RelativePath, o => o.Content.ToArray());
            var fields = GearboxGroupTests.Walk(group, group.BuildView()).OfType<FieldNode>()
                .Where(f => f.ValueId.Contains("/account/", StringComparison.Ordinal) && !f.Spec.ReadOnly).ToArray();
            foreach (var field in fields)
            {
                var snapshot = group.CaptureMemento();
                var next = field.Value.AsInt64() == 0 ? 1 : 0;
                Assert.Equal(Confidence.Inferred, field.Confidence);
                var result = group.ApplyEdits([new(field.ValueId, FieldValue.Of(next))]);
                Assert.True(result.Succeeded);
                var output = group.Serialize();
                var overlay = new SaveToolkit.Core.Storage.OverlaySaveSet(set, output.ToDictionary(o => o.RelativePath, o => o.Content));
                var reopenedInput = new DocumentGroupInput(input.Members.Select(m => new DocumentGroupFile(m.Role, ((ISaveSet)overlay).FindFile(m.File.RelativePath)!)).ToArray());
                using var reopened = (GearboxGroupDocument)plugin.OpenGroup(reopenedInput, IReferenceContext.Empty);
                _ = GearboxGroupTests.Walk(reopened, reopened.BuildView()).ToArray();
                Assert.Equal(next, reopened.GetValue(field.ValueId).AsInt64());
                var inverse = group.RestoreMemento(snapshot);
                Assert.All(group.Serialize(), bytes => Assert.Equal(baseline[bytes.RelativePath], bytes.Content.ToArray()));
                _ = group.RestoreMemento(inverse);
                Assert.All(group.Serialize(), bytes => Assert.Equal(output.Single(o => o.RelativePath == bytes.RelativePath).Content.ToArray(), bytes.Content.ToArray()));
                _ = group.RestoreMemento(snapshot);
                _ = GearboxGroupTests.Walk(group, group.BuildView()).ToArray();
                Assert.False(group.ApplyEdits([new(field.ValueId, FieldValue.Of(-1))]).Succeeded);
                _ = GearboxGroupTests.Walk(group, group.BuildView()).ToArray();
                Assert.False(group.ApplyEdits([new(field.ValueId, FieldValue.Of(Assert.IsType<IntegerSpec>(field.Spec).Max + 1))]).Succeeded);
                _ = GearboxGroupTests.Walk(group, group.BuildView()).ToArray();
                tested++;
            }
            Assert.False(group.ApplyEdits([new("character/character/xp", FieldValue.Of(123)), new("profile/absent", FieldValue.Of(1))]).Succeeded);
            Assert.All(group.Serialize(), bytes => Assert.Equal(baseline[bytes.RelativePath], bytes.Content.ToArray()));
        }
        Assert.True(tested > 0);
    }

    [Theory]
    [InlineData("PPSA01463")]
    [InlineData("PPSA01493")]
    public void UnrepresentableMetadataAndUnknownCapacity_RejectWithoutLosingAnItem(string title)
    {
        var plugin = new GearboxPlugin();
        var set = SampleSaves.Load(title);
        var file = set.AllDiskFiles.First(f => f.FileName == "1" && !f.Directory.IsBackup);
        using var group = (GearboxGroupDocument)plugin.OpenGroup(GearboxGroupTests.Input(set, Assert.Single(plugin.ProposeGroups(file))), IReferenceContext.Empty);
        var item = group.Character.Inventory.Entries.First(e => new SerialReader(title == "PPSA01493").Read(e.Serial).Complete);
        item.Field.Message!.Add(new ProtoField(20000, 0, null, [0x80, 0xE2, 0x09, 0x01], 1UL, null));
        var scope = new ActionScope(["character/inventory_items/" + item.Id]);
        var original = group.Serialize().ToDictionary(o => o.RelativePath, o => o.Content.ToArray());
        var plan = group.PlanAction("transfer/deposit", new Dictionary<string, FieldValue>(), scope);
        if (title == "PPSA01463") Assert.NotEmpty(plan.Errors);
        else
        {
            Assert.Empty(plan.Errors);
            Assert.True(group.ApplyActionPlan("transfer/deposit", new Dictionary<string, FieldValue>(), scope, plan).Succeeded);
            Assert.Contains(group.Profile.Inventory.Entries.Last(e => e.List == GearboxInventory.Bank).Field.Message!.Fields, f => f.Number == 20000);
        }
        var spec = group.Profile.Root.Schema.Spec("Profile", "profile_sdu_list");
        var unknown = new ProtoMessage(group.Profile.Root.Schema, spec.Type, []);
        unknown.SetText("sdu_data_path", "unknown");
        group.Profile.Root.Add(new ProtoField(group.Profile.Root.Schema.Tag("Profile", "profile_sdu_list"), 2, spec, [], Array.Empty<byte>(), unknown));
        Assert.All(group.Profile.Root.Children("profile_sdu_list"), m => m.SetText("sdu_data_path", "unknown"));
        var rejected = group.PlanAction("transfer/deposit", new Dictionary<string, FieldValue>(), scope);
        Assert.NotEmpty(rejected.Errors);
        if (title == "PPSA01463") Assert.Equal(original[file.RelativePath], group.Character.Serialize().Single().Content.ToArray());
    }

    [Theory]
    [InlineData("PPSA01463")]
    [InlineData("PPSA01493")]
    public void LostLootClaim_RealSerialKeepsIdentityAndFreshSnapshotAllocationCounters(string title)
    {
        var plugin = new GearboxPlugin();
        var set = SampleSaves.Load(title);
        var file = set.AllDiskFiles.First(f => f.FileName == "1" && !f.Directory.IsBackup);
        var input = GearboxGroupTests.Input(set, Assert.Single(plugin.ProposeGroups(file)));
        using var group = (GearboxGroupDocument)plugin.OpenGroup(input, IReferenceContext.Empty);
        var serial = group.Character.Inventory.Entries.First(e => new SerialReader(title == "PPSA01493").Read(e.Serial).Complete).Serial;
        var root = group.Profile.Root;
        var spec = root.Schema.Spec("Profile", GearboxInventory.LostLoot);
        root.Add(new ProtoField(root.Schema.Tag("Profile", GearboxInventory.LostLoot), 2, spec, [], serial, null));
        _ = group.BuildView();
        var freeSpace = new ActionScope(group.Character.Inventory.Entries.Take(2).Select(e => "character/inventory_items/" + e.Id).ToArray());
        var removal = group.PlanAction("character/items/remove", new Dictionary<string, FieldValue>(), freeSpace);
        Assert.True(group.ApplyActionPlan("character/items/remove", new Dictionary<string, FieldValue>(), freeSpace, removal).Succeeded);
        var id = group.Profile.Inventory.Entries.Last(e => e.List == GearboxInventory.LostLoot).Id;
        var scope = new ActionScope(["profile/lost_loot_inventory_list/" + id]);
        var before = group.CaptureMemento();
        var plan = group.PlanAction("transfer/claim", new Dictionary<string, FieldValue>(), scope);
        Assert.Empty(plan.Errors);
        Assert.True(group.ApplyActionPlan("transfer/claim", new Dictionary<string, FieldValue>(), scope, plan).Succeeded);
        var moved = group.Character.Inventory.Entries.Last();
        Assert.Equal(serial, moved.Serial);
        Assert.Equal("profile:" + id, moved.Id);
        using var fresh = (GearboxGroupDocument)plugin.OpenGroup(input, IReferenceContext.Empty);
        _ = fresh.RestoreMemento(group.CaptureMemento());
        _ = fresh.BuildView();
        Assert.Equal(moved.Id, fresh.Character.Inventory.Entries.Last().Id);
        var duplicateScope = new ActionScope(["character/inventory_items/" + moved.Id]);
        foreach (var document in new[] { group, fresh })
        {
            var duplicate = document.PlanAction("character/items/duplicate", new Dictionary<string, FieldValue>(), duplicateScope);
            Assert.Empty(duplicate.Errors);
            Assert.True(document.ApplyActionPlan("character/items/duplicate", new Dictionary<string, FieldValue>(), duplicateScope, duplicate).Succeeded);
        }
        Assert.Equal(group.Character.Inventory.Entries.Select(e => e.Id), fresh.Character.Inventory.Entries.Select(e => e.Id));
        _ = group.RestoreMemento(before);
        Assert.Contains(group.Profile.Inventory.Entries, entry => entry.Id == id);
        Assert.DoesNotContain(group.Character.Inventory.Entries, entry => entry.Id == moved.Id);
    }
}
