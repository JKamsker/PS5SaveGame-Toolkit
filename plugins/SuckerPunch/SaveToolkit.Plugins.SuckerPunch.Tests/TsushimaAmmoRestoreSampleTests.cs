using System.Text.Json;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Core.Storage;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;
using SaveToolkit.Testing;
using static SaveToolkit.Plugins.SuckerPunch.Tests.SuckerPunchTestData;
using static SaveToolkit.Plugins.SuckerPunch.Tests.TsushimaAmmoRestoreTests;

namespace SaveToolkit.Plugins.SuckerPunch.Tests;

public sealed class TsushimaAmmoRestoreSampleTests
{
    private static readonly IReadOnlyDictionary<string, FieldValue> NoParameters = new Dictionary<string, FieldValue>();
    private static readonly IReadOnlyDictionary<string, JsonElement> NoInputs = new Dictionary<string, JsonElement>();

    [Fact]
    public void All25SamplesRestoreOnlyReducedEligibleAmmoWithExactCopiesChecksumAndNoOpRoundTrips()
    {
        var plugin = new SuckerPunchPlugin();
        var files = SampleSaves.Load(TsushimaTitle).AllDiskFiles.Where(file => plugin.Probe(file).IsMatch).ToArray();
        Assert.Equal(25, files.Length);
        var changedFiles = 0;
        foreach (var file in files)
        {
            var original = file.ReadAllBytes();
            using var document = Open(file);
            Assert.Equal(original, Bytes(document));
            var ammo = Inventory(document).Rows.Where(row => row.Cells["copy"] is FieldNode)
                .Select(row => (FieldNode)row.Cells["quantity"]).Where(field => !field.Spec.ReadOnly).ToArray();
            var reduced = ammo.Where(field => field.Value.AsUInt32() > 0).ToArray();
            Assert.True(document.ApplyEdits(reduced.Select(field => new ValueEdit(field.ValueId, FieldValue.Of(0L))).ToArray()).Succeeded);
            var staged = Bytes(document);
            var planner = (IActionPlanningDocument)document;
            var plan = planner.PlanAction(ActionId, NoParameters, new());
            Assert.Empty(plan.Errors);
            Assert.Equal(reduced.Select(field => field.ValueId), plan.Changes.Where(change => !change.Derived).Select(change => change.ValueId));
            foreach (var field in reduced)
            {
                Assert.Contains(plan.Changes, change => change.ValueId == field.ValueId && change.NewValue == field.Value && !change.Derived);
                Assert.Contains(plan.Changes, change => change.ValueId == field.ValueId + "-copy" && change.NewValue == field.Value && change.Derived);
            }
            Assert.Equal(staged, Bytes(document)); // Discarding the preview applies nothing.
            var outcome = planner.ApplyActionPlan(ActionId, NoParameters, new(), plan);
            Assert.True(outcome.Succeeded);
            Assert.Equal(plan.Warnings, outcome.Warnings);
            Assert.Equal(plan.Changes.Select(change => (change.ValueId, change.OldValue, change.NewValue)),
                outcome.Changes.Select(change => (change.ValueId, change.OldValue, change.NewValue)));
            var restored = Bytes(document);
            Assert.Equal(original, restored);
            Assert.Equal(Checksum(restored), Word(restored, 8));
            var allowed = reduced.Select(field => (WriteAssertions.CountOffset(original, field.ValueId.Split('/')[1]), 2 * sizeof(uint)))
                .Append((8, sizeof(uint))).ToArray();
            WriteAssertions.OnlyRangesChanged(staged, restored, allowed);
            using var reopened = Open(WithBytes(restored));
            Assert.All(plan.Changes, change => Assert.Equal(change.NewValue, reopened.GetValue(change.ValueId)));
            Assert.Empty(planner.PlanAction(ActionId, NoParameters, new()).Changes);
            if (reduced.Length > 0) changedFiles++;
        }
        Assert.True(changedFiles > 0);
    }

