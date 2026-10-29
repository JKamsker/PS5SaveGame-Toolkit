using System.Text.Json;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Core.Storage;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;
using static SaveToolkit.Plugins.SuckerPunch.Tests.SuckerPunchTestData;
using static SaveToolkit.Plugins.SuckerPunch.Tests.TsushimaAmmoRestoreTests;

namespace SaveToolkit.Plugins.SuckerPunch.Tests;

public sealed class TsushimaAmmoRestoreSelectionTests
{
    private const string NormalRow = "ArrowNormal";
    private const string FireRow = "ArrowFire";
    private const string FireId = "inventory/ArrowFire/quantity";
    private static readonly IReadOnlyDictionary<string, FieldValue> NoParameters = new Dictionary<string, FieldValue>();

    [Fact]
    public void RenderedRestoreWithNormalArrowSelectedLeavesReducedFireArrowsUnchanged()
    {
        var save = new SyntheticSave(yotei: false);
        using var document = save.Open();
        ReduceBoth(document);
        var staged = Bytes(document);
        var tab = InventoryTab(document);
        var table = Assert.Single(tab.Children.OfType<TableNode>());
        var selected = table.Rows.Single(row => row.Key == NormalRow);
        var cellAction = selected.Cells.Values.OfType<ActionNode>().SingleOrDefault(action => action.ActionId == ActionId);
        // The browser only supplies table selection to controls rendered inside that table.
        // Before the fix, the sole rendered restore control is a document-scoped sibling.
        var action = cellAction ?? tab.Children.OfType<ActionNode>().Single(node => node.ActionId == ActionId);
        var scope = cellAction is null ? new ActionScope() : new ActionScope([selected.Key]);
        var planner = (IActionPlanningDocument)document;
        var plan = planner.PlanAction(action.ActionId, NoParameters, scope);
        Assert.Empty(plan.Errors);
        Assert.Equal(staged, Bytes(document));
        Assert.True(planner.ApplyActionPlan(action.ActionId, NoParameters, scope, plan).Succeeded);
        Assert.Equal(SyntheticSave.InitialQuantity, document.GetValue(ArrowId).AsUInt32());
        Assert.Equal(0U, document.GetValue(FireId).AsUInt32());
        Assert.Equal(0U, document.GetValue(FireId + "-copy").AsUInt32());
        Assert.NotNull(cellAction);
        Assert.Contains("selected", cellAction.Label);
        Assert.Equal(ArrowId, Assert.Single(plan.Changes, change => !change.Derived).ValueId);
        WriteAssertions.OnlyRangesChanged(staged, Bytes(document),
            [(save.ChecksumOffset, sizeof(uint)), (save.Inventory[NormalRow] + sizeof(uint), 2 * sizeof(uint))]);
        Assert.Equal(Checksum(Bytes(document)), Word(Bytes(document), save.ChecksumOffset));
        using var reopened = Open(WithBytes(Bytes(document)));
        Assert.Equal(0U, reopened.GetValue(FireId).AsUInt32());
    }

    [Fact]
    public void StandaloneRestoreWithoutTableSelectionRetainsDocumentScope()
    {
        var save = new SyntheticSave(yotei: false);
        using var document = save.Open();
        ReduceBoth(document);
        var action = InventoryTab(document).Children.OfType<ActionNode>().Single(node => node.ActionId == ActionId);
        Assert.Contains("whole document", action.Label);
        var planner = (IActionPlanningDocument)document;
        var plan = planner.PlanAction(action.ActionId, NoParameters, new());
        Assert.Empty(plan.Errors);
        Assert.True(planner.ApplyActionPlan(action.ActionId, NoParameters, new(), plan).Succeeded);
        Assert.Equal(SyntheticSave.InitialQuantity, document.GetValue(ArrowId).AsUInt32());
        Assert.Equal(SyntheticSave.InitialQuantity, document.GetValue(FireId).AsUInt32());
        Assert.Equal(save.Bytes, Bytes(document));
    }

