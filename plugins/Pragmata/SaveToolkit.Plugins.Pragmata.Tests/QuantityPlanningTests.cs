using System.Text.Json;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Core.Storage;
using SaveToolkit.Plugins.Pragmata.Crypto;
using SaveToolkit.Plugins.Pragmata.Editing;
using SaveToolkit.Plugins.Pragmata.Format;
using SaveToolkit.Plugins.Pragmata.Research;
using SaveToolkit.Plugins.Pragmata.Views;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;
using SaveToolkit.Testing.Diff;

namespace SaveToolkit.Plugins.Pragmata.Tests;

public sealed class QuantityPlanningTests
{
    private static readonly Dictionary<string, FieldValue> Parameters = new() { ["quantity"] = FieldValue.Of(17L) };

    [Fact]
    public void PreviewIsPureSelectionIsExactAndNoOpRowsAreExplained()
    {
        using var document = EditingTests.Gameplay();
        var original = document.Serialize().Single().Content.ToArray();
        var item = document.Layout.Items[0];
        var key = QuantityActions.RowKey(item);
        var scope = new ActionScope([key]);
        var plan = document.PlanAction(PragmataViews.QuantityAction, Parameters, scope);
        Assert.Empty(plan.Errors);
        Assert.Single(plan.Changes);
        Assert.Contains(plan.Warnings, warning => warning.Contains("Unknown", StringComparison.Ordinal));
        Assert.Equal(original, document.Serialize().Single().Content.ToArray());
        Assert.True(document.ApplyActionPlan(PragmataViews.QuantityAction, Parameters, scope, plan).Succeeded);
        Assert.Equal(17, document.Read(item.Find(KnownHashes.Quantity)!).AsInt32());
        var again = document.PlanAction(PragmataViews.QuantityAction, Parameters, scope);
        Assert.Empty(again.Changes);
        Assert.Equal(key, Assert.Single(again.SkippedRows).RowKey);
        var none = document.PlanAction(PragmataViews.QuantityAction, Parameters, new ActionScope([]));
        Assert.Empty(none.Errors);
        Assert.Empty(none.Changes);
        Assert.Empty(none.SkippedRows);
    }

    [Fact]
    public void MalformedSelectionsParametersAndForgedPlansAreAtomic()
    {
        using var document = EditingTests.Gameplay();
        var original = document.Serialize().Single().Content.ToArray();
        var key = QuantityActions.RowKey(document.Layout.Items[0]);
        foreach (var scope in new[]
        {
            new ActionScope(["not-an-inventory-record"]), new ActionScope([key, key]),
            new ActionScope { QueryScope = new ActionQueryScope("evidence", new QueryableCollectionQuery(), 0) },
        }) Assert.NotEmpty(document.PlanAction(PragmataViews.QuantityAction, Parameters, scope).Errors);
        foreach (var value in new[] { FieldValue.Of(-1L), FieldValue.Of((long)int.MaxValue + 1), FieldValue.Of("17"), FieldValue.Of(true) })
            Assert.NotEmpty(document.PlanAction(PragmataViews.QuantityAction,
                new Dictionary<string, FieldValue> { ["quantity"] = value }, new ActionScope()).Errors);
        Assert.NotEmpty(document.PlanAction("unknown", Parameters, new ActionScope()).Errors);
        Assert.NotEmpty(document.PlanAction(PragmataViews.QuantityAction, new Dictionary<string, FieldValue>(), new ActionScope()).Errors);
        var plan = document.PlanAction(PragmataViews.QuantityAction, Parameters, new ActionScope([key]));
        var forged = plan with { Changes = [plan.Changes.Single() with { NewValue = FieldValue.Of(99L) }] };
        Assert.False(document.ApplyActionPlan(PragmataViews.QuantityAction, Parameters, new ActionScope([key]), forged).Succeeded);
        Assert.Throws<InvalidOperationException>(() => document.InvokeAction(PragmataViews.QuantityAction, Parameters));
        Assert.Equal(original, document.Serialize().Single().Content.ToArray());
    }

