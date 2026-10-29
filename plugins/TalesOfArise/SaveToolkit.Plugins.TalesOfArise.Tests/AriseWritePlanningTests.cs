using System.Text.Json;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Core.Storage;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Saves;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.TalesOfArise.Tests;

public sealed class AriseWritePlanningTests
{
    private const string ActionId = "inventory/set-quantity";
    private const string RowKey = "inventory/24/quantity";
    private readonly TalesOfArisePlugin _plugin = new();

    [Fact]
    public void ExistingRowQuantityPlan_ListsWarning_PreservesUnrelatedPlaintext_AndReopens()
    {
        var file = AriseSampleTests.GameplayFiles().Single(file => file.FileName == "SaveData_002.sav" && !file.Directory.IsBackup);
        using var document = _plugin.Open(file);
        _ = document.BuildView();
        _ = document.LoadInventory();
        var planner = Assert.IsAssignableFrom<IActionPlanningDocument>(document);
        var before = AriseFormat.Decode(file.ReadAllBytes());
        var row = Assert.Single(((AriseDocument)document).ReadInventoryRows(), row => row.Index == 24);
        Assert.Equal((ushort)1, row.Id);

        var parameters = Parameters(15);
        var plan = planner.PlanAction(ActionId, parameters, new ActionScope([RowKey]));
        Assert.Empty(plan.Errors);
        Assert.Single(plan.Changes);
        Assert.Contains(plan.Warnings, warning => warning.Contains("Experimental", StringComparison.Ordinal));
        Assert.Empty(plan.SkippedRows);

        var outcome = planner.ApplyActionPlan(ActionId, parameters, new ActionScope([RowKey]), plan);
        Assert.True(outcome.Succeeded);
        Assert.NotEmpty(outcome.Warnings);
        var output = Assert.Single(document.Serialize()).Content.ToArray();
        var after = AriseFormat.Decode(output);
        Assert.Equal(before.Original.AsSpan(0, AriseFormat.PayloadOffset).ToArray(), output.AsSpan(0, AriseFormat.PayloadOffset).ToArray());
        for (var offset = 0x20; offset < after.Payload.Length; offset++)
        {
            if (offset != row.Offset)
            {
                Assert.Equal(before.Payload[offset], after.Payload[offset]);
            }
        }

        using var reopened = _plugin.Open(MemorySaveSet.SingleFile(file.FileName, output, "PPSA02745"));
        _ = reopened.BuildView();
        _ = reopened.LoadInventory();
        Assert.Equal(15, reopened.GetValue(RowKey).AsInt64());

        var undo = document.ApplyEdits(outcome.Changes.Select(change => new ValueEdit(change.ValueId, change.OldValue)).ToArray());
        Assert.True(undo.Succeeded);
        Assert.Equal(before.Original, Assert.Single(document.Serialize()).Content.ToArray());
    }

    [Fact]
    public void QuantityPlan_RequiresInventoryExpansion_AndSkipsUnknownRows()
    {
        var file = AriseSampleTests.GameplayFiles().First(file => file.FileName == "SaveData_002.sav" && !file.Directory.IsBackup);
        using var document = _plugin.Open(file);
        _ = document.BuildView();
        var planner = Assert.IsAssignableFrom<IActionPlanningDocument>(document);
        var notExpanded = planner.PlanAction(ActionId, Parameters(15), new ActionScope([RowKey]));
        Assert.NotEmpty(notExpanded.Errors);

        _ = document.LoadInventory();
        var all = planner.PlanAction(ActionId, Parameters(15), new ActionScope());
        Assert.Empty(all.Errors);
        Assert.NotEmpty(all.SkippedRows);
        Assert.Contains(all.SkippedRows, row => row.Reason.Contains("recognised", StringComparison.Ordinal));
        Assert.NotEmpty(all.Changes);
        Assert.NotEmpty(all.Warnings);

        var invalid = planner.PlanAction(ActionId, Parameters(16), new ActionScope([RowKey]));
        Assert.NotEmpty(invalid.Errors);
        var duplicate = planner.PlanAction(ActionId, Parameters(15), new ActionScope([RowKey, RowKey]));
        Assert.NotEmpty(duplicate.Errors);
        Assert.Throws<InvalidOperationException>(() => document.InvokeAction(ActionId, Parameters(15)));
    }

