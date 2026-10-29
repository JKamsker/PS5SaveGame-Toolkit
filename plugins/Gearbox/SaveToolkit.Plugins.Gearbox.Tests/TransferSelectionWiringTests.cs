using System.Text.Json;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.Gearbox.Tests;

public sealed class TransferSelectionWiringTests
{
    private static readonly Dictionary<string, JsonElement> Empty = [];
    private static readonly bool[] GameVariants = [false, true];
    private static readonly string[] ActionIds = ["transfer/deposit", "transfer/withdraw", "transfer/claim"];
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public static IEnumerable<object[]> Cases =>
        from wonderlands in GameVariants
        from action in ActionIds
        select new object[] { wonderlands, action };

    [Theory]
    [MemberData(nameof(Cases))]
    public void BoundTransfer_ExplicitAndExcludedQueriesMoveOnlySelectedRowsWithOneAtomicUndo(bool wonderlands, string actionId)
    {
        var observer = new SelectionGroupObserver();
        using var workspace = SelectionTestData.Workspace(wonderlands, observer);
        var session = Open(workspace);
        var action = Assert.Single(Assert.IsType<SectionNode>(session.BuildView()).Children.OfType<ActionNode>(), action => action.ActionId == actionId);
        var source = action.SelectionTableId!;
        var target = actionId == "transfer/deposit" ? "profile/bank_inventory_list" : "character/inventory_items";
        var originals = workspace.Set.AllDiskFiles.ToDictionary(file => file.RelativePath, file => file.ReadAllBytes());
        Assert.Equal(2, originals.Count);
        SelectionByteAssertions.Equal(observer.Current, originals);
        Assert.Empty(session.Serialize(cancellationToken: Ct));
        var sourceRows = session.QueryCollection(source, new() { Limit = 10 }, expectedRevision: null, cancellationToken: Ct).Rows.Select(row => row.Key).ToArray();
        var targetRows = session.QueryCollection(target, new() { Limit = 10 }, expectedRevision: null, cancellationToken: Ct).Rows.Select(row => row.Key).ToArray();

        foreach (var querySelection in new[] { false, true })
        {
            Assert.Single(session.QueryCollection(source, new() { Limit = 1 }, expectedRevision: null, cancellationToken: Ct).Rows);
            var selected = querySelection ? new[] { sourceRows[2], sourceRows[0] } : [sourceRows[0]];
            var scope = querySelection ? new ActionScope
            {
                QueryScope = new(source, new() { Offset = 1, Limit = 1, SortColumn = "index", Descending = true }, session.State.Revision)
                { ExcludedRowKeys = [sourceRows[1]] },
            } : new(selected);
            var preview = session.PlanAction(action.ActionId, Empty, scope, expectedRevision: null, cancellationToken: Ct);
            Assert.Empty(preview.Plan.Errors);
            Assert.Equal(selected, preview.Scope.RowKeys);
            Assert.Equal(selected.Order(StringComparer.Ordinal), preview.Plan.Changes.Where(change => !change.Derived && !change.NewExists)
                .Select(change => change.ValueId).Order(StringComparer.Ordinal));
            Assert.DoesNotContain(preview.Plan.Changes, change => change.ValueId == sourceRows[1]);
            Assert.Equal(2, preview.Plan.Changes.Count(change => change.Derived && change.ValueId.EndsWith("/count", StringComparison.Ordinal)));
            SelectionByteAssertions.Equal(observer.Current, originals);
            Assert.Empty(session.Serialize(cancellationToken: Ct));
            Assert.False(session.State.CanUndo);
            Assert.True(session.ApplyActionPlan(preview.PlanId!, preview.Revision, cancellationToken: Ct).Ok);
            var changed = SelectionByteAssertions.Capture(observer.Current, originals.Keys.ToArray());
            Assert.Equal(2, changed.Count);
            AssertExportedBytes(session, changed);
            var remaining = session.QueryCollection(source, new() { Limit = 10 }, expectedRevision: null, cancellationToken: Ct).Rows.Select(row => row.Key).ToArray();
            Assert.Equal(sourceRows.Where(row => !selected.Contains(row)), remaining);
            var moved = session.QueryCollection(target, new() { Limit = 10 }, expectedRevision: null, cancellationToken: Ct).Rows.Select(row => row.Key).ToArray();
            Assert.Equal(targetRows, moved.Take(targetRows.Length));
            var role = source.Split('/')[0];
            var movedIds = selected.Order(StringComparer.Ordinal).Select(row => target + "/" + role + ":" + row[(source.Length + 1)..]);
            Assert.Equal(movedIds, moved.Skip(targetRows.Length));
            foreach (var file in workspace.Set.AllDiskFiles)
            {
                using var baseline = (GearboxDocument)new GearboxPlugin().Open(file);
                using var reopened = (GearboxDocument)new GearboxPlugin().Open(GearboxTestData.Overlay(file, changed[file.RelativePath]));
                SerialWriteTests.AssertUnrelated(baseline.Root, reopened.Root, source.Split('/')[1], target.Split('/')[1], "equipped_inventory_list");
                Assert.Equal(changed[file.RelativePath], reopened.Serialize().Single().Content.ToArray());
                if (file.FileName == "1")
                    Assert.Equal(actionId != "transfer/deposit" ? new long[] { 0, 2 } : querySelection ? [-1, -1] : [-1, 1],
                        reopened.Root.Children("equipped_inventory_list").Select(slot => slot.Integer("inventory_list_index")));
            }
            Assert.True(session.Undo().Ok);
            SelectionByteAssertions.Equal(observer.Current, originals);
            Assert.Empty(session.Serialize(cancellationToken: Ct));
            Assert.Equal(sourceRows, session.QueryCollection(source, new() { Limit = 10 }, expectedRevision: null, cancellationToken: Ct).Rows.Select(row => row.Key));
            Assert.Equal(targetRows, session.QueryCollection(target, new() { Limit = 10 }, expectedRevision: null, cancellationToken: Ct).Rows.Select(row => row.Key));
            Assert.False(session.State.CanUndo);
            Assert.False(session.State.IsDirty);
            Assert.True(session.Redo().Ok);
            SelectionByteAssertions.Equal(observer.Current, changed);
            AssertExportedBytes(session, changed);
            Assert.Equal(moved, session.QueryCollection(target, new() { Limit = 10 }, expectedRevision: null, cancellationToken: Ct).Rows.Select(row => row.Key));
            Assert.True(session.Undo().Ok);
            SelectionByteAssertions.Equal(observer.Current, originals);
            Assert.Empty(session.Serialize(cancellationToken: Ct));
            Assert.True(session.ApplyActionPlan(preview.PlanId!, preview.Revision, cancellationToken: Ct).Stale);
            if (querySelection)
                Assert.True(session.PlanAction(action.ActionId, Empty, scope, session.State.Revision, cancellationToken: Ct).Stale);
            SelectionByteAssertions.Equal(observer.Current, originals);
            Assert.Empty(session.Serialize(cancellationToken: Ct));
        }
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void BoundTransfer_RejectsWrongSourcesAndMissingSelectionWhileEmptyScopesPreserveHistory(bool wonderlands, string actionId)
    {
        var observer = new SelectionGroupObserver();
        using var workspace = SelectionTestData.Workspace(wonderlands, observer);
        var session = Open(workspace);
        var root = Assert.IsType<SectionNode>(session.BuildView());
        var action = Assert.Single(root.Children.OfType<ActionNode>(), action => action.ActionId == actionId);
        var source = action.SelectionTableId!;
        var rows = session.QueryCollection(source, new() { Limit = 10 }, expectedRevision: null, cancellationToken: Ct).Rows.Select(row => row.Key).ToArray();
        var before = workspace.Set.AllDiskFiles.ToDictionary(file => file.RelativePath, file => file.ReadAllBytes());
        Assert.Equal(2, before.Count);
        SelectionByteAssertions.Equal(observer.Current, before);
        Assert.Empty(session.Serialize(cancellationToken: Ct));
        var revision = session.State.Revision;
        Assert.NotEmpty(session.PlanAction(actionId, Empty, scope: null, expectedRevision: null, cancellationToken: Ct).Plan.Errors);
        Assert.False(session.InvokeAction(actionId, Empty, expectedRevision: null, cancellationToken: Ct).Ok);
        foreach (var foreign in root.Children.OfType<ActionNode>().Where(node => node.ActionId != actionId))
        {
            var row = session.QueryCollection(foreign.SelectionTableId!, new() { Limit = 1 }, expectedRevision: null, cancellationToken: Ct).Rows[0].Key;
            Assert.NotEmpty(session.PlanAction(actionId, Empty, new([row]), expectedRevision: null, cancellationToken: Ct).Plan.Errors);
            Assert.NotEmpty(session.PlanAction(actionId, Empty, new() { QueryScope = new(foreign.SelectionTableId!, new(), revision) }, expectedRevision: null, cancellationToken: Ct).Plan.Errors);
        }
        Assert.NotEmpty(session.PlanAction(actionId, Empty, new([rows[0], rows[0]]), expectedRevision: null, cancellationToken: Ct).Plan.Errors);
        SelectionByteAssertions.Equal(observer.Current, before);
        Assert.Empty(session.Serialize(cancellationToken: Ct));
        foreach (var scope in new ActionScope[]
        {
            new([]),
            new() { QueryScope = new(source, new(), revision) { ExcludedRowKeys = rows } },
            new() { QueryScope = new(source, new() { Text = "no-such-synthetic-item" }, revision) },
        })
        {
            var preview = session.PlanAction(actionId, Empty, scope, expectedRevision: null, cancellationToken: Ct);
            Assert.Empty(preview.Plan.Errors);
            Assert.Empty(preview.Scope.RowKeys!);
            Assert.Empty(preview.Plan.Changes);
            SelectionByteAssertions.Equal(observer.Current, before);
            Assert.Empty(session.Serialize(cancellationToken: Ct));
            Assert.True(session.ApplyActionPlan(preview.PlanId!, preview.Revision, cancellationToken: Ct).Ok);
            Assert.Equal(revision, session.State.Revision);
            Assert.False(session.State.CanUndo);
            Assert.False(session.State.IsDirty);
            SelectionByteAssertions.Equal(observer.Current, before);
            Assert.Empty(session.Serialize(cancellationToken: Ct));
        }
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void BoundTransfer_QueryBudgetAppliesBeforeExclusions(bool wonderlands, string actionId)
    {
        using var workspace = SelectionTestData.Workspace(wonderlands);
        var session = Open(workspace, new() { MaxQueryActionRows = SelectionTestData.ItemCount - 1 });
        var action = Assert.Single(Assert.IsType<SectionNode>(session.BuildView()).Children.OfType<ActionNode>(), action => action.ActionId == actionId);
        var source = action.SelectionTableId!;
        var rows = session.QueryCollection(source, new() { Limit = 10 }, expectedRevision: null, cancellationToken: Ct).Rows.Select(row => row.Key).ToArray();
        var preview = session.PlanAction(actionId, Empty, new() { QueryScope = new(source, new(), session.State.Revision) { ExcludedRowKeys = rows } }, expectedRevision: null, cancellationToken: Ct);
        Assert.NotEmpty(preview.Plan.Errors);
        Assert.Null(preview.PlanId);
        Assert.False(session.State.IsDirty);
        Assert.False(session.State.CanUndo);
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void BoundTransfer_RevalidatesReviewedEffectsAndCapacityWithoutPartiallyMovingRows(bool wonderlands, string actionId)
    {
        using var group = SelectionTestData.Group(wonderlands);
        var action = Assert.Single(Assert.IsType<SectionNode>(group.BuildView()).Children.OfType<ActionNode>(), node => node.ActionId == actionId);
        var source = action.SelectionTableId!;
        var scope = new ActionScope([group.Query(source, new() { Limit = 1 }).Rows[0].Key]);
        var parameters = new Dictionary<string, SaveToolkit.Sdk.Values.FieldValue>();
        var plan = group.PlanAction(action.ActionId, parameters, scope);
        Assert.Empty(plan.Errors);
        Assert.NotEmpty(plan.Changes);
        var before = SelectionTestData.Bytes(group);
        Assert.False(group.ApplyActionPlan(action.ActionId, parameters, scope, plan with { Changes = [] }).Succeeded);
        SelectionTestData.AssertBytes(group, before);
        var target = actionId == "transfer/deposit" ? group.Profile : group.Character;
        // Unknown existing capacity metadata must reject growth, including at apply-time revalidation.
        GearboxTestData.AddMessage(target.Root, target.Profile ? "profile_sdu_list" : "sdu_list").SetText("sdu_data_path", "unknown");
        before = SelectionTestData.Bytes(group);
        Assert.False(group.ApplyActionPlan(action.ActionId, parameters, scope, plan).Succeeded);
        SelectionTestData.AssertBytes(group, before);
    }

    private static DocumentSession Open(Workspace workspace, DocumentSessionOptions? options = null)
    {
        var file = workspace.Set.AllDiskFiles.Single(file => file.FileName == "1");
        return workspace.OpenGroup(Assert.Single(workspace.ProposeGroups(file.Id, "gearbox")).Token, options: options).Session;
    }

    private static void AssertExportedBytes(DocumentSession session, IReadOnlyDictionary<string, byte[]> expected)
    {
        var delta = session.Serialize(cancellationToken: Ct).ToDictionary(output => output.RelativePath, output => output.Content.ToArray());
        SelectionByteAssertions.Equal(expected, delta);
    }
}
