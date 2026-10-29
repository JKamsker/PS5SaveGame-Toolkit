using System.Buffers.Binary;
using SaveToolkit.Core.Editing;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;
using SaveToolkit.Testing;
using static SaveToolkit.Plugins.GodOfWarRagnarok.Tests.RagnarokRestoreFixture;

namespace SaveToolkit.Plugins.GodOfWarRagnarok.Tests;

public sealed class RagnarokRestoreSelectionTests
{
    [Fact]
    public void OnlyTheMainRestoreControlDeclaresTheResourceTableBinding()
    {
        using var fixture = new RagnarokRestoreFixture();
        var root = Assert.IsType<TabsNode>(fixture.Root);
        var tab = Assert.Single(root.Tabs, tab => tab.Title == "Resources & XP");
        var action = Assert.Single(tab.Children.OfType<ActionNode>());
        Assert.Equal(ActionId, action.ActionId);
        Assert.True(action.SupportsPlanning);
        Assert.Equal(CollectionId, action.SelectionTableId);
        Assert.Single(tab.Children.OfType<QueryableTableNode>(), table => table.CollectionId == action.SelectionTableId);
        Assert.Single(RagnarokTestSupport.EagerNodes(root).OfType<ActionNode>(), node => node.SelectionTableId is not null);
        var index = new ViewIndex();
        index.Add(root);
        Assert.Empty(index.SelectionDiagnostics);

        foreach (var collection in new[] { CollectionId, "xp", "collectibles" })
        foreach (var row in fixture.Document.Query(collection, new()).Rows)
        {
            var restore = Assert.IsType<ActionNode>(row.Cells["restore"]);
            Assert.Equal(ActionId, restore.ActionId);
            Assert.True(restore.SupportsPlanning);
            Assert.Null(restore.SelectionTableId);
            Assert.False(Assert.IsType<ActionNode>(row.Cells["inspect"]).Mutates);
        }
        Assert.Null(RagnarokWrites.RestoreAction().SelectionTableId);
        Assert.Equal(fixture.Original, fixture.Serialized());
    }

