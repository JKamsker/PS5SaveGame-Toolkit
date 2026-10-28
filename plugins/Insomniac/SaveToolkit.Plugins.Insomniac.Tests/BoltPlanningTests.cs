using System.Text.Json;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Core.Storage;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;
using SaveToolkit.Testing;
using SaveToolkit.Testing.Diff;

namespace SaveToolkit.Plugins.Insomniac.Tests;

public sealed class BoltPlanningTests
{
    [Fact]
    public void EveryPrimaryGameplayRecordSupportsPurePreviewApplyReopenAndOneStepUndoRedo()
    {
        var plugin = new InsomniacPlugin();
        var files = SampleSaves.Load("PPSA01474").AllDiskFiles.Where(f => f.FileName == "i29-s.save" && !f.Directory.IsBackup).ToArray();
        Assert.Equal(36, files.Length);
        foreach (var input in files)
        {
            var file = Assert.IsType<DiskSaveFile>(input);
            using var document = plugin.Open(file);
            using var session = new DocumentSession("bolt-plan", file, plugin, document);
            var view = Assert.IsType<TabsNode>(session.BuildView());
            var action = Assert.Single(view.Tabs[0].Children.OfType<ActionNode>());
            Assert.True(action.SupportsPlanning);
            Assert.Contains("game may reject", action.Description);
            var target = document.GetValue(RiftScalarWrites.BalanceId).AsInteger() - 1;
            var oldSpent = document.GetValue(RiftScalarWrites.SpentId);
            var plan = ((IActionPlanningDocument)document).PlanAction(action.ActionId, Parameters(target), new());
            Assert.Empty(plan.Errors);
            Assert.Equal(2, plan.Changes.Count);
            Assert.Contains(plan.Changes, c => c.Derived && c.ValueId == RiftScalarWrites.BalanceId && c.NewValue.AsInteger() == target);
            Assert.Contains(plan.Warnings, w => w.Contains("game may reject", StringComparison.Ordinal));
            var original = file.ReadAllBytes();
            Assert.Equal(original, Assert.Single(document.Serialize()).Content.ToArray());
            var preview = session.PlanAction(action.ActionId, Incoming(target));
            Assert.True(session.ApplyActionPlan(preview.PlanId!, preview.Revision).Ok);
            Assert.Equal(oldSpent, document.GetValue(RiftScalarWrites.SpentId));
            Assert.Equal(target, document.GetValue(RiftScalarWrites.BalanceId).AsInteger());
            var patched = Assert.Single(document.Serialize()).Content.ToArray();
            var atom = PlayerCorpusTests.Fields(file, RiftPlayerView.CampaignTag)["CareerBolts"];
            var width = atom.Kind == DdlValueKind.UInt16 ? 2 : 4;
            Assert.All(Enumerable.Range(0, original.Length).Where(i => original[i] != patched[i]), i => Assert.InRange(i, atom.Offset, atom.Offset + width - 1));
            var candidate = MemorySaveSet.SingleFile(file.FileName, patched, "PPSA01474");
            var decoder = new RiftEvidenceDecoder();
            var diff = SemanticDiffer.Compare(decoder.Decode(file, new()), decoder.Decode(candidate, new()), maxChanges: 10000);
            Assert.False(diff.Truncated);
            Assert.False(diff.InputTruncated);
            Assert.Equal("campaign/CareerBolts/0", Assert.Single(diff.Changes).Path);
            using var reopened = plugin.Open(candidate);
            _ = reopened.BuildView();
            Assert.Equal(target, reopened.GetValue(RiftScalarWrites.BalanceId).AsInteger());
            Assert.True(session.Undo().Ok);
            Assert.False(session.State.CanUndo);
            Assert.Equal(original, Assert.Single(document.Serialize()).Content.ToArray());
            Assert.True(session.Redo().Ok);
            Assert.Equal(patched, Assert.Single(document.Serialize()).Content.ToArray());
        }
    }

