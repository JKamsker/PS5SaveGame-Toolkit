using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.TalesOfArise.Tests;

public sealed class ArisePlanningBoundaryTests
{
    private const string ActionId = "inventory/set-quantity";
    private const string FirstRow = "inventory/0/quantity";
    private const string SecondRow = "inventory/1/quantity";
    private const string OverBoundRow = "inventory/2/quantity";
    private const string UnknownRow = "inventory/3/quantity";
    private const string EmptyRow = "inventory/4/quantity";

    [Theory]
    [InlineData(0)]
    [InlineData(15)]
    public void BoundaryQuantities_PreviewExactEffects_PreserveOtherBytes_ReopenAndUndo(long target)
    {
        var fixture = new AriseTestContainer();
        var before = fixture.Encrypt();
        using var document = Open(before);
        _ = document.BuildView();
        _ = document.LoadInventory();
        var planner = Assert.IsAssignableFrom<IActionPlanningDocument>(document);
        var parameters = Parameters(FieldValue.Of(target));
        var scope = new ActionScope();

        var plan = planner.PlanAction(ActionId, parameters, scope);
        Assert.Empty(plan.Errors);
        Assert.Contains(plan.Warnings, warning => warning.Contains("Experimental", StringComparison.Ordinal));
        var first = Assert.Single(plan.Changes, change => change.ValueId == FirstRow);
        Assert.Equal(FieldValue.Of(1L), first.OldValue);
        Assert.Equal(FieldValue.Of(target), first.NewValue);
        Assert.False(first.Derived);
        Assert.Equal(target == 15 ? 1 : 2, plan.Changes.Count);
        if (target != 15)
        {
            var second = Assert.Single(plan.Changes, change => change.ValueId == SecondRow);
            Assert.Equal(FieldValue.Of(15L), second.OldValue);
            Assert.Equal(FieldValue.Of(target), second.NewValue);
        }

        var expectedSkips = target == 15 ? new[] { SecondRow, OverBoundRow, UnknownRow } : [OverBoundRow, UnknownRow];
        Assert.Equal(expectedSkips, plan.SkippedRows.Select(row => row.RowKey));
        Assert.All(plan.SkippedRows, row => Assert.NotEmpty(row.Reason));
        Assert.Equal(before, Bytes(document)); // A preview cannot mutate either bytes or bindings.
        Assert.Equal(1, document.GetValue(FirstRow).AsInt64());

        var outcome = planner.ApplyActionPlan(ActionId, parameters, scope, plan);
        Assert.True(outcome.Succeeded);
        Assert.Equal(plan.Changes.Select(change => new ValueChange(change.ValueId, change.OldValue, change.NewValue)), outcome.Changes);
        Assert.NotEmpty(outcome.Warnings);
        var output = Bytes(document);
        var expected = fixture.Payload.ToArray();
        var inventory = fixture.Body("SaveData_ItemManager");
        expected[inventory] = (byte)target;
        expected[inventory + AriseTestContainer.ItemRecordSize] = (byte)target;
        Assert.Equal(expected.AsSpan(0x20).ToArray(), AriseFormat.Decode(output).Payload.AsSpan(0x20).ToArray());
        Assert.Equal(before.AsSpan(0, AriseFormat.PayloadOffset).ToArray(), output.AsSpan(0, AriseFormat.PayloadOffset).ToArray());

        using var reopened = Open(output);
        _ = reopened.BuildView();
        var rows = reopened.LoadInventory().Rows;
        Assert.Equal(target, reopened.GetValue(FirstRow).AsInt64());
        Assert.Equal(target, reopened.GetValue(SecondRow).AsInt64());
        Assert.Contains(rows, row => row.Key == FirstRow && !row.IsEmpty); // Zero quantity is not an empty identity.
        Assert.Equal(16, reopened.GetValue(OverBoundRow).AsInt64());
        Assert.Equal(5, reopened.GetValue(UnknownRow).AsInt64());
        var undo = document.ApplyEdits(outcome.Changes.Select(change => new ValueEdit(change.ValueId, change.OldValue)).ToArray());
        Assert.True(undo.Succeeded);
        Assert.Equal(before, Bytes(document));
    }

    [Fact]
    public void EmptySelection_IsANoOp_WhileDocumentScopeSelectsEligibleRows()
    {
        using var document = Open(new AriseTestContainer().Encrypt());
        _ = document.BuildView();
        _ = document.LoadInventory();
        var planner = Assert.IsAssignableFrom<IActionPlanningDocument>(document);
        var before = Bytes(document);
        var parameters = Parameters(FieldValue.Of(12L));
        var scope = new ActionScope([]);
        var empty = planner.PlanAction(ActionId, parameters, scope);
        Assert.Empty(empty.Errors);
        Assert.Empty(empty.Changes);
        Assert.Empty(empty.SkippedRows);
        var outcome = planner.ApplyActionPlan(ActionId, parameters, scope, empty);
        Assert.True(outcome.Succeeded);
        Assert.Empty(outcome.Changes);
        Assert.Equal(before, Bytes(document));

        var all = planner.PlanAction(ActionId, parameters, new ActionScope());
        Assert.Empty(all.Errors);
        Assert.Equal(new[] { FirstRow, SecondRow }, all.Changes.Select(change => change.ValueId));
        Assert.Equal(before, Bytes(document));
    }