    [Theory]
    [InlineData("xp", 0x9C7CF61C839B9A8Aul)]
    [InlineData("collectibles", 0x482780EFF18ED57Eul)]
    public void OtherCounterTablesRetainTheirUnboundCellActions(string collection, ulong id)
    {
        var bytes = Bytes();
        var offset = RagnarokNestedFixture.FirstWalletOffset + RagnarokFormat.WalletHeaderSize;
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(offset), id);
        using var document = new GodOfWarRagnarokPlugin().Open(RagnarokTestSupport.GameplayFile(bytes));
        var row = Assert.Single(((IQueryableDocument)document).Query(collection, new()).Rows);
        var action = Assert.IsType<ActionNode>(row.Cells["restore"]);
        Assert.Equal(ActionId, action.ActionId);
        Assert.True(action.SupportsPlanning);
        Assert.Null(action.SelectionTableId);
        var field = Assert.IsType<FieldNode>(row.Cells["quantity"]);
        Assert.True(document.ApplyEdits([new(field.ValueId, FieldValue.Of(1L))]).Succeeded);
        var plan = ((IActionPlanningDocument)document).PlanAction(action.ActionId,
            new Dictionary<string, FieldValue>(), new([row.Key]));
        Assert.Equal(field.ValueId, Assert.Single(plan.Changes).ValueId);
        Assert.Empty(plan.Errors);
    }

    [Fact]
    public void SelectedRestoreIsPureAndOneUndoGroupWithoutAffectingOtherWalletsOrTime()
    {
        using var fixture = new RagnarokRestoreFixture();
        fixture.Reduce();
        var reduced = fixture.Serialized();
        var preview = fixture.Session.PlanAction(ActionId, Parameters, new([RowKey(2), RowKey(0)]), expectedRevision: null,
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotNull(preview.PlanId);
        Assert.Equal(new[] { ValueId(0), ValueId(2) }, preview.Plan.Changes.Select(change => change.ValueId));
        Assert.All(preview.Plan.Changes, change => Assert.Equal(FieldValue.Of((long)ImportedQuantity), change.NewValue));
        Assert.Empty(preview.Plan.SkippedRows);
        Assert.Contains(RagnarokWrites.IntegrityWarning, preview.Plan.Warnings);
        Assert.Contains(RagnarokWrites.EvidenceWarning, preview.Plan.Warnings);
        Assert.Equal(reduced, fixture.Serialized());
        Assert.True(fixture.Session.ApplyActionPlan(preview.PlanId, preview.Revision, TestContext.Current.CancellationToken).Ok);
        Assert.Equal(fixture.Expected(ImportedQuantity, 2, ImportedQuantity), fixture.Serialized());
        Assert.True(fixture.Session.Undo().Ok);
        Assert.Equal(reduced, fixture.Serialized());
        Assert.True(fixture.Session.Redo().Ok);
        Assert.Equal(fixture.Expected(ImportedQuantity, 2, ImportedQuantity), fixture.Serialized());
    }

    [Theory]
    [InlineData("explicit")]
    [InlineData("excluded query")]
    [InlineData("no matches")]
    public void EmptySelectionsNeverRestoreAllReducedCounters(string kind)
    {
        using var fixture = new RagnarokRestoreFixture();
        fixture.Reduce();
        var reduced = fixture.Serialized();
        var scope = kind == "explicit" ? new ActionScope([]) : new ActionScope
        {
            QueryScope = new(CollectionId, new() { Text = kind == "no matches" ? "no matching resource" : "Hacksilver" },
                fixture.Session.State.Revision)
            { ExcludedRowKeys = Enumerable.Range(0, WalletCount).Select(RowKey).ToArray() },
        };
        var preview = fixture.Session.PlanAction(ActionId, Parameters, scope, expectedRevision: null,
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotNull(preview.PlanId);
        Assert.NotNull(preview.Scope.RowKeys);
        Assert.Empty(preview.Scope.RowKeys);
        Assert.Empty(preview.Plan.Changes);
        Assert.Empty(preview.Plan.SkippedRows);
        Assert.Equal(reduced, fixture.Serialized());
        Assert.True(fixture.Session.ApplyActionPlan(preview.PlanId, preview.Revision, TestContext.Current.CancellationToken).Ok);
        Assert.Equal(reduced, fixture.Serialized());
    }

    [Fact]
    public void DocumentFallbackStillRestoresAllCountersAndDirectInvocationRemainsNonMutating()
    {
        using var fixture = new RagnarokRestoreFixture();
        fixture.Reduce();
        var reduced = fixture.Serialized();
        var direct = fixture.Document.InvokeAction(ActionId, new Dictionary<string, FieldValue>());
        Assert.Empty(direct.Changes);
        Assert.Contains("F6", direct.Message!, StringComparison.Ordinal);
        Assert.Contains(RagnarokWrites.IntegrityWarning, direct.Warnings);
        Assert.False(fixture.Session.InvokeAction(ActionId, Parameters, expectedRevision: null,
            cancellationToken: TestContext.Current.CancellationToken).Ok);
        Assert.Equal(reduced, fixture.Serialized());

        var preview = fixture.Session.PlanAction(ActionId, Parameters, scope: null, expectedRevision: null,
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotNull(preview.PlanId);
        Assert.Null(preview.Scope.RowKeys);
        Assert.Null(preview.Scope.QueryScope);
        Assert.Equal(Enumerable.Range(0, WalletCount).Select(ValueId), preview.Plan.Changes.Select(change => change.ValueId));
        Assert.True(fixture.Session.ApplyActionPlan(preview.PlanId, preview.Revision, TestContext.Current.CancellationToken).Ok);
        Assert.Equal(fixture.Expected(ImportedQuantity, ImportedQuantity, ImportedQuantity), fixture.Serialized());
        Assert.True(fixture.Session.Undo().Ok);
        Assert.Equal(reduced, fixture.Serialized());
        Assert.True(fixture.Session.Redo().Ok);
        Assert.Equal(fixture.Expected(ImportedQuantity, ImportedQuantity, ImportedQuantity), fixture.Serialized());
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("time")]
    [InlineData("duplicate")]
    [InlineData("foreign collection")]
    public void IneligibleSelectionsRejectWithoutWriting(string kind)
    {
        using var fixture = new RagnarokRestoreFixture();
        fixture.Reduce();
        ActionScope scope = kind switch
        {
            "time" => new ActionScope([TimeId]),
            "duplicate" => new([RowKey(0), RowKey(0)]),
            "foreign collection" => new() { QueryScope = new("flags", new(), fixture.Session.State.Revision) },
            _ => new(["unknown"]),
        };
        var preview = fixture.Session.PlanAction(ActionId, Parameters, scope, expectedRevision: null,
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.Null(preview.PlanId);
        Assert.NotEmpty(preview.Plan.Errors);
        Assert.Equal(fixture.Expected(1, 2, 3), fixture.Serialized());
    }

    [Fact]
    public void StagedSelectionDeclarationConformanceHasMeaningfulOwnedEffects()
    {
        var ownership = Enumerable.Range(0, WalletCount).ToDictionary(RowKey,
            wallet => (IReadOnlyList<string>)[ValueId(wallet)], StringComparer.Ordinal);
        var fixture = new SelectionScopeCase(ActionId, CollectionId, [RowKey(0), RowKey(2)])
        {
            Stage = document =>
            {
                _ = ((IQueryableDocument)document).Query(CollectionId, new() { Limit = WalletCount });
                var edits = Enumerable.Range(0, WalletCount)
                    .Select(wallet => new ValueEdit(ValueId(wallet), FieldValue.Of((long)wallet + 1))).ToArray();
                Assert.True(document.ApplyEdits(edits).Succeeded);
            },
            RowOwnedValueIds = ownership,
            ExpectedChangedValueIds = [ValueId(0), ValueId(2)],
        };
        var result = SelectionScopeFixture.Evaluate(() => new GodOfWarRagnarokPlugin().Open(RagnarokRestoreFixture.File()), fixture);
        // NotEvaluated (including a traversal cut or no-op plan) must never count as a pass.
        Assert.True(result.Status == SelectionScopeStatus.Passed, $"{result.Status}: {string.Join("; ", result.Diagnostics)}");
    }
}