    [Fact]
    public void ExplicitEmptyTableSelectionDoesNotRestoreAnyAmmo()
    {
        var save = new SyntheticSave(yotei: false);
        using var document = save.Open();
        ReduceBoth(document);
        var staged = Bytes(document);
        var table = Assert.Single(InventoryTab(document).Children.OfType<TableNode>());
        Assert.All(table.Rows, row => Assert.True(Assert.Single(row.Cells.Values.OfType<ActionNode>(),
            action => action.ActionId == ActionId).SupportsPlanning));
        var action = Assert.Single(table.Rows.Single(row => row.Key == FireRow).Cells.Values.OfType<ActionNode>(),
            node => node.ActionId == ActionId);
        var planner = (IActionPlanningDocument)document;
        var scope = new ActionScope([]);
        var plan = planner.PlanAction(action.ActionId, NoParameters, scope);
        Assert.Empty(plan.Errors);
        Assert.Empty(plan.Changes);
        Assert.True(planner.ApplyActionPlan(action.ActionId, NoParameters, scope, plan).Succeeded);
        Assert.Equal(staged, Bytes(document));
    }

    [Fact]
    public void SampleSelectionPreservesFireArrowsAcrossExportUndoAndRedo()
    {
        var file = Assert.IsType<DiskSaveFile>(Slot("suckerpunchUser8"));
        var plugin = new SuckerPunchPlugin();
        var document = plugin.Open(file);
        using var session = new DocumentSession("restore-selected-ammo", file, plugin, document);
        session.BuildView();
        var normalOpening = document.GetValue(ArrowId);
        Assert.True(normalOpening.AsUInt32() > 0);
        Assert.True(document.GetValue(FireId).AsUInt32() > 0);
        Assert.True(session.ApplyEdits([new IncomingEdit(ArrowId, JsonSerializer.SerializeToElement("0")),
            new IncomingEdit(FireId, JsonSerializer.SerializeToElement("0"))], expectedRevision: null, cancellationToken: TestContext.Current.CancellationToken).Ok);
        var staged = Bytes(document);
        var action = Assert.Single(Inventory(document).Rows.Single(row => row.Key == NormalRow).Cells.Values
            .OfType<ActionNode>(), node => node.ActionId == ActionId);
        var preview = session.PlanAction(action.ActionId, new Dictionary<string, JsonElement>(), new([NormalRow]), expectedRevision: null, cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotNull(preview.PlanId);
        Assert.Empty(preview.Plan.Errors);
        Assert.Equal(ArrowId, Assert.Single(preview.Plan.Changes, change => !change.Derived).ValueId);
        Assert.Equal(staged, Bytes(document));
        Assert.True(session.ApplyActionPlan(preview.PlanId!, preview.Revision, cancellationToken: TestContext.Current.CancellationToken).Ok);
        Assert.Equal(normalOpening, document.GetValue(ArrowId));
        Assert.Equal(normalOpening, document.GetValue(ArrowId + "-copy"));
        Assert.Equal(0U, document.GetValue(FireId).AsUInt32());
        Assert.Equal(0U, document.GetValue(FireId + "-copy").AsUInt32());
        var restored = Bytes(document);
        WriteAssertions.OnlyRangesChanged(staged, restored,
            [(8, sizeof(uint)), (WriteAssertions.CountOffset(staged, NormalRow), 2 * sizeof(uint))]);
        Assert.Equal(Checksum(restored), Word(restored, 8));
        using var reopened = Open(WithBytes(restored));
        Assert.Equal(normalOpening, reopened.GetValue(ArrowId));
        Assert.Equal(0U, reopened.GetValue(FireId).AsUInt32());
        Assert.True(session.Undo().Ok);
        Assert.Equal(staged, Bytes(document));
        Assert.True(session.Redo().Ok);
        Assert.Equal(restored, Bytes(document));
    }

    private static Tab InventoryTab(ISaveDocument document) =>
        Assert.IsType<TabsNode>(document.BuildView()).Tabs.Single(tab => tab.Title == "Inventory");

    private static void ReduceBoth(ISaveDocument document) =>
        Assert.True(document.ApplyEdits([new(ArrowId, FieldValue.Of(0L)), new(FireId, FieldValue.Of(0L))]).Succeeded);

    private static byte[] Bytes(ISaveDocument document) => Assert.Single(document.Serialize()).Content.ToArray();
}
