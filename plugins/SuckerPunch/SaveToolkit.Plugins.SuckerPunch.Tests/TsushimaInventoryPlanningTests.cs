using System.Buffers.Binary;
using System.Text.Json;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Core.Storage;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;
using SaveToolkit.Testing;
using static SaveToolkit.Plugins.SuckerPunch.Tests.SuckerPunchTestData;

namespace SaveToolkit.Plugins.SuckerPunch.Tests;

public sealed class TsushimaInventoryPlanningTests
{
    private const string ActionId = "inventory/set-quantity";
    private static Dictionary<string, FieldValue> Target(long value) => new() { ["quantity"] = FieldValue.Of(value) };

    [Fact]
    public void EverySamplePlansAllWritableCountsWithCopiesAndChecksumAndPreservesEverythingElse()
    {
        var plugin = new SuckerPunchPlugin();
        var files = SampleSaves.Load(TsushimaTitle).AllDiskFiles.Where(file => plugin.Probe(file).IsMatch).ToArray();
        Assert.Equal(25, files.Length);
        foreach (var file in files)
        {
            var original = file.ReadAllBytes();
            using var document = Open(file);
            var inventory = Inventory(document);
            var planner = (IActionPlanningDocument)document;
            var scope = new ActionScope();
            var plan = planner.PlanAction(ActionId, Target(7), scope);
            Assert.Empty(plan.Errors);
            Assert.Contains("not validated on console", Assert.Single(plan.Warnings));
            Assert.Equal(original, Assert.Single(document.Serialize()).Content.ToArray());
            var writable = inventory.Rows.Select(row => (FieldNode)row.Cells["quantity"])
                .Where(field => !field.Spec.ReadOnly && field.Value.AsInt32() != 7).ToArray();
            Assert.Equal(writable.Select(field => field.ValueId), plan.Changes.Where(change => !change.Derived).Select(change => change.ValueId));
            var expected = original.ToArray();
            var allowed = new List<(int Start, int Length)> { (8, sizeof(uint)) };
            foreach (var change in plan.Changes.Where(change => !change.Derived))
            {
                var name = change.ValueId.Split('/')[1];
                var offset = WriteAssertions.CountOffset(original, name);
                BinaryPrimitives.WriteUInt32LittleEndian(expected.AsSpan(offset), 7);
                allowed.Add((offset, sizeof(uint)));
                var row = inventory.Rows.Single(row => row.Key == name);
                if (row.Cells["copy"] is FieldNode copy)
                {
                    Assert.True(copy.Spec.ReadOnly);
                    Assert.Contains(plan.Changes, derived => derived.ValueId == copy.ValueId && derived.Derived);
                    BinaryPrimitives.WriteUInt32LittleEndian(expected.AsSpan(offset + sizeof(uint)), 7);
                    allowed.Add((offset + sizeof(uint), sizeof(uint)));
                }
            }
            RepairChecksum(expected);

            var outcome = planner.ApplyActionPlan(ActionId, Target(7), scope, plan);
            Assert.True(outcome.Succeeded);
            Assert.Equal(plan.Warnings, outcome.Warnings);
            Assert.Equal(plan.Changes.Select(change => (change.ValueId, change.OldValue, change.NewValue)),
                outcome.Changes.Select(change => (change.ValueId, change.OldValue, change.NewValue)));
            var edited = Assert.Single(document.Serialize()).Content.ToArray();
            Assert.Equal(expected, edited);
            WriteAssertions.OnlyRangesChanged(original, edited, allowed);
            using var reopened = Open(WithBytes(edited));
            Assert.All(plan.Changes, change => Assert.Equal(change.NewValue, reopened.GetValue(change.ValueId)));
            Assert.True(document.ApplyEdits(plan.Changes.Where(change => !change.Derived)
                .Select(change => new ValueEdit(change.ValueId, change.OldValue)).ToArray()).Succeeded);
            Assert.Equal(original, Assert.Single(document.Serialize()).Content.ToArray());
        }
    }