    [Fact]
    public void InvalidTargetsSelectionsForgedAndStalePlansCannotMutateTheSave()
    {
        var file = Assert.IsType<DiskSaveFile>(PlayerCorpusTests.Find("i29slot0-s-autosave", 1));
        using var document = new InsomniacPlugin().Open(file);
        using var session = new DocumentSession("invalid-plan", file, new InsomniacPlugin(), document);
        var action = Assert.Single(Assert.IsType<TabsNode>(session.BuildView()).Tabs[0].Children.OfType<ActionNode>());
        var maximum = Assert.IsType<IntegerSpec>(Assert.Single(action.Parameters).Spec).Max;
        var planner = (IActionPlanningDocument)document;
        foreach (var invalid in new[] { FieldValue.Of(-1L), FieldValue.Of(maximum + 1), FieldValue.Of("1"), FieldValue.Of(true), FieldValue.Null })
            Assert.NotEmpty(planner.PlanAction(action.ActionId, new Dictionary<string, FieldValue> { ["target"] = invalid }, new()).Errors);
        Assert.NotEmpty(planner.PlanAction(action.ActionId, new Dictionary<string, FieldValue>(), new()).Errors);
        Assert.NotEmpty(planner.PlanAction("unknown", Parameters(1), new()).Errors);
        Assert.NotEmpty(planner.PlanAction(action.ActionId, Parameters(1), new([])).Errors);
        Assert.NotEmpty(planner.PlanAction(action.ActionId, Parameters(1), new() { QueryScope = new("arsenal/weapons", new(), 0) }).Errors);
        var plan = planner.PlanAction(action.ActionId, Parameters(1), new());
        Assert.False(planner.ApplyActionPlan(action.ActionId, Parameters(1), new(), plan with { Changes = [] }).Succeeded);
        Assert.False(planner.ApplyActionPlan(action.ActionId, Parameters(1), new(), plan with { Warnings = [] }).Succeeded);
        Assert.Equal(file.ReadAllBytes(), Assert.Single(document.Serialize()).Content.ToArray());
        var preview = session.PlanAction(action.ActionId, Incoming(1));
        Assert.True(session.ApplyEdits([new(RiftScalarWrites.CareerId, JsonSerializer.SerializeToElement(FieldValue.Of(23445L)))]).Ok);
        var after = Assert.Single(document.Serialize()).Content.ToArray();
        Assert.True(session.ApplyActionPlan(preview.PlanId!, preview.Revision).Stale);
        Assert.False(planner.ApplyActionPlan(action.ActionId, Parameters(1), new(), plan).Succeeded);
        Assert.Equal(after, Assert.Single(document.Serialize()).Content.ToArray());
    }

    [Fact]
    public void BoundaryTargetsPersistAndNoOpIsExplainedWithoutInventingAChange()
    {
        using var document = new InsomniacPlugin().Open(PlayerCorpusTests.Find("i29slot0-s-autosave", 1));
        var action = Assert.Single(Assert.IsType<TabsNode>(document.BuildView()).Tabs[0].Children.OfType<ActionNode>());
        var planner = (IActionPlanningDocument)document;
        var current = document.GetValue(RiftScalarWrites.BalanceId).AsInteger();
        var noOp = planner.PlanAction(action.ActionId, Parameters(current), new());
        Assert.Empty(noOp.Changes);
        Assert.Single(noOp.SkippedRows);
        Assert.True(planner.ApplyActionPlan(action.ActionId, Parameters(current), new(), noOp).Succeeded);
        var maximum = Assert.IsType<IntegerSpec>(action.Parameters[0].Spec).Max - document.GetValue(RiftScalarWrites.SpentId).AsInteger();
        foreach (var target in new Int128[] { 0, maximum })
        {
            var plan = planner.PlanAction(action.ActionId, Parameters(target), new());
            Assert.Empty(plan.Errors);
            var result = planner.ApplyActionPlan(action.ActionId, Parameters(target), new(), plan);
            Assert.True(result.Succeeded);
            Assert.NotEmpty(result.Warnings);
            using var reopened = new InsomniacPlugin().Open(MemorySaveSet.SingleFile("i29-s.save", Assert.Single(document.Serialize()).Content.ToArray(), "PPSA01474"));
            _ = reopened.BuildView();
            Assert.Equal(target, reopened.GetValue(RiftScalarWrites.BalanceId).AsInteger());
        }
    }