    [Fact]
    public void DuplicateRawItemIdsFailPreflightWithoutChangingQuantities()
    {
        using var document = EditingTests.Gameplay();
        var first = document.Layout.Items[0].Find(KnownHashes.ItemId)!;
        var second = document.Layout.Items[1].Find(KnownHashes.ItemId)!;
        var raw = document.Field(second, raw: true);
        Assert.True(document.ApplyEdits([new(raw.ValueId, document.Read(first))]).Succeeded);
        var before = document.Serialize().Single().Content.ToArray();
        var plan = document.PlanAction(PragmataViews.QuantityAction, Parameters, new ActionScope());
        Assert.Contains(plan.Errors, error => error.Message.Contains("Duplicate inventory IDs", StringComparison.Ordinal));
        Assert.Equal(before, document.Serialize().Single().Content.ToArray());
    }

    [Fact]
    public void FrameworkRequiresPreviewRejectsStalePlansAndUndoesSelectionInOneStep()
    {
        var file = (DiskSaveFile)SampleEvidenceTests.Files().Single(file => file.FileName == "data000.bin");
        using var document = new PragmataDocument(file.RelativePath, file.ReadAllBytes());
        using var session = new DocumentSession("planned-quantity", file, new PragmataPlugin(), document);
        var table = ((TabsNode)session.BuildView()).Tabs.Single(tab => tab.Title == "Inventory").Children.OfType<TableNode>().Single();
        var parameters = new Dictionary<string, JsonElement> { ["quantity"] = JsonSerializer.SerializeToElement("17") };
        Assert.False(session.InvokeAction(PragmataViews.QuantityAction, parameters).Ok);
        var scope = new ActionScope(table.Rows.Take(3).Select(row => row.Key).ToArray());
        var preview = session.PlanAction(PragmataViews.QuantityAction, parameters, scope);
        Assert.NotNull(preview.PlanId);
        Assert.Equal(3, preview.Plan.Changes.Count);
        Assert.True(session.ApplyActionPlan(preview.PlanId, preview.Revision).Ok);
        var modified = session.Serialize().Single().Content.ToArray();
        Assert.False(session.ApplyActionPlan(preview.PlanId, preview.Revision).Ok);
        Assert.True(session.Undo().Ok);
        Assert.Equal(file.ReadAllBytes(), document.Serialize().Single().Content.ToArray());
        Assert.False(session.State.CanUndo);
        Assert.True(session.Redo().Ok);
        Assert.Equal(modified, session.Serialize().Single().Content.ToArray());
        var stale = session.PlanAction(PragmataViews.QuantityAction, parameters, new ActionScope());
        var target = EditingTests.Quantity(document);
        Assert.True(session.ApplyEdits([new IncomingEdit(target.Path, JsonSerializer.SerializeToElement("19"))]).Ok);
        var beforeStale = document.Serialize().Single().Content.ToArray();
        Assert.False(session.ApplyActionPlan(stale.PlanId!, stale.Revision).Ok);
        Assert.Equal(beforeStale, document.Serialize().Single().Content.ToArray());
    }