    [Fact]
    public void CoreRequiresPlanningAndUndoesAmmoCopiesAndResourcesTogether()
    {
        var file = Assert.IsType<DiskSaveFile>(Slot("suckerpunchUser1"));
        var plugin = new SuckerPunchPlugin();
        var document = plugin.Open(file);
        using var session = new DocumentSession("inventory", file, plugin, document);
        var view = Assert.IsType<TabsNode>(session.BuildView());
        var action = view.Tabs.Single(tab => tab.Title == "Inventory").Children.OfType<ActionNode>()
            .Single(action => action.ActionId == ActionId);
        Assert.True(action.SupportsPlanning);
        Assert.Contains("not validated on console", action.Description);
        var parameters = new Dictionary<string, JsonElement> { ["quantity"] = JsonSerializer.SerializeToElement("7") };
        Assert.False(session.InvokeAction(ActionId, parameters).Ok);
        var preview = session.PlanAction(ActionId, parameters, new ActionScope(["Supplies", "ArrowNormal"]));
        Assert.NotNull(preview.PlanId);
        Assert.Contains(preview.Plan.Changes, change => change.ValueId == "inventory/ArrowNormal/quantity-copy" && change.Derived);
        Assert.True(session.ApplyActionPlan(preview.PlanId, preview.Revision).Ok);
        var edited = Assert.Single(document.Serialize()).Content.ToArray();
        Assert.Equal(4, session.GetChanges().Changes.Count);
        Assert.True(session.Undo().Ok);
        Assert.False(session.State.CanUndo);
        Assert.Empty(session.GetChanges().Changes);
        Assert.Equal(file.ReadAllBytes(), Assert.Single(document.Serialize()).Content.ToArray());
        Assert.True(session.Redo().Ok);
        Assert.Equal(edited, Assert.Single(document.Serialize()).Content.ToArray());
        Assert.True(session.ApplyActionPlan(preview.PlanId, preview.Revision).Stale);
        var stale = session.PlanAction(ActionId, parameters);
        Assert.True(session.ApplyEdits([new IncomingEdit(ArrowId, JsonSerializer.SerializeToElement("8"))]).Ok);
        var beforeRejected = Assert.Single(document.Serialize()).Content.ToArray();
        Assert.True(session.ApplyActionPlan(stale.PlanId!, stale.Revision).Stale);
        Assert.Equal(beforeRejected, Assert.Single(document.Serialize()).Content.ToArray());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(32767)]
    public void BoundaryQuantitiesPersistInBothAmmoWords(long target)
    {
        using var document = Open(Slot("suckerpunchUser1"));
        var planner = (IActionPlanningDocument)document;
        var scope = new ActionScope(["ArrowNormal", "Supplies"]);
        var plan = planner.PlanAction(ActionId, Target(target), scope);
        Assert.Empty(plan.Errors);
        Assert.True(planner.ApplyActionPlan(ActionId, Target(target), scope, plan).Succeeded);
        using var reopened = Open(WithBytes(Assert.Single(document.Serialize()).Content.ToArray()));
        Assert.Equal(target, reopened.GetValue(ArrowId).AsInt64());
        Assert.Equal(target, reopened.GetValue("inventory/ArrowNormal/quantity-copy").AsInt64());
        Assert.Equal(target, reopened.GetValue(SuppliesId).AsInt64());
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(32768)]
    public void InvalidTargetsAndForgedPlansRejectAtomically(long value)
    {
        var file = Slot("suckerpunchUser1");
        using var document = Open(file);
        var planner = (IActionPlanningDocument)document;
        var invalid = planner.PlanAction(ActionId, Target(value), new ActionScope());
        Assert.NotEmpty(invalid.Errors);
        Assert.False(planner.ApplyActionPlan(ActionId, Target(value), new ActionScope(), invalid).Succeeded);
        var plan = planner.PlanAction(ActionId, Target(7), new ActionScope());
        Assert.False(planner.ApplyActionPlan(ActionId, Target(7), new ActionScope(), plan with { Warnings = [] }).Succeeded);
        Assert.False(planner.ApplyActionPlan(ActionId, Target(7), new ActionScope(), plan with { Changes = [] }).Succeeded);
        Assert.Equal(file.ReadAllBytes(), Assert.Single(document.Serialize()).Content.ToArray());
    }