    [Fact]
    public void UnsupportedTitlesAndPreferencesHaveNoBoltActionAndRejectPlanning()
    {
        var plugin = new InsomniacPlugin();
        var file = PlayerCorpusTests.Find("i29slot0-s-autosave", 0);
        using var foreign = plugin.Open(MemorySaveSet.SingleFile(file.FileName, file.ReadAllBytes()));
        Assert.Empty(Assert.IsType<TabsNode>(foreign.BuildView()).Tabs.SelectMany(t => t.Children).OfType<ActionNode>());
        Assert.NotEmpty(((IActionPlanningDocument)foreign).PlanAction(RiftBoltPlanning.ActionId, Parameters(1), new()).Errors);
        using var preferences = plugin.Open(SampleSaves.Load("PPSA01474").AllDiskFiles.First(f => f.FileName == "i29-userprefs.save"));
        Assert.NotEmpty(((IActionPlanningDocument)preferences).PlanAction(RiftBoltPlanning.ActionId, Parameters(1), new()).Errors);
    }

    [Fact]
    public void PreviewIncludesExpandedDiagnosticsAndSpendingEditsDoNotLeaveAStaleParameterLimit()
    {
        var plugin = new InsomniacPlugin();
        var file = Assert.IsType<DiskSaveFile>(PlayerCorpusTests.Find("i29slot0-s-autosave", 1));
        using var document = plugin.Open(file);
        using var session = new DocumentSession("dynamic-headroom", file, plugin, document);
        var action = Assert.Single(Assert.IsType<TabsNode>(session.BuildView()).Tabs[0].Children.OfType<ActionNode>());
        _ = session.Expand("system/d436a987");
        _ = session.Expand("ddl/d436a987/CareerBolts");
        Assert.True(session.ApplyEdits([new(RiftScalarWrites.SpentId, JsonSerializer.SerializeToElement(FieldValue.Of(0L)))]).Ok);
        var target = Assert.IsType<IntegerSpec>(action.Parameters[0].Spec).Max;
        var preview = session.PlanAction(action.ActionId, Incoming(target));
        Assert.Empty(preview.Plan.Errors);
        Assert.Contains(preview.Plan.Changes, c => c.ValueId == "ddl/d436a987/CareerBolts/0" && c.Derived && c.NewValue.AsInteger() == target);
        Assert.True(session.ApplyActionPlan(preview.PlanId!, preview.Revision).Ok);
        Assert.Equal(target, document.GetValue("ddl/d436a987/CareerBolts/0").AsInteger());
        Assert.True(session.Undo().Ok);
        Assert.Equal(23444, document.GetValue("ddl/d436a987/CareerBolts/0").AsInteger());
        Assert.True(session.Undo().Ok);
        Assert.Equal(file.ReadAllBytes(), Assert.Single(document.Serialize()).Content.ToArray());
    }

    private static IReadOnlyDictionary<string, FieldValue> Parameters(Int128 target) =>
        new Dictionary<string, FieldValue> { [RiftBoltPlanning.TargetParameter] = FieldValue.Of(target) };

    private static IReadOnlyDictionary<string, JsonElement> Incoming(Int128 target) =>
        new Dictionary<string, JsonElement> { [RiftBoltPlanning.TargetParameter] = JsonSerializer.SerializeToElement(FieldValue.Of(target)) };
}
