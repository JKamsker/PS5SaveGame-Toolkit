using System.Text.Json;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Core.Storage;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Values;
using static SaveToolkit.Plugins.SuckerPunch.Tests.QuantityPlanningTestData;
using static SaveToolkit.Plugins.SuckerPunch.Tests.YoteiTestData;

namespace SaveToolkit.Plugins.SuckerPunch.Tests;

public sealed class YoteiPlanningTests
{
    [Fact]
    public void PureBoundedPlansRespectEachRecordLimitAndEmitCopiesChecksumAndWarnings()
    {
        var file = Slot("suckerpunchUser4");
        using var doc = Open(file);
        var planner = (IActionPlanningDocument)doc;
        var parameters = Target(5);
        var scope = new ActionScope(["Mon", "ArrowNormal", "ArrowFire", "TestItem"]);
        var plan = planner.PlanAction(ActionId, parameters, scope);
        Assert.Empty(plan.Errors);
        Assert.Contains("game may reject", Assert.Single(plan.Warnings));
        Assert.Equal(2, plan.Changes.Count(change => !change.Derived));
        Assert.Contains(plan.Changes, change => change.ValueId == "yotei/inventory/ArrowNormal/quantity-copy" && change.Derived);
        Assert.Contains(plan.Changes, change => change.ValueId == "container/word3" && change.Derived);
        Assert.Equal(2, plan.SkippedRows.Count);
        Assert.Equal(file.ReadAllBytes(), Assert.Single(doc.Serialize()).Content.ToArray());
        Assert.True(planner.ApplyActionPlan(ActionId, parameters, scope, plan).Succeeded);
        var edited = Assert.Single(doc.Serialize()).Content.ToArray();
        var offsets = InventoryRecords(edited).ToDictionary(row => row.Name, row => row.Data);
        WriteAssertions.OnlyRangesChanged(file.ReadAllBytes(), edited, [(12, 4), (offsets["Mon"] + 5, 4), (offsets["ArrowNormal"] + 5, 8)]);
        using var reopened = Open(WithBytes(edited));
        Assert.Equal(5, reopened.GetValue(MonId).AsInt32());
        Assert.Equal(5, reopened.GetValue(ArrowId).AsInt32());
        Assert.Equal(3, reopened.GetValue("yotei/inventory/ArrowFire/quantity").AsInt32());
    }

    [Fact]
    public void CoreUndoRedoTreatsAWholeBulkPlanAsOneEdit()
    {
        var file = Assert.IsType<DiskSaveFile>(Slot("suckerpunchUser4"));
        var plugin = new SuckerPunchPlugin();
        using var doc = plugin.Open(file);
        using var session = new DocumentSession("yotei-plan", file, plugin, doc);
        session.BuildView();
        var parameters = new Dictionary<string, JsonElement> { ["quantity"] = JsonSerializer.SerializeToElement("5") };
        Assert.False(session.InvokeAction(ActionId, parameters).Ok);
        var preview = session.PlanAction(ActionId, parameters, new ActionScope(["Mon", "ArrowNormal"]));
        Assert.NotNull(preview.PlanId);
        Assert.True(session.ApplyActionPlan(preview.PlanId, preview.Revision).Ok);
        var edited = Assert.Single(doc.Serialize()).Content.ToArray();
        Assert.Equal(4, session.GetChanges().Changes.Count);
        Assert.True(session.Undo().Ok);
        Assert.False(session.State.CanUndo);
        Assert.Empty(session.GetChanges().Changes);
        Assert.Equal(file.ReadAllBytes(), Assert.Single(doc.Serialize()).Content.ToArray());
        Assert.True(session.Redo().Ok);
        Assert.Equal(edited, Assert.Single(doc.Serialize()).Content.ToArray());
        Assert.True(session.ApplyActionPlan(preview.PlanId, preview.Revision).Stale);
    }

    [Fact]
    public void InvalidScopesParametersAndForgedOrStalePlansCannotChangeBytes()
    {
        var file = Slot("suckerpunchUser4");
        using var doc = Open(file);
        var planner = (IActionPlanningDocument)doc;
        Assert.Empty(planner.PlanAction(ActionId, Target(5), new ActionScope([])).Changes);
        Assert.NotEmpty(planner.PlanAction(ActionId, Target(5), new ActionScope(["unknown"])).Errors);
        Assert.NotEmpty(planner.PlanAction(ActionId, Target(5), new ActionScope
            { QueryScope = new ActionQueryScope("yotei/gear-registry", new QueryableCollectionQuery(), 0) }).Errors);
        Assert.NotEmpty(planner.PlanAction(ActionId, Target(-1), new ActionScope()).Errors);
        Assert.NotEmpty(planner.PlanAction(ActionId, Target(112100), new ActionScope()).Errors);
        Assert.NotEmpty(planner.PlanAction(ActionId, new Dictionary<string, FieldValue> { ["quantity"] = FieldValue.Of("1") }, new ActionScope()).Errors);
        Assert.NotEmpty(planner.PlanAction(ActionId, new Dictionary<string, FieldValue>(), new ActionScope()).Errors);
        var scope = new ActionScope(["Mon", "ArrowNormal"]);
        var plan = planner.PlanAction(ActionId, Target(5), scope);
        Assert.False(planner.ApplyActionPlan(ActionId, Target(5), scope, plan with { Warnings = [] }).Succeeded);
        Assert.False(planner.ApplyActionPlan(ActionId, Target(5), scope, plan with { Changes = [] }).Succeeded);
        Assert.Equal(file.ReadAllBytes(), Assert.Single(doc.Serialize()).Content.ToArray());
        Assert.True(doc.ApplyEdits([new(MonId, FieldValue.Of(100L))]).Succeeded);
        var before = Assert.Single(doc.Serialize()).Content.ToArray();
        Assert.False(planner.ApplyActionPlan(ActionId, Target(5), scope, plan).Succeeded);
        Assert.Equal(before, Assert.Single(doc.Serialize()).Content.ToArray());
    }

    [Fact]
    public void EverySamplePlansOnlySupportedEditableRowsOrRejectsPlaceholders()
    {
        foreach (var file in Files)
        {
            using var doc = Open(file);
            var planner = (IActionPlanningDocument)doc;
            var plan = planner.PlanAction(ActionId, Target(0), new ActionScope());
            if (Word(file.ReadAllBytes(), 0) == 0) { Assert.NotEmpty(plan.Errors); continue; }
            Assert.Empty(plan.Errors);
            Assert.All(plan.Changes.Where(change => !change.Derived), change =>
                Assert.Contains(Inventory(doc).Rows, row => ((SaveToolkit.Sdk.Views.FieldNode)row.Cells["quantity"]).ValueId == change.ValueId &&
                    !((SaveToolkit.Sdk.Views.FieldNode)row.Cells["quantity"]).Spec.ReadOnly));
            Assert.True(planner.ApplyActionPlan(ActionId, Target(0), new ActionScope(), plan).Succeeded);
            using var reopened = Open(WithBytes(Assert.Single(doc.Serialize()).Content.ToArray()));
            Assert.All(plan.Changes.Where(change => !change.Derived), change => Assert.Equal(0, reopened.GetValue(change.ValueId).AsInt32()));
        }
    }
}
