using System.Text.Json;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.Gearbox.Tests;

public sealed class ItemSelectionWiringTests
{
    private static readonly Dictionary<string, JsonElement> Empty = [];
    private static readonly bool[] GameVariants = [false, true];
    private static readonly string[] ActionIds = ["items/duplicate", "items/remove"];
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public static IEnumerable<object[]> Cases =>
        from wonderlands in GameVariants
        from list in new[] { GearboxInventory.Backpack, GearboxInventory.Bank, GearboxInventory.LostLoot }
        from action in ActionIds
        select new object[] { wonderlands, list, action };

    [Theory]
    [MemberData(nameof(Cases))]
    public void DeclaredRowAction_ExplicitAndQueryScopesPreserveUnselectedRecordsAndHistory(bool wonderlands, string list, string actionId)
    {
        using var workspace = SelectionTestData.Workspace(wonderlands);
        var file = workspace.Set.AllDiskFiles.Single(file => file.FileName == (list == GearboxInventory.Backpack ? "1" : "profile"));
        var plugin = new GearboxPlugin();
        using var document = (GearboxDocument)plugin.Open(file);
        using var session = new DocumentSession("item-selection", file, plugin, document);
        _ = session.BuildView();
        var page = session.QueryCollection(list, new() { Limit = 1 }, expectedRevision: null, cancellationToken: Ct);
        Assert.Single(page.Rows); // Other selected rows are not loaded by this client page.
        Assert.NotEmpty(session.PlanAction(actionId, Empty, new([page.Rows[0].Key]), expectedRevision: null, cancellationToken: Ct).Plan.Errors);
        var details = session.Expand(Assert.IsType<SectionNode>(page.Rows[0].Cells["detail"]).LazyId!);
        var action = Assert.Single(details.OfType<ActionNode>(), node => node.ActionId == actionId);
        var original = SelectionByteAssertions.Capture(document, [file.RelativePath]);
        Assert.Equal(file.ReadAllBytes(), original[file.RelativePath]);
        Assert.Empty(session.Serialize(cancellationToken: Ct));
        var originalKeys = session.QueryCollection(list, new() { Limit = 10 }, expectedRevision: null, cancellationToken: Ct).Rows.Select(row => row.Key).ToArray();

        foreach (var querySelection in new[] { false, true })
        {
            var current = session.QueryCollection(list, new() { Limit = 1 }, expectedRevision: null, cancellationToken: Ct);
            _ = session.Expand(Assert.IsType<SectionNode>(current.Rows[0].Cells["detail"]).LazyId!);
            var selected = querySelection ? new[] { originalKeys[2], originalKeys[0] } : [originalKeys[0]];
            var scope = querySelection ? new ActionScope
            {
                QueryScope = new(list, new() { Offset = 1, Limit = 1, SortColumn = "index", Descending = true }, session.State.Revision)
                { ExcludedRowKeys = [originalKeys[1]] },
            } : new(selected);
            var preview = session.PlanAction(action.ActionId, Empty, scope, expectedRevision: null, cancellationToken: Ct);
            Assert.Empty(preview.Plan.Errors);
            Assert.Equal(selected, preview.Scope.RowKeys);
            Assert.Equal(selected.Order(StringComparer.Ordinal), preview.Plan.Changes.Where(change => change.ValueId.EndsWith("/presence", StringComparison.Ordinal))
                .Select(change => change.ValueId[..^"/presence".Length]).Order(StringComparer.Ordinal));
            SelectionByteAssertions.Equal(document, original);
            Assert.Empty(session.Serialize(cancellationToken: Ct));
            Assert.False(session.State.CanUndo);
            Assert.True(session.ApplyActionPlan(preview.PlanId!, preview.Revision, cancellationToken: Ct).Ok);
            var changed = SelectionByteAssertions.Capture(document, [file.RelativePath]);
            var exported = Assert.Single(session.Serialize(cancellationToken: Ct));
            Assert.Equal(file.RelativePath, exported.RelativePath);
            Assert.Equal(changed[file.RelativePath], exported.Content.ToArray());
            var changedKeys = session.QueryCollection(list, new() { Limit = 10 }, expectedRevision: null, cancellationToken: Ct).Rows.Select(row => row.Key).ToArray();
            Assert.Equal(originalKeys.Length + (actionId == "items/duplicate" ? selected.Length : -selected.Length), changedKeys.Length);
            Assert.Contains(originalKeys[1], changedKeys);
            if (actionId == "items/remove") Assert.DoesNotContain(changedKeys, selected.Contains);
            else Assert.Equal(originalKeys, changedKeys.Take(originalKeys.Length));
            using var reopened = (GearboxDocument)plugin.Open(GearboxTestData.Overlay(file, changed[file.RelativePath]));
            using var baseline = (GearboxDocument)new GearboxPlugin().Open(file);
            Assert.Equal(baseline.Inventory.Find(originalKeys[1]).Field.Encode(), reopened.Inventory.Entries
                .Where(entry => entry.List == list).ElementAt(actionId == "items/remove" ? 0 : 1).Field.Encode());
            SerialWriteTests.AssertUnrelated(baseline.Root, reopened.Root, list, "equipped_inventory_list");
            if (list == GearboxInventory.Backpack)
                Assert.Equal(actionId == "items/duplicate" ? new long[] { 0, 2 } : querySelection ? [-1, -1] : [-1, 1],
                    reopened.Root.Children("equipped_inventory_list").Select(slot => slot.Integer("inventory_list_index")));
            Assert.False(session.ApplyActionPlan(preview.PlanId!, preview.Revision, cancellationToken: Ct).Ok);
            SelectionByteAssertions.Equal(document, changed);
            Assert.True(session.Undo().Ok);
            SelectionByteAssertions.Equal(document, original);
            Assert.Empty(session.Serialize(cancellationToken: Ct));
            Assert.Equal(originalKeys, session.QueryCollection(list, new() { Limit = 10 }, expectedRevision: null, cancellationToken: Ct).Rows.Select(row => row.Key));
            Assert.False(session.State.CanUndo);
            Assert.False(session.State.IsDirty);
            Assert.True(session.Redo().Ok);
            SelectionByteAssertions.Equal(document, changed);
            exported = Assert.Single(session.Serialize(cancellationToken: Ct));
            Assert.Equal(file.RelativePath, exported.RelativePath);
            Assert.Equal(changed[file.RelativePath], exported.Content.ToArray());
            Assert.Equal(changedKeys, session.QueryCollection(list, new() { Limit = 10 }, expectedRevision: null, cancellationToken: Ct).Rows.Select(row => row.Key));
            Assert.True(session.Undo().Ok);
            SelectionByteAssertions.Equal(document, original);
            Assert.Empty(session.Serialize(cancellationToken: Ct));
        }
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void DeclaredRowAction_RejectsMissingScopeAndKeepsEmptySelectionsAsNoOps(bool wonderlands, string list, string actionId)
    {
        using var workspace = SelectionTestData.Workspace(wonderlands);
        var file = workspace.Set.AllDiskFiles.Single(file => file.FileName == (list == GearboxInventory.Backpack ? "1" : "profile"));
        var plugin = new GearboxPlugin();
        using var document = (GearboxDocument)plugin.Open(file);
        using var session = new DocumentSession("empty-item-selection", file, plugin, document);
        _ = session.BuildView();
        var page = session.QueryCollection(list, new() { Limit = 10 }, expectedRevision: null, cancellationToken: Ct);
        _ = session.Expand(Assert.IsType<SectionNode>(page.Rows[0].Cells["detail"]).LazyId!);
        var before = SelectionByteAssertions.Capture(document, [file.RelativePath]);
        Assert.Equal(file.ReadAllBytes(), before[file.RelativePath]);
        Assert.Empty(session.Serialize(cancellationToken: Ct));
        var revision = session.State.Revision;
        Assert.NotEmpty(session.PlanAction(actionId, Empty, scope: null, expectedRevision: null, cancellationToken: Ct).Plan.Errors);
        Assert.False(session.InvokeAction(actionId, Empty, expectedRevision: null, cancellationToken: Ct).Ok); // Planning cannot be bypassed.
        Assert.NotEmpty(session.PlanAction(actionId, Empty, new(["foreign"]), expectedRevision: null, cancellationToken: Ct).Plan.Errors);
        Assert.NotEmpty(session.PlanAction(actionId, Empty, new([page.Rows[0].Key, page.Rows[0].Key]), expectedRevision: null, cancellationToken: Ct).Plan.Errors);
        SelectionByteAssertions.Equal(document, before);
        Assert.Empty(session.Serialize(cancellationToken: Ct));
        foreach (var scope in new ActionScope[]
        {
            new([]),
            new() { QueryScope = new(list, new(), revision) { ExcludedRowKeys = page.Rows.Select(row => row.Key).ToArray() } },
            new() { QueryScope = new(list, new() { Text = "no-such-synthetic-item" }, revision) },
        })
        {
            var preview = session.PlanAction(actionId, Empty, scope, expectedRevision: null, cancellationToken: Ct);
            Assert.Empty(preview.Plan.Errors);
            Assert.Empty(preview.Scope.RowKeys!);
            Assert.Empty(preview.Plan.Changes);
            SelectionByteAssertions.Equal(document, before);
            Assert.Empty(session.Serialize(cancellationToken: Ct));
            Assert.True(session.ApplyActionPlan(preview.PlanId!, preview.Revision, cancellationToken: Ct).Ok);
            Assert.Equal(revision, session.State.Revision);
            Assert.False(session.State.IsDirty);
            Assert.False(session.State.CanUndo);
            SelectionByteAssertions.Equal(document, before);
            Assert.Empty(session.Serialize(cancellationToken: Ct));
        }
    }
}