    [Fact]
    public void PreviewIncludesRawAliasesAndFrameworkUndoRestoresBothViews()
    {
        var file = (DiskSaveFile)SampleEvidenceTests.Files().Single(file => file.FileName == "data000.bin");
        using var document = new PragmataDocument(file.RelativePath, file.ReadAllBytes());
        using var session = new DocumentSession("quantity-alias", file, new PragmataPlugin(), document);
        var target = EditingTests.Quantity(document);
        IReadOnlyList<ViewNode> nodes = ((TabsNode)session.BuildView()).Tabs.Single(tab => tab.Title == "Raw").Children;
        var ancestors = new Stack<SaveNode>();
        for (var ancestor = target.Parent; ancestor is not null; ancestor = ancestor.Parent) ancestors.Push(ancestor);
        foreach (var ancestor in ancestors)
        {
            var section = nodes.OfType<SectionNode>().Single(section => section.LazyId!.StartsWith($"tree/{ancestor.Path}/", StringComparison.Ordinal));
            nodes = session.Expand(section.LazyId!);
        }
        var raw = nodes.OfType<FieldNode>().Single(field => field.ValueId == $"raw/{target.Path}");
        var item = document.Layout.Items.Single(item => item.Find(KnownHashes.Quantity) == target);
        var preview = session.PlanAction(PragmataViews.QuantityAction,
            new Dictionary<string, JsonElement> { ["quantity"] = JsonSerializer.SerializeToElement("17") },
            new ActionScope([QuantityActions.RowKey(item)]));
        Assert.NotNull(preview.PlanId);
        Assert.Equal(2, preview.Plan.Changes.Count);
        Assert.Equal(raw.ValueId, Assert.Single(preview.Plan.Changes, change => change.Derived).ValueId);
        Assert.True(session.ApplyActionPlan(preview.PlanId, preview.Revision).Ok);
        Assert.Equal(17, document.GetValue(raw.ValueId).AsInt32());
        Assert.True(session.Undo().Ok);
        Assert.Equal(raw.Value, document.GetValue(raw.ValueId));
        Assert.Equal(file.ReadAllBytes(), document.Serialize().Single().Content.ToArray());
    }

    [Fact]
    public void EverySampleBulkDeltaContainsOnlyNamedQuantitiesAndReversesByteIdentically()
    {
        // This F1 experiment proves storage preservation, not an in-game causal effect or legal cap.
        var decoder = new PragmataDecoder();
        var limits = new DecodeLimits { MaxValues = 1_000_000, MaxDepth = 100 };
        foreach (var file in SampleEvidenceTests.Files().Where(file => file.FileName != "data00-1.bin"))
        {
            var original = file.ReadAllBytes();
            using var document = new PragmataDocument(file.RelativePath, original);
            _ = document.BuildView();
            var plan = document.PlanAction(PragmataViews.QuantityAction, Parameters, new ActionScope());
            Assert.Empty(plan.Errors);
            var outcome = document.ApplyActionPlan(PragmataViews.QuantityAction, Parameters, new ActionScope(), plan);
            Assert.True(outcome.Succeeded);
            var before = new DsssContainer(original).Plaintext;
            var modified = document.Serialize().Single().Content.ToArray();
            var after = new DsssContainer(modified).Plaintext;
            var delta = SemanticDiffer.Compare(decoder.Decode(before, new SaveLayout(before).Roots, limits),
                decoder.Decode(after, new SaveLayout(after).Roots, limits), maxChanges: 1000);
            Assert.False(delta.InputTruncated);
            Assert.Equal(plan.Changes.Count, delta.Total);
            Assert.All(delta.Changes, change =>
            {
                Assert.EndsWith("/d18b252d", change.Path, StringComparison.Ordinal);
                Assert.Contains("/3185b7a5[key=1b5a30c0:", change.Path, StringComparison.Ordinal);
                Assert.Equal("17", change.After);
            });
            var allowed = document.Layout.Items.SelectMany(item =>
            {
                var quantity = item.Find(KnownHashes.Quantity)!;
                return Enumerable.Range(quantity.Offset, quantity.Size);
            }).ToHashSet();
            Assert.Equal(before.Length, after.Length);
            for (var index = 0; index < before.Length; index++)
                if (before[index] != after[index]) Assert.Contains(index, allowed);
            using var reopened = new PragmataDocument(file.RelativePath, modified);
            _ = reopened.BuildView();
            Assert.All(reopened.Layout.Items, item => Assert.Equal(17, reopened.Read(item.Find(KnownHashes.Quantity)!).AsInt32()));
            Assert.True(reopened.ApplyEdits(outcome.Changes.Select(change => new ValueEdit(change.ValueId, change.OldValue)).ToArray()).Succeeded);
            Assert.Equal(original, reopened.Serialize().Single().Content.ToArray());
        }
    }
}