    [Fact]
    public void QuantityPlan_IsOneCoreUndoGroup_AndRedoable()
    {
        var file = Assert.IsType<DiskSaveFile>(AriseSampleTests.GameplayFiles().Single(file => file.FileName == "SaveData_002.sav" && !file.Directory.IsBackup));
        using var document = _plugin.Open(file);
        using var session = new DocumentSession("arise-write", file, _plugin, document);
        _ = session.BuildView();
        _ = session.Expand("inventory");
        _ = session.QueryCollection("inventory", new(), expectedRevision: null, cancellationToken: TestContext.Current.CancellationToken);

        var preview = session.PlanAction(ActionId, ParametersAsJson(15), new ActionScope([RowKey]), expectedRevision: null, cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotNull(preview.PlanId);
        Assert.Single(preview.Plan.Changes);
        var applied = session.ApplyActionPlan(preview.PlanId!, preview.Revision, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(applied.Ok);
        Assert.True(session.State.CanUndo);
        var edited = Assert.Single(document.Serialize()).Content.ToArray();
        Assert.Equal(15, document.GetValue(RowKey).AsInt64());
        var changes = session.GetChanges(cancellationToken: TestContext.Current.CancellationToken).Changes;
        Assert.Contains(changes, change => change.ValueId == RowKey && change.CurrentValue.AsInt64() == 15);
        // Core's semantic baseline does not traverse F8 rows; query-only changes use undo.
        Assert.False(changes.Single(change => change.ValueId == RowKey).CanRevert);

        Assert.True(session.Undo().Ok);
        Assert.Equal(file.ReadAllBytes(), Assert.Single(document.Serialize()).Content.ToArray());
        Assert.False(session.State.CanUndo);
        Assert.True(session.Redo().Ok);
        Assert.True(session.State.CanUndo);
        Assert.Equal(15, document.GetValue(RowKey).AsInt64());
        Assert.Equal(edited, Assert.Single(document.Serialize()).Content.ToArray());
    }

    [Fact]
    public void QuantityPlan_ResolvesF8QueryScopeAcrossUnloadedInventoryRows()
    {
        var file = Assert.IsType<DiskSaveFile>(AriseSampleTests.GameplayFiles().First(file => file.FileName == "SaveData_002.sav" && !file.Directory.IsBackup));
        using var document = _plugin.Open(file);
        using var session = new DocumentSession("arise-query-write", file, _plugin, document);
        _ = session.BuildView();
        var scope = new ActionScope
        {
            QueryScope = new ActionQueryScope("inventory", new QueryableCollectionQuery { Text = "Apple Gel" }, session.State.Revision),
        };
        var preview = session.PlanAction(ActionId, ParametersAsJson(12), scope, expectedRevision: null, cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotNull(preview.PlanId);
        Assert.Contains("inventory/24/quantity", preview.Scope.RowKeys!);
        Assert.Single(preview.Plan.Changes);
        Assert.True(session.ApplyActionPlan(preview.PlanId!, preview.Revision, cancellationToken: TestContext.Current.CancellationToken).Ok);
        Assert.Equal(12, session.GetChanges(cancellationToken: TestContext.Current.CancellationToken).Changes.Single(change => change.ValueId == RowKey).CurrentValue.AsInt64());
        Assert.True(session.Undo().Ok);
        Assert.False(session.State.IsDirty);
    }

    private static IReadOnlyDictionary<string, FieldValue> Parameters(long quantity) =>
        new Dictionary<string, FieldValue> { ["quantity"] = FieldValue.Of(quantity) };

    private static IReadOnlyDictionary<string, JsonElement> ParametersAsJson(long quantity) =>
        new Dictionary<string, JsonElement> { ["quantity"] = JsonSerializer.SerializeToElement(quantity.ToString()) };
}
