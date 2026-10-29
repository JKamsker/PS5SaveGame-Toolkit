using System.Buffers.Binary;
using System.Text.Json;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.Witcher3.Tests;

public sealed class WitcherGwentPlanningTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    public void EverySampleBatchHasPurePreviewExactExportAndSingleUndoRedo(int target)
    {
        var plugin = new Witcher3Plugin();
        var references = ReferencePackFixture.Pin(plugin);
        foreach (var file in EnabledWriteFixture.Files())
        {
            var input = file.ReadAllBytes();
            var before = EnabledWriteFixture.Read(input);
            using var document = plugin.Open(file, references);
            using var session = new DocumentSession("gwent-batch", file, plugin, document, references: references);
            _ = session.BuildView();
            // Only render one row: planning must bind changes on all remaining query pages itself.
            var row = Assert.Single(session.QueryCollection(GwentCollection.CollectionId, new() { Limit = 1 }).Rows);
            var action = Assert.IsType<ActionNode>(row.Cells[GwentPlanning.ActionColumn]);
            Assert.Equal(GwentPlanning.ActionId, action.ActionId);
            Assert.True(action.SupportsPlanning);
            Assert.Contains("Inferred", action.Description, StringComparison.Ordinal);
            var excluded = before.Features.Cards!.First(c => !c.Leader);
            var scope = new ActionScope { QueryScope = new(GwentCollection.CollectionId, new() { Limit = 1 },
                session.State.Revision) { ExcludedRowKeys = [GwentCollection.Key(excluded)] } };
            var parameters = Wire(target);
            var preview = session.PlanAction(GwentPlanning.ActionId, parameters, scope);
            Assert.NotNull(preview.PlanId);
            Assert.Empty(preview.Plan.Errors);
            Assert.Equal(before.Features.Cards!.Count - 1, preview.Scope.RowKeys!.Count);
            var changed = before.Features.Cards.Where(c => !c.Leader && c.Index != excluded.Index && c.Copies != target).ToArray();
            Assert.NotEmpty(changed);
            Assert.Equal(changed.Select(c => new GwentCopies(c).ValueId).Order(), preview.Plan.Changes.Select(c => c.ValueId).Order());
            Assert.All(preview.Plan.Changes, c => {
                Assert.Equal(FieldValue.Of(target), c.NewValue);
                Assert.True(c.OldExists && c.NewExists);
                Assert.False(c.Derived);
            });
            Assert.Equal(4, preview.Plan.SkippedRows.Count(s => s.Reason.Contains("Leader", StringComparison.Ordinal)));
            Assert.Contains(preview.Plan.Warnings, w => w.StartsWith(WriteFields.ConsoleWarning, StringComparison.Ordinal));
            Assert.Equal(input, Assert.Single(document.Serialize()).Content.ToArray()); // Cancel = discard preview.
            Assert.False(session.State.CanUndo);
            Assert.False(session.InvokeAction(GwentPlanning.ActionId, parameters).Ok);
            var applied = session.ApplyActionPlan(preview.PlanId, preview.Revision);
            Assert.True(applied.Ok);
            Assert.Equal(preview.Plan.Changes.Count, applied.Changes.Count);
            Assert.Equal(preview.Plan.Warnings, applied.Warnings);
            var output = Assert.Single(document.Serialize()).Content.ToArray();
            var expected = before.Container.Payload.ToArray();
            foreach (var card in changed) { BinaryPrimitives.WriteInt32LittleEndian(expected.AsSpan(card.CopiesOffset), target); }
            // Exact whole-payload audit covers deck, leaders, identities, unknown data and directory pointers.
            Assert.Equal(expected, WitcherContainer.Parse(output).Payload);
            var reopened = EnabledWriteFixture.Read(output);
            Assert.Equal(before.Features.Cards.Select(c => (c.Index, c.Leader, Copies: changed.Contains(c) ? target : c.Copies)),
                reopened.Features.Cards!.Select(c => (c.Index, c.Leader, c.Copies)));
            EnabledWriteFixture.SemanticChanges(input, output, changed.Select(c => $"gwent/collection/{c.Index}/copies").ToArray());
            Assert.True(session.Undo().Ok);
            Assert.Equal(input, Assert.Single(document.Serialize()).Content.ToArray());
            Assert.False(session.State.CanUndo);
            Assert.True(session.Redo().Ok);
            Assert.Equal(output, Assert.Single(document.Serialize()).Content.ToArray());
        }
    }

    [Fact]
    public void ExplicitSelectionRejectsForgedStaleAndInvalidPlansWithoutMutation()
    {
        var file = EnabledWriteFixture.Files()[0];
        var input = file.ReadAllBytes();
        var plugin = new Witcher3Plugin();
        using var document = plugin.Open(file, ReferencePackFixture.Pin(plugin));
        _ = document.BuildView();
        var card = EnabledWriteFixture.Read(input).Features.Cards!.First(c => !c.Leader);
        var scope = new ActionScope([GwentCollection.Key(card)]);
        var parameters = Parameters(0);
        var planner = (IActionPlanningDocument)document;
        var preview = planner.PlanAction(GwentPlanning.ActionId, parameters, scope);
        var change = Assert.Single(preview.Changes);
        Assert.Equal(FieldValue.Of(card.Copies), change.OldValue);
        Assert.Contains($"{card.Index}", change.Label, StringComparison.Ordinal);
        foreach (var forged in new[] {
            preview with { Changes = [change with { NewValue = FieldValue.Of(3) }] },
            preview with { Warnings = [] }, preview with { SkippedRows = [new("x", "forged")] },
        }) { Assert.False(planner.ApplyActionPlan(GwentPlanning.ActionId, parameters, scope, forged).Succeeded); }
        Assert.Equal(input, Assert.Single(document.Serialize()).Content.ToArray());
        Assert.True(planner.ApplyActionPlan(GwentPlanning.ActionId, parameters, scope, preview).Succeeded);
        Assert.False(planner.ApplyActionPlan(GwentPlanning.ActionId, parameters, scope, preview).Succeeded);
        var expected = EnabledWriteFixture.Read(input).Container.Payload.ToArray();
        BinaryPrimitives.WriteInt32LittleEndian(expected.AsSpan(card.CopiesOffset), 0);
        Assert.Equal(expected, WitcherContainer.Parse(Assert.Single(document.Serialize()).Content.ToArray()).Payload);
        foreach (var bad in new[] { new ActionScope(["unknown"]), new ActionScope([scope.RowKeys![0], scope.RowKeys[0]]),
            new ActionScope { QueryScope = new(GwentCollection.CollectionId, new(), 0) },
            new ActionScope(scope.RowKeys) { QueryScope = new(InventoryViews.CollectionId, new(), 0) },
        }) { Assert.NotEmpty(planner.PlanAction(GwentPlanning.ActionId, parameters, bad).Errors); }
        var empty = planner.PlanAction(GwentPlanning.ActionId, parameters, new([]));
        Assert.Empty(empty.Changes);
        Assert.Empty(empty.SkippedRows);
        Assert.True(planner.ApplyActionPlan(GwentPlanning.ActionId, parameters, new([]), empty).Succeeded);
        Assert.Throws<KeyNotFoundException>(() => document.InvokeAction(GwentPlanning.ActionId, parameters));
    }

    [Fact]
    public void CoreInvalidatesCancelledPreviewAndStaleRevisionAndHonorsZeroQueryMatches()
    {
        var file = EnabledWriteFixture.Files()[0];
        var plugin = new Witcher3Plugin();
        var references = ReferencePackFixture.Pin(plugin);
        using var document = plugin.Open(file, references);
        using var session = new DocumentSession("gwent-stale", file, plugin, document, references: references);
        _ = session.BuildView();
        var cancelled = session.PlanAction(GwentPlanning.ActionId, Wire(0));
        _ = session.PlanAction(GwentPlanning.ActionId, Wire(3));
        Assert.False(session.ApplyActionPlan(cancelled.PlanId!, cancelled.Revision).Ok);
        var fresh = session.PlanAction(GwentPlanning.ActionId, Wire(3));
        Assert.True(session.ApplyActionPlan(fresh.PlanId!, fresh.Revision).Ok);
        Assert.True(session.ApplyActionPlan(fresh.PlanId!, fresh.Revision).Stale);
        var none = session.PlanAction(GwentPlanning.ActionId, Wire(0), new() {
            QueryScope = new(GwentCollection.CollectionId, new() { Text = "no such card" }, session.State.Revision),
        });
        Assert.Empty(none.Scope.RowKeys!);
        Assert.Empty(none.Plan.Changes);
        Assert.Empty(none.Plan.SkippedRows);
    }

    [Fact]
    public void FilteredSelectionKeepsExclusionsAndCanSelectNoRows()
    {
        var file = EnabledWriteFixture.Files()[0];
        var plugin = new Witcher3Plugin();
        var references = ReferencePackFixture.Pin(plugin);
        using var document = plugin.Open(file, references);
        using var session = new DocumentSession("gwent-filter", file, plugin, document, references: references);
        _ = session.BuildView();
        var ordinary = EnabledWriteFixture.Read(file.ReadAllBytes()).Features.Cards!.Where(c => !c.Leader).ToArray();
        var excluded = GwentCollection.Key(ordinary[0]);
        var query = new QueryableCollectionQuery {
            Filters = new Dictionary<string, string> { ["kind"] = "Ordinary collection" },
            SortColumn = "index", Descending = true, Limit = 1,
        };
        var scope = new ActionScope { QueryScope = new(GwentCollection.CollectionId, query, session.State.Revision) {
            ExcludedRowKeys = [excluded, "not-a-match"],
        } };
        var preview = session.PlanAction(GwentPlanning.ActionId, Wire(0), scope);
        Assert.NotNull(preview.PlanId);
        Assert.Equal(ordinary.Where(c => GwentCollection.Key(c) != excluded).OrderByDescending(c => c.Index).Select(GwentCollection.Key),
            preview.Scope.RowKeys);
        Assert.Empty(preview.Plan.SkippedRows);
        Assert.Equal(ordinary.Length - 1, preview.Plan.Changes.Count);
        Assert.True(session.ApplyActionPlan(preview.PlanId, preview.Revision).Ok);
        var none = session.PlanAction(GwentPlanning.ActionId, Wire(3), scope with {
            QueryScope = scope.QueryScope with { Revision = session.State.Revision, ExcludedRowKeys = ordinary.Select(GwentCollection.Key).ToArray() },
        });
        Assert.Empty(none.Scope.RowKeys!);
        Assert.Empty(none.Plan.Changes);
        Assert.Empty(none.Plan.SkippedRows);
        var staleScope = session.PlanAction(GwentPlanning.ActionId, Wire(1), scope);
        Assert.True(staleScope.Stale);
    }

    internal static Dictionary<string, FieldValue> Parameters(int target) => new() { [GwentPlanning.CopiesParameter] = FieldValue.Of(target) };
    private static Dictionary<string, JsonElement> Wire(int target) => new() {
        [GwentPlanning.CopiesParameter] = JsonSerializer.SerializeToElement(FieldValue.Of(target)),
    };
}
