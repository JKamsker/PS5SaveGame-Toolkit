using System.Text.Json;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Core.Storage;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.SuckerPunch.Tests;

public sealed class QuantitySelectionSessionTests
{
    private const int YoteiQuantityOffset = sizeof(uint) + sizeof(byte);
    [Theory]
    [InlineData(false, "inventory/set-goods-quantity")]
    [InlineData(false, "inventory/set-quantity")]
    [InlineData(true, "yotei/inventory/set-quantity")]
    public void BoundControlPreviewApplyExportAndOneStepHistoryLeaveUnselectedRowsIntact(bool yotei, string actionId)
    {
        var file = Assert.IsType<DiskSaveFile>(yotei ? YoteiTestData.Slot("suckerpunchUser4") : SuckerPunchTestData.Slot("suckerpunchUser1"));
        var plugin = new SuckerPunchPlugin();
        var document = plugin.Open(file);
        using var session = new DocumentSession("quantity-selection", file, plugin, document);
        var tab = QuantitySelectionBindingTests.InventoryTab(session.BuildView());
        var table = Assert.Single(tab.Children.OfType<TableNode>());
        var action = Assert.Single(tab.Children.OfType<ActionNode>(), node => node.ActionId == actionId);
        Assert.Equal(table.SelectionId, action.SelectionTableId);
        var prefix = yotei ? "yotei/inventory/" : "inventory/";
        var resource = yotei ? "Mon" : "Supplies";
        var resourceId = prefix + resource + "/quantity";
        var arrowId = prefix + "ArrowNormal/quantity";
        var flowersId = prefix + "Flowers/quantity";
        var goodsOnly = actionId == "inventory/set-goods-quantity";
        var cancellation = TestContext.Current.CancellationToken;
        var zero = JsonSerializer.SerializeToElement("0");
        Assert.True(session.ApplyEdits([new(resourceId, zero), new(arrowId, zero), new(flowersId, zero)],
            expectedRevision: null, cancellationToken: cancellation).Ok);
        var staged = Bytes(document);
        var parameters = new Dictionary<string, JsonElement> { ["quantity"] = JsonSerializer.SerializeToElement("1") };
        var scope = new ActionScope([resource, "ArrowNormal", "TestItem"]);
        var preview = session.PlanAction(action.ActionId, parameters, scope, expectedRevision: null, cancellationToken: cancellation);
        Assert.NotNull(preview.PlanId);
        Assert.Empty(preview.Plan.Errors);
        Assert.Equal(scope.RowKeys, preview.Scope.RowKeys);
        Assert.Equal(staged, Bytes(document));
        var primaryIds = goodsOnly ? new[] { resourceId } : new[] { resourceId, arrowId };
        Assert.Equal(primaryIds.Order(StringComparer.Ordinal), preview.Plan.Changes.Where(change => !change.Derived)
            .Select(change => change.ValueId).Order(StringComparer.Ordinal));
        Assert.Contains(preview.Plan.SkippedRows, row => row.RowKey == "TestItem");
        if (goodsOnly) Assert.Contains(preview.Plan.SkippedRows, row => row.RowKey == "ArrowNormal");
        var applied = session.ApplyActionPlan(preview.PlanId!, preview.Revision, cancellationToken: cancellation);
        Assert.True(applied.Ok);
        Assert.Equal(preview.Plan.Warnings, applied.Warnings);
        var edited = Bytes(document);
        Assert.Equal(1U, document.GetValue(resourceId).AsUInt32());
        Assert.Equal(goodsOnly ? 0U : 1U, document.GetValue(arrowId).AsUInt32());
        Assert.Equal(document.GetValue(arrowId), document.GetValue(arrowId + "-copy"));
        Assert.Equal(0U, document.GetValue(flowersId).AsUInt32());
        var allowed = new List<(int Start, int Length)> { (yotei ? 12 : 8, sizeof(uint)), (Offset(resource), sizeof(uint)) };
        if (!goodsOnly) allowed.Add((Offset("ArrowNormal"), 2 * sizeof(uint)));
        WriteAssertions.OnlyRangesChanged(staged, edited, allowed);
        Assert.Equal(yotei ? YoteiTestData.Checksum(edited) : SuckerPunchTestData.Checksum(edited),
            SuckerPunchTestData.Word(edited, yotei ? 12 : 8));
        using (var reopened = SuckerPunchTestData.Open(SuckerPunchTestData.WithBytes(edited, file.Directory.TitleId)))
            Assert.All(preview.Plan.Changes, change => Assert.Equal(change.NewValue, reopened.GetValue(change.ValueId)));

        // An explicit empty request is distinct from the browser's clear-selection document fallback.
        var noOp = session.PlanAction(action.ActionId, parameters, new([]), expectedRevision: null, cancellationToken: cancellation);
        Assert.NotNull(noOp.PlanId);
        Assert.Empty(noOp.Plan.Changes);
        Assert.True(session.ApplyActionPlan(noOp.PlanId!, noOp.Revision, cancellationToken: cancellation).Ok);
        Assert.Equal(edited, Bytes(document));
        Assert.True(session.Undo().Ok);
        Assert.Equal(staged, Bytes(document));
        Assert.True(session.Redo().Ok);
        Assert.Equal(edited, Bytes(document));
        Assert.True(session.ApplyActionPlan(preview.PlanId!, preview.Revision, cancellationToken: cancellation).Stale);

        var stale = session.PlanAction(action.ActionId, parameters, scope, expectedRevision: null, cancellationToken: cancellation);
        Assert.NotNull(stale.PlanId);
        Assert.True(session.ApplyEdits([new(resourceId, zero)], expectedRevision: null, cancellationToken: cancellation).Ok);
        var beforeReject = Bytes(document);
        Assert.True(session.ApplyActionPlan(stale.PlanId!, stale.Revision, cancellationToken: cancellation).Stale);
        Assert.Equal(beforeReject, Bytes(document));

        int Offset(string name) => yotei ? YoteiTestData.InventoryRecords(staged).Single(row => row.Name == name).Data + YoteiQuantityOffset
            : WriteAssertions.CountOffset(staged, name);
    }

    private static byte[] Bytes(ISaveDocument document) => SuckerPunchTestData.PrimaryOutput(document).Content.ToArray();
}
