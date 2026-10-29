using System.Buffers.Binary;
using System.Text.Json;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Core.Storage;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Testing.Diff;

namespace SaveToolkit.Plugins.Cyberpunk2077.Tests;

public sealed class StackActionTests
{
    private static readonly Dictionary<string, JsonElement> NoParameters = [];
    private static readonly string[] QueryValueIds = ["inventory/49/quantity", "inventory/51/quantity"];
    private static Dictionary<string, JsonElement> Target(int quantity) => new() { ["quantity"] = JsonSerializer.SerializeToElement(quantity.ToString()) };

    [Fact]
    public void SetTargetPreviewsExclusionsPatchesOnlySelectedStacksAndUndoesAsOneGroup()
    {
        var file = ProgressionSampleTests.Files()[0];
        var original = CsavContainer.Parse(file.ReadAllBytes());
        var items = InventoryReader.Read(original, CsavTree.Parse(original));
        var plugin = new Cyberpunk2077Plugin();
        using var document = plugin.Open(file);
        using var session = new DocumentSession("stacks-write", (DiskSaveFile)file, plugin, document);
        _ = session.BuildView();
        var preview = session.PlanAction(StackEditor.SetTarget, Target(700), new ActionScope(["49", "50", "39", "40"]), expectedRevision: null, cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotNull(preview.PlanId);
        Assert.Equal(2, preview.Plan.Changes.Count);
        Assert.Equal(2, preview.Plan.SkippedRows.Count);
        Assert.Contains(preview.Plan.SkippedRows, row => row.RowKey == "39" && row.Reason.Contains("Money", StringComparison.Ordinal));
        Assert.Contains(preview.Plan.Warnings, warning => warning.Contains("game may reject", StringComparison.Ordinal));
        Assert.False(session.State.CanUndo);
        Assert.Equal(original.Original, Assert.Single(document.Serialize()).Content.ToArray());
        Assert.False(session.InvokeAction(StackEditor.SetTarget, Target(700), expectedRevision: null, cancellationToken: TestContext.Current.CancellationToken).Ok);
        Assert.True(session.ApplyActionPlan(preview.PlanId, preview.Revision, cancellationToken: TestContext.Current.CancellationToken).Ok);
        var bytes = Assert.Single(session.Serialize(cancellationToken: TestContext.Current.CancellationToken)).Content.ToArray();
        var expected = original.Payload.ToArray();
        foreach (var node in new[] { 49, 50 }) BinaryPrimitives.WriteUInt32LittleEndian(expected.AsSpan(items.Single(item => item.NodeId == node).QuantityOffset!.Value), 700);
        var after = CsavContainer.Parse(bytes);
        Assert.Equal(expected, after.Payload);
        Assert.InRange(ByteDiffer.Compare(original.Payload, after.Payload).ChangedBytes, 1, 8);
        using var reopened = ProgressionSampleTests.Open(bytes);
        _ = CyberpunkSampleTests.ExpandAll(reopened);
        Assert.Equal(FieldValue.Of(700L), reopened.GetValue("inventory/49/quantity"));
        Assert.Equal(FieldValue.Of(700L), reopened.GetValue("inventory/50/quantity"));
        Assert.True(session.Undo().Ok);
        Assert.False(session.State.CanUndo);
        Assert.Equal(original.Original, Assert.Single(document.Serialize()).Content.ToArray());
        Assert.True(session.Redo().Ok);
        Assert.Equal(bytes, Assert.Single(session.Serialize(cancellationToken: TestContext.Current.CancellationToken)).Content.ToArray());
    }

    [Fact]
    public void RefillUsesOriginalObservedQuantitiesNeverReducesAndHasOneUndoStep()
    {
        var file = ProgressionSampleTests.Files()[0];
        var plugin = new Cyberpunk2077Plugin();
        using var document = plugin.Open(file);
        using var session = new DocumentSession("refill", (DiskSaveFile)file, plugin, document);
        _ = CyberpunkSampleTests.ExpandAll(document);
        _ = session.BuildView();
        _ = session.QueryCollection("inventory", new QueryableCollectionQuery(), expectedRevision: null, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(session.ApplyEdits([new("inventory/49/quantity", JsonSerializer.SerializeToElement("5")),
            new("inventory/50/quantity", JsonSerializer.SerializeToElement("6")), new("inventory/51/quantity", JsonSerializer.SerializeToElement("999"))], expectedRevision: null, cancellationToken: TestContext.Current.CancellationToken).Ok);
        var before = Assert.Single(session.Serialize(cancellationToken: TestContext.Current.CancellationToken)).Content.ToArray();
        var preview = session.PlanAction(StackEditor.Refill, NoParameters, new ActionScope(["49", "50", "51"]), expectedRevision: null, cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotNull(preview.PlanId);
        Assert.Equal(2, preview.Plan.Changes.Count);
        Assert.Single(preview.Plan.SkippedRows);
        Assert.True(session.ApplyActionPlan(preview.PlanId, preview.Revision, cancellationToken: TestContext.Current.CancellationToken).Ok);
        var bytes = Assert.Single(session.Serialize(cancellationToken: TestContext.Current.CancellationToken)).Content.ToArray();
        using var reopened = ProgressionSampleTests.Open(bytes);
        _ = CyberpunkSampleTests.ExpandAll(reopened);
        Assert.Equal(FieldValue.Of(600L), reopened.GetValue("inventory/49/quantity"));
        Assert.Equal(FieldValue.Of(600L), reopened.GetValue("inventory/50/quantity"));
        Assert.Equal(FieldValue.Of(999L), reopened.GetValue("inventory/51/quantity"));
        var expected = CsavContainer.Parse(before).Payload.ToArray();
        var container = CsavContainer.Parse(before);
        foreach (var item in InventoryReader.Read(container, CsavTree.Parse(container)).Where(item => item.NodeId is 49 or 50))
            BinaryPrimitives.WriteUInt32LittleEndian(expected.AsSpan(item.QuantityOffset!.Value), 600);
        Assert.Equal(expected, CsavContainer.Parse(bytes).Payload);
        Assert.True(session.Undo().Ok);
        Assert.Equal(before, Assert.Single(session.Serialize(cancellationToken: TestContext.Current.CancellationToken)).Content.ToArray());
        Assert.True(session.Redo().Ok);
        Assert.Equal(bytes, Assert.Single(session.Serialize(cancellationToken: TestContext.Current.CancellationToken)).Content.ToArray());
    }

    [Fact]
    public void QueryScopeResolvesUnloadedRowsAndEmptySelectionDoesNothing()
    {
        var file = ProgressionSampleTests.Files()[0];
        var plugin = new Cyberpunk2077Plugin();
        using var document = plugin.Open(file);
        using var session = new DocumentSession("query-scope", (DiskSaveFile)file, plugin, document);
        _ = session.BuildView();
        var queryScope = new ActionScope { QueryScope = new("inventory", new QueryableCollectionQuery { Text = "Ammo." }, session.State.Revision)
            { ExcludedRowKeys = ["50"] } };
        var preview = session.PlanAction(StackEditor.SetTarget, Target(700), queryScope, expectedRevision: null, cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotNull(preview.PlanId);
        Assert.Equal(QueryValueIds, preview.Plan.Changes.Select(change => change.ValueId).Order());
        Assert.True(session.ApplyActionPlan(preview.PlanId, preview.Revision, cancellationToken: TestContext.Current.CancellationToken).Ok);
        Assert.True(session.Undo().Ok);
        var empty = session.PlanAction(StackEditor.SetTarget, Target(700), new ActionScope([]), expectedRevision: null, cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotNull(empty.PlanId);
        Assert.Empty(empty.Plan.Changes);
        Assert.True(session.ApplyActionPlan(empty.PlanId, empty.Revision, cancellationToken: TestContext.Current.CancellationToken).Ok);
        Assert.Equal(file.ReadAllBytes(), Assert.Single(document.Serialize()).Content.ToArray());
    }

    [Fact]
    public void InvalidParametersScopesAndTamperedOrStalePlansAreAtomic()
    {
        var file = ProgressionSampleTests.Files()[0];
        using var document = new Cyberpunk2077Plugin().Open(file);
        _ = CyberpunkSampleTests.ExpandAll(document);
        var planner = Assert.IsAssignableFrom<IActionPlanningDocument>(document);
        var parameters = new Dictionary<string, FieldValue> { ["quantity"] = FieldValue.Of(700L) };
        foreach (var invalid in new[] { FieldValue.Of(-1L), FieldValue.Of(1_000_000L), FieldValue.Of("bad") })
            Assert.NotEmpty(planner.PlanAction(StackEditor.SetTarget, new Dictionary<string, FieldValue> { ["quantity"] = invalid }, new()).Errors);
        foreach (var scope in new[] { new ActionScope(["missing"]), new ActionScope(["49", "49"]),
            new ActionScope { QueryScope = new("facts", new(), 0) }, new ActionScope { QueryScope = new("inventory", new(), 0) } })
            Assert.NotEmpty(planner.PlanAction(StackEditor.SetTarget, parameters, scope).Errors);
        var validScope = new ActionScope(["49"]);
        var plan = planner.PlanAction(StackEditor.SetTarget, parameters, validScope);
        Assert.False(planner.ApplyActionPlan(StackEditor.SetTarget, parameters, validScope, plan with { Changes = [] }).Succeeded);
        Assert.Equal(file.ReadAllBytes(), Assert.Single(document.Serialize()).Content.ToArray());
        Assert.True(document.ApplyEdits([new("inventory/49/quantity", FieldValue.Of(601L))]).Succeeded);
        var changed = Assert.Single(document.Serialize()).Content.ToArray();
        Assert.False(planner.ApplyActionPlan(StackEditor.SetTarget, parameters, validScope, plan).Succeeded);
        Assert.Equal(changed, Assert.Single(document.Serialize()).Content.ToArray());
    }
}
