using System.Text.Json;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.EldenRing.Tests;

public sealed class RefillPlanningTests
{
    private const string ActionId = "slots/0/inventory/refill";
    private static readonly Dictionary<string, JsonElement> NoParameters = [];

    [Fact]
    public void SampleBulkRefill_PreviewsSkipsAndChangesThenRestoresBothRowsInOneUndo()
    {
        var file = WriteTestSupport.Sample();
        var original = file.ReadAllBytes();
        using var session = WriteTestSupport.Session(file, out var document);
        var fruit = WriteTestSupport.Quantity(session, "held", "0x400050F0");
        var stone = WriteTestSupport.Quantity(session, "held", "0x40002774");
        Assert.True(session.ApplyEdits([WriteTestSupport.Incoming(fruit.ValueId, 10), WriteTestSupport.Incoming(stone.ValueId, 1)], expectedRevision: null, cancellationToken: TestContext.Current.CancellationToken).Ok);
        var reduced = WriteTestSupport.Bytes(document);
        // Action is exposed in table cells, so ordinary UI row/whole-filtered selections reach F6.
        var tableAction = Assert.IsType<ActionNode>(session.QueryCollection("slots/0/inventory/held", new() { Limit = 1 }, expectedRevision: null, cancellationToken: TestContext.Current.CancellationToken).Rows[0].Cells["refill"]);
        Assert.Equal(ActionId, tableAction.ActionId);
        Assert.True(tableAction.SupportsPlanning);
        var preview = session.PlanAction(ActionId, NoParameters, scope: null, expectedRevision: null, cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotNull(preview.PlanId);
        Assert.Equal(2, preview.Plan.Changes.Count);
        Assert.Equal(63, preview.Plan.SkippedRows.Count);
        Assert.Contains(preview.Plan.Warnings, warning => warning.Contains("may reject", StringComparison.Ordinal));
        WriteTestSupport.EqualBytes(reduced, document);
        Assert.True(session.ApplyActionPlan(preview.PlanId, preview.Revision, cancellationToken: TestContext.Current.CancellationToken).Ok);
        WriteTestSupport.EqualBytes(original, document);
        using var reopened = WriteTestSupport.Reopen(document);
        var quantities = (IQueryableDocument)reopened;
        Assert.Equal(12U, Assert.IsType<FieldNode>(Assert.Single(quantities.Query("slots/0/inventory/held",
            new() { Text = "0x400050F0" }).Rows).Cells["quantity"]).Value.AsUInt32());
        Assert.Equal(3U, Assert.IsType<FieldNode>(Assert.Single(quantities.Query("slots/0/inventory/held",
            new() { Text = "0x40002774" }).Rows).Cells["quantity"]).Value.AsUInt32());
        var slot = EldenRingParser.Parse(original).Slots[0];
        var ranges = SemanticReader.Read(original, slot).Containers.SelectMany(container => container.Entries)
            .Where(entry => entry.ItemId is 0x400050F0 or 0x40002774).Select(entry => (entry.Offset + sizeof(uint), sizeof(uint))).ToArray();
        WriteTestSupport.OnlyRanges(reduced, original, ranges);
        Assert.True(session.Undo().Ok);
        WriteTestSupport.EqualBytes(reduced, document); // Both changes are undone together.
        Assert.True(session.Redo().Ok);
        WriteTestSupport.EqualBytes(original, document);
        Assert.True(session.Undo().Ok);
        Assert.True(session.Undo().Ok);
        Assert.False(session.State.CanUndo);
        Assert.False(session.State.IsDirty);
        WriteTestSupport.EqualBytes(original, document);
    }

    [Fact]
    public void SampleFilteredScope_ResolvesUnloadedRowsAndHonorsExclusionsAndStaleness()
    {
        using var session = WriteTestSupport.Session(WriteTestSupport.Sample(), out var document);
        var fruitPage = session.QueryCollection("slots/0/inventory/held", new() { Text = "0x400050F0" }, expectedRevision: null, cancellationToken: TestContext.Current.CancellationToken);
        var fruitRow = Assert.Single(fruitPage.Rows);
        var fruit = Assert.IsType<FieldNode>(fruitRow.Cells["quantity"]);
        var stone = WriteTestSupport.Quantity(session, "held", "0x40002774");
        Assert.True(session.ApplyEdits([WriteTestSupport.Incoming(fruit.ValueId, 10), WriteTestSupport.Incoming(stone.ValueId, 1)], expectedRevision: null, cancellationToken: TestContext.Current.CancellationToken).Ok);
        var reduced = WriteTestSupport.Bytes(document);
        var empty = session.PlanAction(ActionId, NoParameters, new([]), expectedRevision: null, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Empty(empty.Plan.Changes);
        var scoped = new ActionScope { QueryScope = new("slots/0/inventory/held", new()
            { Filters = new Dictionary<string, string> { ["category"] = "goods" }, Limit = 1 }, session.State.Revision)
            { ExcludedRowKeys = [fruitRow.Key] } };
        var preview = session.PlanAction(ActionId, NoParameters, scoped, expectedRevision: null, cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotNull(preview.PlanId);
        Assert.DoesNotContain(fruitRow.Key, preview.Scope.RowKeys!);
        Assert.Equal(stone.ValueId, Assert.Single(preview.Plan.Changes).ValueId);
        Assert.NotEmpty(preview.Plan.SkippedRows);
        Assert.True(session.ApplyActionPlan(preview.PlanId, preview.Revision, cancellationToken: TestContext.Current.CancellationToken).Ok);
        Assert.Equal(10U, WriteTestSupport.Quantity(session, "held", "0x400050F0").Value.AsUInt32());
        Assert.Equal(3U, WriteTestSupport.Quantity(session, "held", "0x40002774").Value.AsUInt32());
        Assert.True(session.Undo().Ok);
        WriteTestSupport.EqualBytes(reduced, document);
        var stale = session.PlanAction(ActionId, NoParameters, scope: null, expectedRevision: null, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(session.ApplyEdits([WriteTestSupport.Incoming(stone.ValueId, 2)], expectedRevision: null, cancellationToken: TestContext.Current.CancellationToken).Ok);
        var afterEdit = WriteTestSupport.Bytes(document);
        Assert.True(session.ApplyActionPlan(stale.PlanId!, stale.Revision, cancellationToken: TestContext.Current.CancellationToken).Stale);
        WriteTestSupport.EqualBytes(afterEdit, document);
    }

    [Fact]
    public void DirectRefill_RegistersUnopenedRowsAndRejectsTamperedPlansInvalidScopesAndParameters()
    {
        var fixture = new SemanticFixture();
        fixture.Row("held", "common", 0, 0xB00050F0, 1, 1);
        fixture.Row("storage", "common", 0, 0xB00050F0, 2, 2);
        fixture.Row("held", "key", 0, 0xB0002198, 1, 3);
        using var document = fixture.Save.Open();
        var planner = (IActionPlanningDocument)document;
        var parameters = new Dictionary<string, FieldValue>();
        var preview = planner.PlanAction(ActionId, parameters, new());
        Assert.Equal(2, preview.Changes.Count);
        Assert.Single(preview.SkippedRows);
        Assert.Throws<InvalidOperationException>(() => document.InvokeAction(ActionId, parameters));
        Assert.False(planner.ApplyActionPlan(ActionId, parameters, new(), preview with { SkippedRows = [] }).Succeeded);
        Assert.False(planner.ApplyActionPlan(ActionId, parameters, new(), preview with { Warnings = [] }).Succeeded);
        Assert.NotEmpty(planner.PlanAction(ActionId, new Dictionary<string, FieldValue> { ["extra"] = FieldValue.Of(1L) }, new()).Errors);
        Assert.NotEmpty(planner.PlanAction(ActionId, parameters, new(["other-character"])).Errors);
        Assert.NotEmpty(planner.PlanAction(ActionId, parameters, new() { QueryScope = new("catalog/items", new(), 0) }).Errors);
        Assert.NotEmpty(planner.PlanAction(ActionId, parameters, new() { QueryScope = new("slots/0/inventory/held", new(), 0) }).Errors);
        var heldKey = SemanticReader.Read(fixture.Save.Bytes, EldenRingParser.Parse(fixture.Save.Bytes).Slots[0])
            .Containers[0].Entries[0].Key(0);
        Assert.NotEmpty(planner.PlanAction(ActionId, parameters, new([heldKey, heldKey])).Errors);
        Assert.Empty(planner.PlanAction(ActionId, parameters, new([])).Changes);
        WriteTestSupport.EqualBytes(fixture.Save.Bytes, document);
        var outcome = planner.ApplyActionPlan(ActionId, parameters, new(), preview);
        Assert.True(outcome.Succeeded);
        Assert.Equal(2, outcome.Changes.Count);
        Assert.False(planner.ApplyActionPlan(ActionId, parameters, new(), preview).Succeeded);
        Assert.True(document.ApplyEdits(outcome.Changes.Select(change => new ValueEdit(change.ValueId, change.OldValue)).ToArray()).Succeeded);
        WriteTestSupport.EqualBytes(fixture.Save.Bytes, document);
        var readOnly = new SemanticFixture();
        readOnly.Save.Write32(SyntheticSave.UserData, 91);
        using var readOnlyDocument = readOnly.Save.Open();
        Assert.NotEmpty(((IActionPlanningDocument)readOnlyDocument).PlanAction(ActionId, parameters, new()).Errors);
    }
}
