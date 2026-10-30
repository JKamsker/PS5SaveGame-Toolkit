using SaveToolkit.Core.Editing;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;
using SaveToolkit.Testing;
using static SaveToolkit.Plugins.SuckerPunch.Tests.QuantityPlanningTestData;

namespace SaveToolkit.Plugins.SuckerPunch.Tests;

public sealed class QuantitySelectionBindingTests
{
    private const string GoodsAction = "inventory/set-goods-quantity";
    private const string RestoreAction = "inventory/restore-imported-ammo";

    [Theory]
    [InlineData(false, 47U)]
    [InlineData(false, 48U)]
    [InlineData(false, 49U)]
    [InlineData(true, 0U)]
    public void VariantPublishesStableUnambiguousInventoryBindingAndPreservesRestorePlacement(bool yotei, uint version)
    {
        var save = new SyntheticSave(yotei);
        if (!yotei) { save.Write(12, version); save.Repair(); }
        using var document = save.Open();
        var expectedId = yotei ? "yotei/inventory" : "tsushima/inventory";
        for (var rebuild = 0; rebuild < 2; rebuild++)
        {
            var view = document.BuildView();
            var tab = InventoryTab(view);
            var table = Assert.Single(tab.Children.OfType<TableNode>());
            Assert.Equal(expectedId, table.SelectionId);
            Assert.False(string.IsNullOrWhiteSpace(table.Title));
            var actions = tab.Children.OfType<ActionNode>().Where(action => action.ActionId != RestoreAction).ToArray();
            Assert.Equal(yotei ? new[] { save.ActionId } : new[] { GoodsAction, save.ActionId }, actions.Select(action => action.ActionId));
            Assert.All(actions, action =>
            {
                Assert.True(action.SupportsPlanning);
                Assert.Equal(expectedId, action.SelectionTableId);
                Assert.Contains("With nothing selected", action.Description);
                Assert.Contains("not validated on console", action.Description!, StringComparison.OrdinalIgnoreCase);
            });
            var index = new ViewIndex();
            index.Add(view);
            Assert.Empty(index.SelectionDiagnostics);
            Assert.DoesNotContain(((IQueryableDocument)document).Collections, collection => collection.Id == expectedId);
            if (yotei) continue;
            Assert.Null(Assert.Single(tab.Children.OfType<ActionNode>(), action => action.ActionId == RestoreAction).SelectionTableId);
            Assert.All(table.Rows, row => Assert.Null(Assert.Single(row.Cells.Values.OfType<ActionNode>(),
                action => action.ActionId == RestoreAction).SelectionTableId));
        }
        Assert.Equal(save.Bytes, SuckerPunchTestData.PrimaryOutput(document).Content.ToArray());
    }

    [Theory]
    [InlineData(false, GoodsAction)]
    [InlineData(false, "inventory/set-quantity")]
    [InlineData(true, "yotei/inventory/set-quantity")]
    public void AdvisoryFixtureProvesOnlyDeclaredSelectedRowEffectsAndPureRepeatPlans(bool yotei, string actionId)
    {
        var save = new SyntheticSave(yotei);
        var goodsOnly = actionId == GoodsAction;
        var fixture = new SelectionScopeCase(actionId, yotei ? "yotei/inventory" : "tsushima/inventory",
            [save.ResourceName, "ArrowNormal"])
        {
            Parameters = Target(1),
            Stage = document =>
            {
                if (!document.ApplyEdits([new(save.ResourceId, FieldValue.Of(0L)), new(save.ArrowId, FieldValue.Of(0L)),
                    new(save.Prefix + "Flowers/quantity", FieldValue.Of(0L))]).Succeeded)
                    throw new InvalidOperationException("Selection fixture staging was rejected.");
            },
            // Ownership comes from the synthetic named-record layout, not rendered cell placement.
            RowOwnedValueIds = save.Inventory.Keys.ToDictionary(name => name,
                name => (IReadOnlyList<string>)(name is "ArrowNormal" or "ArrowFire"
                    ? new[] { save.Prefix + name + "/quantity", save.Prefix + name + "/quantity-copy" }
                    : new[] { save.Prefix + name + "/quantity" }), StringComparer.Ordinal),
            ExpectedChangedValueIds = goodsOnly ? [save.ResourceId] : [save.ResourceId, save.ArrowId, save.ArrowId + "-copy"],
            PermittedDerivedValueIds = [$"container/word{save.ChecksumOffset / sizeof(uint)}"],
        };
        var result = SelectionScopeFixture.Evaluate(save.Open, fixture, new() { MaxQueryPages = 32 });
        Assert.Equal(SelectionScopeStatus.Passed, result.Status);
    }

    [Theory]
    [InlineData(false, GoodsAction)]
    [InlineData(false, "inventory/set-quantity")]
    [InlineData(true, "yotei/inventory/set-quantity")]
    public void EmptyExplicitSelectionIsNoOpDocumentScopeRemainsBroadAndQueryScopesReject(bool yotei, string actionId)
    {
        var save = new SyntheticSave(yotei);
        using var document = save.Open();
        var planner = (IActionPlanningDocument)document;
        var emptyScope = new ActionScope([]);
        var empty = planner.PlanAction(actionId, Target(1), emptyScope);
        Assert.Empty(empty.Errors);
        Assert.Empty(empty.Changes);
        Assert.True(planner.ApplyActionPlan(actionId, Target(1), emptyScope, empty).Succeeded);
        var whole = planner.PlanAction(actionId, Target(1), new());
        Assert.Empty(whole.Errors);
        Assert.Contains(whole.Changes, change => change.ValueId == save.ResourceId);
        Assert.Contains(whole.Changes, change => change.ValueId == save.Prefix + "Flowers/quantity");
        Assert.Equal(actionId != GoodsAction, whole.Changes.Any(change => change.ValueId == save.ArrowId));

        // Eager inventory tables publish selection IDs, not query collections. All/excluded query
        // requests must reject even if a caller supplies resolved, otherwise valid row keys.
        foreach (var id in new[] { yotei ? "yotei/inventory" : "tsushima/inventory",
            yotei ? "yotei/gear-registry" : "equipment/identifiers", "foreign/collection" })
        foreach (var excluded in new IReadOnlyList<string>[] { [], [save.ResourceName], [save.ResourceName, "ArrowNormal"] })
        {
            var scope = new ActionScope(new[] { save.ResourceName, "ArrowNormal" }.Except(excluded, StringComparer.Ordinal).ToArray())
            {
                QueryScope = new ActionQueryScope(id, new QueryableCollectionQuery(), 0) { ExcludedRowKeys = excluded },
            };
            var rejected = planner.PlanAction(actionId, Target(1), scope);
            Assert.NotEmpty(rejected.Errors);
            Assert.False(planner.ApplyActionPlan(actionId, Target(1), scope, rejected).Succeeded);
        }
        foreach (var key in new[] { "unknown", save.ResourceName.ToUpperInvariant() })
            Assert.NotEmpty(planner.PlanAction(actionId, Target(1), new([key])).Errors);
        Assert.Equal(save.Bytes, SuckerPunchTestData.PrimaryOutput(document).Content.ToArray());
    }

    internal static Tab InventoryTab(ViewNode view) =>
        Assert.IsType<TabsNode>(view).Tabs.Single(tab => tab.Title == "Inventory");
}