    [Fact]
    public void CoreRequiresPreviewAndKeepsOpeningBaselineAcrossCancelUndoRedoAndStaleRejection()
    {
        var file = Assert.IsType<DiskSaveFile>(Slot("suckerpunchUser8"));
        var plugin = new SuckerPunchPlugin();
        var document = plugin.Open(file);
        using var session = new DocumentSession("restore-ammo", file, plugin, document);
        _ = session.BuildView();
        var opening = document.GetValue(ArrowId);
        Assert.False(session.InvokeAction(ActionId, NoInputs, expectedRevision: null, cancellationToken: TestContext.Current.CancellationToken).Ok);
        Assert.True(session.ApplyEdits([new IncomingEdit(ArrowId, JsonSerializer.SerializeToElement("1"))], expectedRevision: null, cancellationToken: TestContext.Current.CancellationToken).Ok);
        Assert.True(session.ApplyEdits([new IncomingEdit(ArrowId, JsonSerializer.SerializeToElement("0"))], expectedRevision: null, cancellationToken: TestContext.Current.CancellationToken).Ok);
        var staged = Bytes(document);
        var cancelled = session.PlanAction(ActionId, NoInputs, new(["ArrowNormal"]), expectedRevision: null, cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotNull(cancelled.PlanId);
        Assert.Equal(staged, Bytes(document));
        _ = session.PlanAction(ActionId, NoInputs, new(["ArrowNormal"]), expectedRevision: null, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(session.ApplyActionPlan(cancelled.PlanId!, cancelled.Revision, cancellationToken: TestContext.Current.CancellationToken).Stale);
        Assert.Equal(staged, Bytes(document));
        var preview = session.PlanAction(ActionId, NoInputs, new(["ArrowNormal"]), expectedRevision: null, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(opening, Assert.Single(preview.Plan.Changes, change => !change.Derived).NewValue);
        Assert.True(session.ApplyActionPlan(preview.PlanId!, preview.Revision, cancellationToken: TestContext.Current.CancellationToken).Ok);
        Assert.Equal(opening, document.GetValue(ArrowId));
        Assert.Equal(file.ReadAllBytes(), Bytes(document));
        Assert.True(session.Undo().Ok);
        Assert.Equal(staged, Bytes(document));
        Assert.True(session.State.CanUndo); // Earlier staged reductions remain a separate history step.
        Assert.True(session.Redo().Ok);
        Assert.Equal(file.ReadAllBytes(), Bytes(document));
        Assert.True(session.Undo().Ok);
        var stale = session.PlanAction(ActionId, NoInputs, new(["ArrowNormal"]), expectedRevision: null, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(session.ApplyEdits([new IncomingEdit(ArrowId, JsonSerializer.SerializeToElement("2"))], expectedRevision: null, cancellationToken: TestContext.Current.CancellationToken).Ok);
        var current = Bytes(document);
        Assert.True(session.ApplyActionPlan(stale.PlanId!, stale.Revision, cancellationToken: TestContext.Current.CancellationToken).Stale);
        Assert.Equal(current, Bytes(document));
        var fresh = session.PlanAction(ActionId, NoInputs, new(["ArrowNormal"]), expectedRevision: null, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(opening, Assert.Single(fresh.Plan.Changes, change => !change.Derived).NewValue);
    }

    [Fact]
    public void CoreRestoresMultipleAmmoKindsInOneUndoRedoStep()
    {
        var file = Assert.IsType<DiskSaveFile>(Slot("suckerpunchUser8"));
        var plugin = new SuckerPunchPlugin();
        var document = plugin.Open(file);
        using var session = new DocumentSession("restore-many", file, plugin, document);
        _ = session.BuildView();
        var rows = Inventory(document).Rows.Where(row => row.Cells["copy"] is FieldNode)
            .Where(row => row.Cells["quantity"] is FieldNode { Spec.ReadOnly: false } field && field.Value.AsUInt32() > 0).ToArray();
        Assert.True(rows.Length >= 2);
        Assert.True(session.ApplyEdits(rows.Select(row => new IncomingEdit(((FieldNode)row.Cells["quantity"]).ValueId,
            JsonSerializer.SerializeToElement("0"))).ToArray(), expectedRevision: null, cancellationToken: TestContext.Current.CancellationToken).Ok);
        var staged = Bytes(document);
        var preview = session.PlanAction(ActionId, NoInputs, new(rows.Select(row => row.Key).ToArray()), expectedRevision: null, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(rows.Length, preview.Plan.Changes.Count(change => !change.Derived));
        Assert.True(session.ApplyActionPlan(preview.PlanId!, preview.Revision, cancellationToken: TestContext.Current.CancellationToken).Ok);
        Assert.Equal(file.ReadAllBytes(), Bytes(document));
        Assert.True(session.Undo().Ok);
        Assert.Equal(staged, Bytes(document));
        Assert.True(session.Redo().Ok);
        Assert.Equal(file.ReadAllBytes(), Bytes(document));
    }

    private static byte[] Bytes(ISaveDocument document) => SuckerPunchTestData.PrimaryOutput(document).Content.ToArray();
}
