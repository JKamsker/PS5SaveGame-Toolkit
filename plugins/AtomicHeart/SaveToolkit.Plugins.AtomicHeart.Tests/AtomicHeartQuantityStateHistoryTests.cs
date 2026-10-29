using System.Text.Json;
using SaveToolkit.Core.References;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Core.Storage;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.AtomicHeart.Tests;

public sealed class AtomicHeartQuantityStateHistoryTests
{
    private static readonly IReadOnlyDictionary<string, JsonElement> NoParameters = new Dictionary<string, JsonElement>();

    [Theory]
    [InlineData("IntProperty", 4L)]
    [InlineData("Int64Property", long.MaxValue)]
    public void FilterMembershipTracksEditsRestoreAndHistoryWhileOpeningAmountsStayFixed(string type, long opening)
    {
        var bytes = InventoryFixture.Create(amountType: type, firstAmount: opening, secondAmount: 2);
        var file = Import(bytes);
        var references = Assert.IsType<ReferenceContext>(WriteTestData.References());
        var document = WriteTestData.Plugin.Open(file, references);
        using var session = new DocumentSession("quantity-history", file, WriteTestData.Plugin, document, references: references);
        _ = session.BuildView();
        var rows = session.QueryCollection(InventoryCollection.Id, new() { SortColumn = "instance" },
            expectedRevision: null, cancellationToken: TestContext.Current.CancellationToken).Rows;
        var reducedKey = rows[0].Key;
        var increasedKey = rows[1].Key;
        Assert.True(session.ApplyEdits([
            new(reducedKey + "/amount", JsonSerializer.SerializeToElement(0)),
            new(increasedKey + "/amount", JsonSerializer.SerializeToElement(3)),
            new(reducedKey + "/asset", JsonSerializer.SerializeToElement("DA_Item_ShotgunKS23")),
        ], session.State.Revision, cancellationToken: TestContext.Current.CancellationToken).Ok);
        var reduced = Query(session, "Reduced since import");
        Assert.Equal(reducedKey, Assert.Single(reduced.Rows).Key);
        Assert.False(reduced.Rows[0].IsEmpty);
        Assert.Equal(increasedKey, Assert.Single(Query(session, "Increased since import").Rows).Key);
        var edited = WriteTestData.Output(document);
        var scope = Scope(session);
        var stateBeforePreview = session.State;
        var preview = session.PlanAction(InventoryPlanning.RestoreActionId, NoParameters, scope,
            session.State.Revision, cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotNull(preview.PlanId);
        Assert.Equal(FieldValue.Of(opening), Assert.Single(preview.Plan.Changes).NewValue);
        Assert.Empty(preview.Plan.SkippedRows);
        Assert.Equal(stateBeforePreview, session.State);
        Assert.Equal(edited, WriteTestData.Output(document));
        Assert.True(session.ApplyEdits([new(reducedKey + "/amount", JsonSerializer.SerializeToElement(1))],
            session.State.Revision, cancellationToken: TestContext.Current.CancellationToken).Ok);
        Assert.False(session.ApplyActionPlan(preview.PlanId!, session.State.Revision, cancellationToken: TestContext.Current.CancellationToken).Ok);
        Assert.True(session.Undo().Ok);
        preview = session.PlanAction(InventoryPlanning.RestoreActionId, NoParameters, Scope(session),
            session.State.Revision, cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotNull(preview.PlanId);
        Assert.True(session.ApplyActionPlan(preview.PlanId!, session.State.Revision, cancellationToken: TestContext.Current.CancellationToken).Ok);
        Assert.Empty(Query(session, "Reduced since import").Rows);
        Assert.Equal(1, Query(session, "Increased since import").FilteredCount);
        Assert.Equal(FieldValue.Of("DA_Item_ShotgunKS23"), document.GetValue(reducedKey + "/asset"));
        var restored = WriteTestData.Output(document);
        _ = WriteTestData.IndependentDecode(bytes, restored);
        Assert.True(session.Undo().Ok);
        Assert.Equal(reducedKey, Assert.Single(Query(session, "Reduced since import").Rows).Key);
        Assert.Equal(edited, WriteTestData.Output(document));
        Assert.True(session.Redo().Ok);
        Assert.Empty(Query(session, "Reduced since import").Rows);
        Assert.Equal(restored, WriteTestData.Output(document));
        Assert.True(session.Undo().Ok);
        Assert.True(session.Undo().Ok);
        Assert.Empty(Query(session, "Reduced since import").Rows);
        Assert.Equal(2, Query(session, "Unchanged").FilteredCount);
        Assert.Equal(bytes, WriteTestData.Output(document));
        Assert.True(session.Redo().Ok);
        Assert.Equal(reducedKey, Assert.Single(Query(session, "Reduced since import").Rows).Key);
        Assert.Equal(edited, WriteTestData.Output(document));
    }

    [Fact]
    public void ReducedQueryRestoresUnloadedRowsHonorsExclusionsAndKeepsEmptyScopeEmpty()
    {
        var bytes = InventoryFixture.Create();
        var file = Import(bytes);
        var references = Assert.IsType<ReferenceContext>(WriteTestData.References());
        var document = WriteTestData.Plugin.Open(file, references);
        var planner = Assert.IsAssignableFrom<IActionPlanningDocument>(document);
        var parameters = new Dictionary<string, FieldValue> { ["amount"] = FieldValue.Of(0) };
        var batch = planner.PlanAction(InventoryPlanning.ActionId, parameters, new());
        Assert.Equal(2, batch.Changes.Count);
        Assert.True(planner.ApplyActionPlan(InventoryPlanning.ActionId, parameters, new(), batch).Succeeded);
        var staged = WriteTestData.Output(document);
        // Staging precedes the session; only one row is loaded there before restoring.
        using var session = new DocumentSession("quantity-exclusions", file, WriteTestData.Plugin, document, references: references);
        _ = session.BuildView();
        var loaded = session.QueryCollection(InventoryCollection.Id,
            AtomicHeartQuantityStateTests.Filter("Reduced since import") with { Limit = 1 },
            expectedRevision: null, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(2, loaded.FilteredCount);
        var excluded = Assert.Single(loaded.Rows).Key;
        var preview = session.PlanAction(InventoryPlanning.RestoreActionId, NoParameters, Scope(session, [excluded]),
            session.State.Revision, cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotNull(preview.PlanId);
        var change = Assert.Single(preview.Plan.Changes);
        Assert.NotEqual(excluded + "/amount", change.ValueId);
        Assert.Empty(preview.Plan.SkippedRows);
        Assert.Equal(staged, WriteTestData.Output(document));
        Assert.True(session.ApplyActionPlan(preview.PlanId!, session.State.Revision, cancellationToken: TestContext.Current.CancellationToken).Ok);
        Assert.Equal(excluded, Assert.Single(Query(session, "Reduced since import").Rows).Key);
        Assert.True(session.Undo().Ok);
        Assert.False(session.State.CanUndo);
        Assert.Equal(2, Query(session, "Reduced since import").FilteredCount);
        Assert.Equal(staged, WriteTestData.Output(document));
        Assert.True(session.Redo().Ok);
        Assert.Equal(excluded, Assert.Single(Query(session, "Reduced since import").Rows).Key);
        var allExcluded = session.PlanAction(InventoryPlanning.RestoreActionId, NoParameters, Scope(session, [excluded]),
            session.State.Revision, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Empty(allExcluded.Scope.RowKeys!);
        Assert.Empty(allExcluded.Plan.Changes);
        var remaining = session.PlanAction(InventoryPlanning.RestoreActionId, NoParameters, Scope(session),
            session.State.Revision, cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotNull(remaining.PlanId);
        Assert.True(session.ApplyActionPlan(remaining.PlanId!, session.State.Revision, cancellationToken: TestContext.Current.CancellationToken).Ok);
        Assert.Empty(Query(session, "Reduced since import").Rows);
        Assert.Equal(bytes, WriteTestData.Output(document));
        var empty = session.PlanAction(InventoryPlanning.RestoreActionId, NoParameters, Scope(session),
            session.State.Revision, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Empty(empty.Scope.RowKeys!);
        Assert.Empty(empty.Plan.Changes);
    }

    private static DiskSaveFile Import(byte[] bytes) => Assert.Single(SaveSetImporter.Import(
        [new ImportSource("PPSA10695/sdimg_authored/slot", () => new MemoryStream(bytes, writable: false))],
        Path.Combine(TestScratch.ProcessFolder, "atomic-quantity-" + Guid.NewGuid().ToString("N")),
        limits: null, cancellationToken: TestContext.Current.CancellationToken).AllDiskFiles);

    private static QueryableCollectionPage Query(DocumentSession session, string state) => session.QueryCollection(
        InventoryCollection.Id, AtomicHeartQuantityStateTests.Filter(state), expectedRevision: null,
        cancellationToken: TestContext.Current.CancellationToken);

    private static ActionScope Scope(DocumentSession session, IReadOnlyList<string>? excluded = null) => new() {
        QueryScope = new(InventoryCollection.Id, AtomicHeartQuantityStateTests.Filter("Reduced since import") with { Limit = 1 },
            session.State.Revision) { ExcludedRowKeys = excluded ?? [] },
    };
}