    [Fact]
    public void MixedExplicitSelection_ReportsEachSkip_AndChangesOnlyTheSelectedEligibleRow()
    {
        var fixture = new AriseTestContainer();
        using var document = Open(fixture.Encrypt());
        _ = document.BuildView();
        _ = document.LoadInventory();
        var planner = Assert.IsAssignableFrom<IActionPlanningDocument>(document);
        var scope = new ActionScope([EmptyRow, UnknownRow, OverBoundRow, FirstRow]);
        var parameters = Parameters(FieldValue.Of(12L));
        var plan = planner.PlanAction(ActionId, parameters, scope);
        Assert.Empty(plan.Errors);
        Assert.Equal(FirstRow, Assert.Single(plan.Changes).ValueId);
        Assert.Equal(new[] { EmptyRow, UnknownRow, OverBoundRow }, plan.SkippedRows.Select(row => row.RowKey));
        Assert.Contains("Empty slot", plan.SkippedRows[0].Reason, StringComparison.Ordinal);
        Assert.Contains("recognised", plan.SkippedRows[1].Reason, StringComparison.Ordinal);
        Assert.Contains("exceeds", plan.SkippedRows[2].Reason, StringComparison.Ordinal);
        Assert.True(planner.ApplyActionPlan(ActionId, parameters, scope, plan).Succeeded);
        var expected = fixture.Payload.ToArray();
        expected[fixture.Body("SaveData_ItemManager")] = 12;
        Assert.Equal(expected.AsSpan(0x20).ToArray(), AriseFormat.Decode(Bytes(document)).Payload.AsSpan(0x20).ToArray());
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("extra")]
    [InlineData("negative")]
    [InlineData("above-limit")]
    [InlineData("text")]
    [InlineData("float")]
    [InlineData("null")]
    [InlineData("unknown-row")]
    [InlineData("duplicate-row")]
    public void InvalidRequests_RejectPreviewAndApply_WithoutAnyPartialWrite(string mutation)
    {
        using var document = Open(new AriseTestContainer().Encrypt());
        _ = document.BuildView();
        _ = document.LoadInventory();
        var planner = Assert.IsAssignableFrom<IActionPlanningDocument>(document);
        var before = Bytes(document);
        var scope = new ActionScope([FirstRow]);
        var parameters = Parameters(FieldValue.Of(12L));
        var valid = planner.PlanAction(ActionId, parameters, scope);
        Assert.Empty(valid.Errors);
        Assert.Single(valid.Changes);
        switch (mutation)
        {
            case "missing": parameters.Clear(); break;
            case "extra": parameters.Add("unexpected", FieldValue.Of(1L)); break;
            case "negative": parameters["quantity"] = FieldValue.Of(-1L); break;
            case "above-limit": parameters["quantity"] = FieldValue.Of(16L); break;
            case "text": parameters["quantity"] = FieldValue.Of("12"); break;
            case "float": parameters["quantity"] = FieldValue.Of(12d); break;
            case "null": parameters["quantity"] = FieldValue.Null; break;
            case "unknown-row": scope = new ActionScope([FirstRow, "inventory/934/quantity"]); break;
            case "duplicate-row": scope = new ActionScope([FirstRow, FirstRow]); break;
            default: throw new ArgumentException("Unknown mutation.", nameof(mutation));
        }

        var rejected = planner.PlanAction(ActionId, parameters, scope);
        Assert.NotEmpty(rejected.Errors);
        Assert.Empty(rejected.Changes);
        var outcome = planner.ApplyActionPlan(ActionId, parameters, scope, valid);
        Assert.False(outcome.Succeeded);
        Assert.Empty(outcome.Changes);
        Assert.NotEmpty(outcome.Warnings);
        Assert.Equal(1, document.GetValue(FirstRow).AsInt64());
        Assert.Equal(before, Bytes(document));
    }

    private static ISaveDocument Open(byte[] bytes) => new TalesOfArisePlugin().Open(
        MemorySaveSet.SingleFile(AriseTestContainer.FileName, bytes, AriseTestContainer.TitleId));
    private static byte[] Bytes(ISaveDocument document) => Assert.Single(document.Serialize()).Content.ToArray();
    private static Dictionary<string, FieldValue> Parameters(FieldValue quantity) => new() { ["quantity"] = quantity };
}
