using System.Text.Json;
using SaveToolkit.Core.References;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Core.Storage;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Testing.Diff;

namespace SaveToolkit.Plugins.AtomicHeart.Tests;

public sealed class AtomicHeartRestoreSampleTests
{
    private static readonly IReadOnlyDictionary<string, JsonElement> NoParameters = new Dictionary<string, JsonElement>();

    [Fact]
    public void QueryRestoreIncludesRowsThatHaveNeverBeenLoadedIntoTheSession()
    {
        var file = Assert.IsType<DiskSaveFile>(WriteTestData.Sample());
        var references = Assert.IsType<ReferenceContext>(WriteTestData.References());
        using var document = WriteTestData.Plugin.Open(file, references);
        var fields = WriteTestData.Table(document).Rows.Select(row => WriteTestData.Field(row, "amount"))
            .Where(field => !field.Spec.ReadOnly).ToArray();
        // Stage before creating the host session so no table pages are registered there.
        Assert.True(document.ApplyEdits(fields.Select(field => new ValueEdit(field.ValueId, FieldValue.Of(0))).ToArray()).Succeeded);
        var reduced = WriteTestData.Output(document);
        using var session = new DocumentSession("atomic-restore-unloaded", file, WriteTestData.Plugin, document, references: references);
        _ = session.BuildView();
        var loaded = Assert.Single(session.QueryCollection(InventoryCollection.Id, new() { Limit = 1 }, expectedRevision: null, cancellationToken: TestContext.Current.CancellationToken).Rows);
        var scope = new ActionScope { QueryScope = new(InventoryCollection.Id, new(), session.State.Revision) {
            ExcludedRowKeys = [loaded.Key],
        } };
        var preview = session.PlanAction(AtomicHeartRestoreTests.RestoreId, NoParameters, scope, session.State.Revision, cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotNull(preview.PlanId);
        Assert.Equal(fields.Length - 1, preview.Plan.Changes.Count);
        Assert.True(preview.Plan.Changes.Count > 1);
        Assert.DoesNotContain(preview.Plan.Changes, change => change.ValueId == loaded.Key + "/amount");
        Assert.True(session.ApplyActionPlan(preview.PlanId!, session.State.Revision, cancellationToken: TestContext.Current.CancellationToken).Ok);
        var restored = WriteTestData.Output(document);
        _ = WriteTestData.IndependentDecode(file.ReadAllBytes(), restored);
        using (var reopened = WriteTestData.Open(restored))
            foreach (var field in fields)
                Assert.Equal(field.ValueId == loaded.Key + "/amount" ? FieldValue.Of(0) : field.Value,
                    reopened.GetValue(field.ValueId));
        Assert.True(session.Undo().Ok);
        Assert.Equal(reduced, WriteTestData.Output(document));
        Assert.False(session.State.CanUndo);
        Assert.True(session.Redo().Ok);
        Assert.Equal(restored, WriteTestData.Output(document));
    }

    [Theory]
    [InlineData("document", false)]
    [InlineData("selected", false)]
    [InlineData("query", false)]
    [InlineData("query", true)]
    public void RestorePreviewCancelApplyUndoRedoAndExportRetainOriginalBytesAndIntegrity(string mode, bool includeEmpty)
    {
        var file = Assert.IsType<DiskSaveFile>(WriteTestData.Sample());
        var original = file.ReadAllBytes();
        var index = SlotDataDecoder.Decode(original, SaveExtensionFile.Parse(original));
        var state = new InventoryState(index, null);
        var eligible = state.Items.Where(state.CanSetAmount).ToArray();
        Assert.True(eligible.Length > 2);
        var references = Assert.IsType<ReferenceContext>(WriteTestData.References());
        using var document = WriteTestData.Plugin.Open(file, references);
        using var session = new DocumentSession("atomic-restore-" + mode, file, WriteTestData.Plugin, document, references: references);
        _ = session.BuildView();
        _ = session.QueryCollection(InventoryCollection.Id, new() { IncludeEmpty = true, Limit = QueryableCollectionLimits.MaxPageSize }, expectedRevision: null, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(original, WriteTestData.Output(document));
        Assert.True(session.ApplyEdits(eligible.Select(item => new IncomingEdit(item.Key + "/amount",
            JsonSerializer.SerializeToElement(0))).ToArray(), session.State.Revision, cancellationToken: TestContext.Current.CancellationToken).Ok);
        var reduced = WriteTestData.Output(document);
        var reducedPlain = WriteTestData.IndependentDecode(original, reduced);
        var beforePreview = session.State;
        var selected = eligible.Take(2).ToArray();
        var excluded = eligible[0].Key;
        ActionScope scope = mode switch {
            "selected" => new(selected.Reverse().Select(item => item.Key).ToArray()),
            "query" => new() { QueryScope = new(InventoryCollection.Id,
                new() { IncludeEmpty = includeEmpty, Filters = new Dictionary<string, string> { ["amount"] = "0" }, Limit = 1 },
                session.State.Revision) { ExcludedRowKeys = [excluded] } },
            _ => new(),
        };
        var expected = mode == "selected" ? selected : mode == "query" ? eligible.Where(item => item.Key != excluded).ToArray() : eligible;
        var preview = session.PlanAction(AtomicHeartRestoreTests.RestoreId, NoParameters, scope, session.State.Revision, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Empty(preview.Plan.Errors);
        Assert.NotNull(preview.PlanId);
        Assert.Equal(expected.Select(item => item.Key + "/amount"), preview.Plan.Changes.Select(change => change.ValueId));
        Assert.Equal(expected.Select(item => item.Amount), preview.Plan.Changes.Select(change => change.NewValue.AsInt64()));
        Assert.All(preview.Plan.Changes, change => Assert.Equal(FieldValue.Of(0), change.OldValue));
        Assert.Equal(mode == "selected" || mode == "query" && !includeEmpty ? 0 : state.Items.Count - eligible.Length,
            preview.Plan.SkippedRows.Count);
        Assert.Contains(InventoryState.WriteWarning, preview.Plan.Warnings);
        // Cancel leaves both the live history/revision and exported bytes unchanged.
        Assert.Equal(beforePreview, session.State);
        Assert.Equal(reduced, WriteTestData.Output(document));
        var cancelledToken = preview.PlanId!;
        preview = session.PlanAction(AtomicHeartRestoreTests.RestoreId, NoParameters, scope, session.State.Revision, cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotEqual(cancelledToken, preview.PlanId);
        Assert.True(session.ApplyActionPlan(preview.PlanId!, session.State.Revision, cancellationToken: TestContext.Current.CancellationToken).Ok);
        var restored = WriteTestData.Output(document);
        var restoredPlain = WriteTestData.IndependentDecode(original, restored);
        var diff = ByteDiffer.Compare(reducedPlain, restoredPlain, options: new() { MergeGap = 0 });
        Assert.True(diff.HasChanges);
        Assert.False(diff.Truncated);
        Assert.All(diff.Ranges, range => Assert.Contains(expected,
            item => range.Offset >= item.Quantity.Payload.Offset && range.End <= item.Quantity.Payload.End));
        using (var reopened = WriteTestData.Open(restored))
        {
            foreach (var item in eligible)
                Assert.Equal(FieldValue.Of(expected.Contains(item) ? item.Amount : 0), reopened.GetValue(item.Key + "/amount"));
            Assert.Equal(restored, WriteTestData.Output(reopened));
        }
        if (mode == "document") Assert.Equal(original, restored);
        Assert.True(session.Undo().Ok);
        Assert.Equal(reduced, WriteTestData.Output(document));
        Assert.True(session.State.CanUndo); // Reductions and restore are separate history steps.
        Assert.True(session.Redo().Ok);
        Assert.Equal(restored, WriteTestData.Output(document));
        Assert.False(session.ApplyActionPlan(preview.PlanId!, session.State.Revision, cancellationToken: TestContext.Current.CancellationToken).Ok);
        Assert.True(session.Undo().Ok);
        Assert.True(session.Undo().Ok);
        Assert.False(session.State.CanUndo);
        Assert.Equal(original, WriteTestData.Output(document));
    }

    [Fact]
    public void RestorePreservesHigherAmountsAndAnExistingAssetReplacement()
    {
        var file = WriteTestData.Sample();
        var original = file.ReadAllBytes();
        var index = SlotDataDecoder.Decode(original, SaveExtensionFile.Parse(original));
        var state = new InventoryState(index, null);
        var higher = state.Items.First(item => state.CanSetAmount(item) && item.Amount < state.Limit(item));
        using var document = WriteTestData.Plugin.Open(file, WriteTestData.References());
        var weapon = WriteTestData.Table(document).Rows.Single(row => WriteTestData.Field(row, "asset").Value.AsString() == "DA_Item_Shved");
        var amount = WriteTestData.Field(weapon, "amount");
        var asset = WriteTestData.Field(weapon, "asset");
        Assert.NotEqual(higher.Key + "/amount", amount.ValueId);
        Assert.True(document.ApplyEdits([new(amount.ValueId, FieldValue.Of(0)),
            new(higher.Key + "/amount", FieldValue.Of(higher.Amount + 1)),
            new(asset.ValueId, FieldValue.Of("DA_Item_ShotgunKS23"))]).Succeeded);
        var before = WriteTestData.Output(document);
        var planner = Assert.IsAssignableFrom<IActionPlanningDocument>(document);
        var parameters = new Dictionary<string, FieldValue>();
        var plan = planner.PlanAction(AtomicHeartRestoreTests.RestoreId, parameters, new());
        Assert.Equal(amount.ValueId, Assert.Single(plan.Changes).ValueId);
        Assert.Equal(amount.Value, plan.Changes[0].NewValue);
        Assert.True(planner.ApplyActionPlan(AtomicHeartRestoreTests.RestoreId, parameters, new(), plan).Succeeded);
        var output = WriteTestData.Output(document);
        var diff = ByteDiffer.Compare(WriteTestData.IndependentDecode(original, before),
            WriteTestData.IndependentDecode(original, output), options: new() { MergeGap = 0 });
        // Replacement may shift later offsets; the weapon's Amount follows its resized asset.
        var after = new InventoryState(new RecordReader(WriteTestData.IndependentDecode(original, output)).Read(), null);
        var payload = after.Items.Single(item => item.Key == weapon.Key).Quantity.Payload;
        Assert.True(diff.HasChanges);
        Assert.All(diff.Ranges, range => Assert.True(range.Offset >= payload.Offset && range.End <= payload.End));
        using var reopened = WriteTestData.Open(output);
        Assert.Equal(FieldValue.Of(higher.Amount + 1), reopened.GetValue(higher.Key + "/amount"));
        Assert.Equal(FieldValue.Of("DA_Item_ShotgunKS23"), reopened.GetValue(asset.ValueId));
        Assert.Equal(amount.Value, reopened.GetValue(amount.ValueId));
        Assert.Empty(planner.PlanAction(AtomicHeartRestoreTests.RestoreId, parameters, new()).Changes);
    }
}
