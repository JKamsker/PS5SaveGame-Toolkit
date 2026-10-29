using System.Buffers.Binary;
using System.Text.Json;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.Witcher3.Tests;

public sealed class WitcherRefillSyntheticTests
{
    [Theory]
    [InlineData("document")]
    [InlineData("explicit")]
    [InlineData("query")]
    public void RefillPersistsOnlySelectedQuantityAndUndoRedoKeepEarlierCurrencyEdit(string selection)
    {
        var input = SyntheticPlayerSave.Create();
        var file = WitcherTestFiles.Import(input);
        var plugin = new Witcher3Plugin();
        using var document = plugin.Open(file);
        using var session = new DocumentSession("synthetic-refill", file, plugin, document);
        _ = session.BuildView();
        _ = session.QueryCollection(InventoryViews.CollectionId, new() { Limit = 200 }, expectedRevision: null, cancellationToken: TestContext.Current.CancellationToken);
        // The selection query below deliberately requests only the first row (currency).
        Assert.Equal("1", Assert.Single(session.QueryCollection(InventoryViews.CollectionId, new() { Limit = 1 }, expectedRevision: null, cancellationToken: TestContext.Current.CancellationToken).Rows).Key);
        Assert.True(session.ApplyEdits([
            EnabledWriteFixture.Incoming("inventory/1/quantity", FieldValue.Of(300)),
            EnabledWriteFixture.Incoming("inventory/2/quantity", FieldValue.Of(1)),
        ], expectedRevision: null, cancellationToken: TestContext.Current.CancellationToken).Ok);
        var reduced = Bytes(document);
        var state = session.State;
        var scope = selection switch {
            "explicit" => new ActionScope(["2"]),
            "query" => new ActionScope { QueryScope = new(InventoryViews.CollectionId, new() { Limit = 1 }, state.Revision) },
            _ => new ActionScope(),
        };
        var preview = session.PlanAction(RefillPlanning.ActionId, new Dictionary<string, JsonElement>(), scope, expectedRevision: null, cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotNull(preview.PlanId);
        Assert.Empty(preview.Plan.Errors);
        var change = Assert.Single(preview.Plan.Changes);
        Assert.Equal("inventory/2/quantity", change.ValueId);
        Assert.Equal(FieldValue.Of(1), change.OldValue);
        Assert.Equal(FieldValue.Of(5), change.NewValue);
        Assert.Equal(selection == "explicit" ? Array.Empty<string>() : ["1"], preview.Plan.SkippedRows.Select(row => row.RowKey));
        Assert.Equal(state, session.State);
        Assert.Equal(reduced, Bytes(document));

        var result = session.ApplyActionPlan(preview.PlanId, preview.Revision, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(result.Ok);
        Assert.Equal(new ValueChange("inventory/2/quantity", FieldValue.Of(1), FieldValue.Of(5)), Assert.Single(result.Changes));
        Assert.Contains(RefillPlanning.Warning, result.Warnings);
        var output = Bytes(document);
        var before = EnabledWriteFixture.Read(reduced);
        var bread = before.Player.Inventory!.Items.Single(i => i.Id == 2);
        var expected = before.Container.Payload.ToArray();
        BinaryPrimitives.WriteUInt16LittleEndian(expected.AsSpan(bread.QuantityOffset), 5);
        Assert.Equal(expected, WitcherContainer.Parse(output).Payload);
        Assert.Equal(input[^2..], output[^2..]);
        Assert.Equal(before.Container.Original.AsSpan(40, before.Container.HeaderSize - 40).ToArray(),
            output.AsSpan(40, before.Container.HeaderSize - 40).ToArray());
        using var reopened = plugin.Open(MemorySaveSet.SingleFile("Manual.sav", output, "PPSA04018"));
        _ = reopened.BuildView();
        Assert.Equal(FieldValue.Of(5), reopened.GetValue("inventory/2/quantity"));
        Assert.Equal(FieldValue.Of(300), reopened.GetValue("inventory/1/quantity"));

        Assert.True(session.Undo().Ok);
        Assert.Equal(reduced, Bytes(document));
        Assert.True(session.Redo().Ok);
        Assert.Equal(output, Bytes(document));
        Assert.True(session.Undo().Ok);
        Assert.Equal(reduced, Bytes(document));
        Assert.True(session.Undo().Ok);
        Assert.Equal(input, Bytes(document));
        Assert.False(session.Undo().Ok);
    }

    [Fact]
    public void QueryExclusionProducesAnEmptyPlanWithoutChangingBytesOrHistory()
    {
        var file = WitcherTestFiles.Import(SyntheticPlayerSave.Create());
        var plugin = new Witcher3Plugin();
        using var document = plugin.Open(file);
        using var session = new DocumentSession("excluded-refill", file, plugin, document);
        _ = session.BuildView();
        _ = session.QueryCollection(InventoryViews.CollectionId, new() { Limit = 200 }, expectedRevision: null, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(session.ApplyEdits([EnabledWriteFixture.Incoming("inventory/2/quantity", FieldValue.Of(1))], expectedRevision: null, cancellationToken: TestContext.Current.CancellationToken).Ok);
        var before = Bytes(document);
        var state = session.State;
        var scope = new ActionScope { QueryScope = new(InventoryViews.CollectionId, new() { Text = "Bread", Limit = 1 }, state.Revision) {
            ExcludedRowKeys = ["2"],
        } };
        var preview = session.PlanAction(RefillPlanning.ActionId, new Dictionary<string, JsonElement>(), scope, expectedRevision: null, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Empty(preview.Plan.Errors);
        Assert.Empty(preview.Plan.Changes);
        Assert.Empty(preview.Plan.SkippedRows);
        Assert.Equal(state, session.State);
        Assert.Equal(before, Bytes(document));
    }

    [Theory]
    [InlineData("new-value")]
    [InlineData("old-value")]
    [InlineData("value-id")]
    [InlineData("missing-change")]
    [InlineData("stale")]
    public void ForgedAndStalePlansRejectWithoutChangingAnyValuesOrBytes(string mutation)
    {
        using var document = WitcherTestFiles.Open(SyntheticPlayerSave.Create());
        _ = document.BuildView();
        Assert.True(document.ApplyEdits([new("inventory/2/quantity", FieldValue.Of(1))]).Succeeded);
        var planner = Assert.IsAssignableFrom<IActionPlanningDocument>(document);
        var parameters = new Dictionary<string, FieldValue>();
        var scope = new ActionScope(["2"]);
        var preview = planner.PlanAction(RefillPlanning.ActionId, parameters, scope);
        Assert.Empty(preview.Errors);
        var change = Assert.Single(preview.Changes);
        var altered = mutation switch {
            "new-value" => preview with { Changes = [change with { NewValue = FieldValue.Of(65535) }] },
            "old-value" => preview with { Changes = [change with { OldValue = FieldValue.Of(2) }] },
            "value-id" => preview with { Changes = [change with { ValueId = "inventory/1/quantity" }] },
            "missing-change" => preview with { Changes = [] },
            _ => preview,
        };
        if (mutation == "stale") { Assert.True(document.ApplyEdits([new("inventory/2/quantity", FieldValue.Of(2))]).Succeeded); }
        var before = Bytes(document);
        var quantity = document.GetValue("inventory/2/quantity");
        var rejected = planner.ApplyActionPlan(RefillPlanning.ActionId, parameters, scope, altered);
        Assert.False(rejected.Succeeded);
        Assert.NotEmpty(rejected.Errors);
        Assert.Empty(rejected.Changes);
        Assert.Equal(quantity, document.GetValue("inventory/2/quantity"));
        Assert.Equal(FieldValue.Of(250), document.GetValue("inventory/1/quantity"));
        Assert.Equal(before, Bytes(document));
    }

    [Fact]
    public void SessionRejectsOldRevisionAndPreservesItsExistingUndoHistory()
    {
        var input = SyntheticPlayerSave.Create();
        var file = WitcherTestFiles.Import(input);
        var plugin = new Witcher3Plugin();
        using var document = plugin.Open(file);
        using var session = new DocumentSession("stale-refill", file, plugin, document);
        _ = session.BuildView();
        _ = session.QueryCollection(InventoryViews.CollectionId, new() { Limit = 200 }, expectedRevision: null, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(session.ApplyEdits([EnabledWriteFixture.Incoming("inventory/2/quantity", FieldValue.Of(1))], expectedRevision: null, cancellationToken: TestContext.Current.CancellationToken).Ok);
        var preview = session.PlanAction(RefillPlanning.ActionId, new Dictionary<string, JsonElement>(), new(), expectedRevision: null, cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotNull(preview.PlanId);
        Assert.True(session.ApplyEdits([EnabledWriteFixture.Incoming("inventory/2/quantity", FieldValue.Of(2))], expectedRevision: null, cancellationToken: TestContext.Current.CancellationToken).Ok);
        var before = Bytes(document);
        var state = session.State;
        var rejected = session.ApplyActionPlan(preview.PlanId, preview.Revision, cancellationToken: TestContext.Current.CancellationToken);
        Assert.False(rejected.Ok);
        Assert.True(rejected.Stale);
        Assert.Empty(rejected.Changes);
        Assert.Equal(state, session.State);
        Assert.Equal(before, Bytes(document));
        Assert.True(session.Undo().Ok);
        Assert.Equal(FieldValue.Of(1), document.GetValue("inventory/2/quantity"));
        Assert.True(session.Undo().Ok);
        Assert.Equal(input, Bytes(document));
        Assert.False(session.Undo().Ok);
    }

    [Theory]
    [InlineData("unknown-row")]
    [InlineData("duplicate-row")]
    [InlineData("wrong-collection")]
    [InlineData("unresolved-query")]
    [InlineData("unexpected-parameter")]
    [InlineData("readonly")]
    public void InvalidRequestsRejectBeforePlanningOrWriting(string request)
    {
        var input = request == "readonly" ? SyntheticSave.Create() : SyntheticPlayerSave.Create();
        using var document = WitcherTestFiles.Open(input);
        _ = document.BuildView();
        if (document.CanEdit) { Assert.True(document.ApplyEdits([new("inventory/2/quantity", FieldValue.Of(1))]).Succeeded); }
        var before = Bytes(document);
        var scope = request switch {
            "unknown-row" => new ActionScope(["missing"]),
            "duplicate-row" => new ActionScope(["2", "2"]),
            "wrong-collection" => new ActionScope(["2"]) { QueryScope = new("wrong", new(), 0) },
            "unresolved-query" => new ActionScope { QueryScope = new(InventoryViews.CollectionId, new(), 0) },
            _ => new ActionScope(),
        };
        var parameters = new Dictionary<string, FieldValue>();
        if (request == "unexpected-parameter") { parameters.Add("quantity", FieldValue.Of(65535)); }
        var planner = Assert.IsAssignableFrom<IActionPlanningDocument>(document);
        var plan = planner.PlanAction(RefillPlanning.ActionId, parameters, scope);
        Assert.NotEmpty(plan.Errors);
        Assert.Empty(plan.Changes);
        Assert.Empty(plan.SkippedRows);
        Assert.Equal(before, Bytes(document));
        var rejected = planner.ApplyActionPlan(RefillPlanning.ActionId, parameters, scope, plan);
        Assert.False(rejected.Succeeded);
        Assert.NotEmpty(rejected.Errors);
        Assert.Empty(rejected.Changes);
        Assert.Equal(before, Bytes(document));
    }

    private static byte[] Bytes(ISaveDocument document) => Assert.Single(document.Serialize()).Content.ToArray();
}