    [Fact]
    public void LockedOversizedUnmappedAndInconsistentSelectedRowsAreSkipped()
    {
        var bytes = Slot("suckerpunchUser1").ReadAllBytes();
        var arrow = WriteAssertions.CountOffset(bytes, "ArrowNormal");
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(arrow + sizeof(uint)), 5);
        var flowers = WriteAssertions.CountOffset(bytes, "Flowers");
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(flowers), 32768);
        var supplies = WriteAssertions.CountOffset(bytes, "Supplies");
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(supplies - sizeof(uint)), 0);
        RepairChecksum(bytes);
        using var document = Open(WithBytes(bytes));
        var planner = (IActionPlanningDocument)document;
        var scope = new ActionScope(["ArrowNormal", "Flowers", "Supplies", "TestItem"]);
        var plan = planner.PlanAction(ActionId, Target(7), scope);
        Assert.Empty(plan.Errors);
        Assert.Empty(plan.Changes);
        Assert.Equal(4, plan.SkippedRows.Count);
        Assert.True(planner.ApplyActionPlan(ActionId, Target(7), scope, plan).Succeeded);
        Assert.Equal(bytes, Assert.Single(document.Serialize()).Content.ToArray());
    }

    [Fact]
    public void EmptyUnknownAndQueryScopesAndWrongParametersCannotWidenSelection()
    {
        using var document = Open(Slot("suckerpunchUser1"));
        var planner = (IActionPlanningDocument)document;
        Assert.Empty(planner.PlanAction(ActionId, Target(7), new ActionScope([])).Changes);
        Assert.NotEmpty(planner.PlanAction(ActionId, Target(7), new ActionScope(["unknown"])).Errors);
        var query = new ActionScope { QueryScope = new ActionQueryScope("inventory", new QueryableCollectionQuery(), 0) };
        Assert.NotEmpty(planner.PlanAction(ActionId, Target(7), query).Errors);
        Dictionary<string, FieldValue>[] invalid = [[], new() { ["quantity"] = FieldValue.Of("7") },
            new() { ["quantity"] = FieldValue.Of(Int128.MaxValue) },
            new() { ["quantity"] = FieldValue.Of(7L), ["unlock"] = FieldValue.Of(true) }];
        Assert.All(invalid, parameters => Assert.NotEmpty(planner.PlanAction(ActionId, parameters, new ActionScope()).Errors));
    }

    [Fact]
    public void YoteiAndUnknownTsushimaVersionsCannotUseTsushimaInventoryPlans()
    {
        var plugin = new SuckerPunchPlugin();
        var files = SampleSaves.Load("PPSA26344").AllDiskFiles.Where(file => plugin.Probe(file).IsMatch).ToArray();
        Assert.NotEmpty(files);
        var future = Slot("suckerpunchUser1").ReadAllBytes();
        BinaryPrimitives.WriteUInt32LittleEndian(future.AsSpan(12), 50);
        RepairChecksum(future);
        foreach (var file in files.Append(WithBytes(future)))
        {
            using var document = Open(file);
            var planner = (IActionPlanningDocument)document;
            Assert.NotEmpty(planner.PlanAction(ActionId, Target(7), new ActionScope()).Errors);
            Assert.False(planner.ApplyActionPlan(ActionId, Target(7), new ActionScope(), new ActionPlan()).Succeeded);
            Assert.Equal(file.ReadAllBytes(), Assert.Single(document.Serialize()).Content.ToArray());
        }
    }
}
