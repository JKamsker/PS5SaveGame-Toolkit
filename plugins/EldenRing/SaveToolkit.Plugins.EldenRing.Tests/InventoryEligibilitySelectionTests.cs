using System.Buffers.Binary;
using System.Text.Json;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.EldenRing.Tests;

public sealed class InventoryEligibilitySelectionTests
{
    private const string HeldId = "slots/0/inventory/held";
    private const string RefillId = "slots/0/inventory/refill";
    private const int HeldCapacity = SemanticLayout.HeldCommonCapacity + SemanticLayout.HeldKeyCapacity;
    private static readonly Dictionary<string, JsonElement> NoParameters = [];
    private static QueryableCollectionQuery Editable => new()
    {
        Filters = new Dictionary<string, string> { ["editing"] = "eDiTaBlE qUaNtItY" },
        SortColumn = "index", Limit = 1,
    };

    [Fact]
    public void StatusAndReasonQueriesFilterBeforeVisibilitySortingAndPagingWithoutChangingBytes()
    {
        var fixture = Fixture();
        using var document = fixture.Save.Open();
        var queryable = (IQueryableDocument)document;
        var metadata = queryable.Collections.Single(collection => collection.Id == HeldId);
        Assert.Equal("Editing", Assert.Single(metadata.Columns, column => column.Key == "editing").Label);
        var first = queryable.Query(HeldId, new() { Limit = 1, SortColumn = "index" });
        Assert.Equal("Protected", Assert.IsType<TextNode>(Assert.Single(first.Rows).Cells["editing"]).Text);
        var eligible = queryable.Query(HeldId, Editable);
        var second = queryable.Query(HeldId, Editable with { Offset = 1 });
        Assert.Equal(2, eligible.FilteredCount);
        Assert.Equal(0, eligible.HiddenEmptyCount);
        Assert.Equal(2, second.FilteredCount);
        Assert.NotEqual(Assert.Single(eligible.Rows).Key, Assert.Single(second.Rows).Key);
        Assert.Empty(queryable.Query(HeldId, Editable with { Offset = 2 }).Rows);
        Assert.Equal(eligible.Rows[0].Key, Assert.Single(queryable.Query(HeldId, Editable with { Descending = true, Offset = 1 }).Rows).Key);
        Assert.Equal(2, queryable.Query(HeldId, new() { Text = "  EDITABLE quantity  " }).FilteredCount);
        Assert.Equal(2, queryable.Query(HeldId, new() { Text = "outside the two-ID" }).FilteredCount);
        Assert.Empty(queryable.Query(HeldId, Editable with { Text = "outside the two-ID" }).Rows);
        Assert.Empty(queryable.Query(HeldId, new() { Filters = new Dictionary<string, string> { ["editing"] = "Editable" } }).Rows);

        var protectedQuery = new QueryableCollectionQuery
        {
            Filters = new Dictionary<string, string> { ["editing"] = "Protected" }, SortColumn = "editing", Limit = 1,
        };
        var protectedRows = queryable.Query(HeldId, protectedQuery);
        Assert.Equal(2, protectedRows.FilteredCount);
        Assert.Equal(HeldCapacity - 4, protectedRows.HiddenEmptyCount);
        var nextProtected = queryable.Query(HeldId, protectedQuery with { Offset = 1 });
        Assert.True(StringComparer.Ordinal.Compare(Assert.Single(protectedRows.Rows).Key, Assert.Single(nextProtected.Rows).Key) < 0);
        var shown = queryable.Query(HeldId, protectedQuery with { IncludeEmpty = true });
        Assert.Equal(HeldCapacity - 2, shown.FilteredCount);
        Assert.Equal(0, shown.HiddenEmptyCount);
        var emptySearch = queryable.Query(HeldId, protectedQuery with { Text = "All-zero vacant slot" });
        Assert.Empty(emptySearch.Rows);
        Assert.Equal(HeldCapacity - 4, emptySearch.HiddenEmptyCount);
        Assert.Equal(0, queryable.Query(HeldId, protectedQuery with { Text = "no such restriction" }).HiddenEmptyCount);
        var numeric = queryable.Query(HeldId, new() { SortColumn = "quantity", Limit = 4 });
        Assert.Equal(new uint[] { 1, 2, 4, 25 }, numeric.Rows.Select(row => Assert.IsType<FieldNode>(row.Cells["quantity"]).Value.AsUInt32()));
        Assert.Equal(1, queryable.Query("slots/0/inventory/storage", Editable).FilteredCount); // Same handle in a different container is independent.
        WriteTestSupport.EqualBytes(fixture.Save.Bytes, document);
    }

    [Fact]
    public void WholeEligibilityQuerySelectsUnloadedRowsAndPreservesExclusionsHistoryAndExactBytes()
    {
        var fixture = Fixture();
        using var disk = new SyntheticDiskSave(fixture.Save.Bytes);
        using var session = WriteTestSupport.Session(disk.File, out var document);
        var first = session.QueryCollection(HeldId, Editable, expectedRevision: null, cancellationToken: TestContext.Current.CancellationToken);
        var fruit = Assert.Single(first.Rows);
        var fruitQuantity = Assert.IsType<FieldNode>(fruit.Cells["quantity"]);
        ActionScope Scope(params string[] exclusions) => new()
        {
            QueryScope = new(HeldId, Editable with { Offset = 1 }, session.State.Revision) { ExcludedRowKeys = exclusions },
        };
        var state = session.State;
        var preview = session.PlanAction(RefillId, NoParameters, Scope(), expectedRevision: null, cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotNull(preview.PlanId);
        Assert.Equal(2, preview.Scope.RowKeys!.Count);
        Assert.Equal(0, preview.Scope.QueryScope!.Query.Offset);
        Assert.Equal(2, preview.Plan.Changes.Count);
        Assert.Empty(preview.Plan.SkippedRows);
        Assert.Equal(state, session.State);
        WriteTestSupport.EqualBytes(fixture.Save.Bytes, document);
        var excluded = session.PlanAction(RefillId, NoParameters, Scope(fruit.Key), expectedRevision: null, cancellationToken: TestContext.Current.CancellationToken);
        var stoneKey = Assert.Single(excluded.Scope.RowKeys!);
        Assert.NotEqual(fruit.Key, stoneKey);
        Assert.Equal(stoneKey + "/quantity", Assert.Single(excluded.Plan.Changes).ValueId);
        Assert.Empty(excluded.Plan.SkippedRows);
        var allExcluded = session.PlanAction(RefillId, NoParameters, Scope(fruit.Key, stoneKey), expectedRevision: null, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Empty(allExcluded.Scope.RowKeys!);
        Assert.Empty(allExcluded.Plan.Changes);
        Assert.Empty(allExcluded.Plan.SkippedRows);
        Assert.Equal(state, session.State);

        excluded = session.PlanAction(RefillId, NoParameters, Scope(fruit.Key), expectedRevision: null, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(session.ApplyActionPlan(excluded.PlanId!, excluded.Revision, cancellationToken: TestContext.Current.CancellationToken).Ok);
        Assert.Equal(2U, document.GetValue(fruitQuantity.ValueId).AsUInt32());
        Assert.Equal(3U, document.GetValue(stoneKey + "/quantity").AsUInt32());
        Assert.True(session.Undo().Ok);
        WriteTestSupport.EqualBytes(fixture.Save.Bytes, document);
        preview = session.PlanAction(RefillId, NoParameters, Scope(), expectedRevision: null, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(session.ApplyActionPlan(preview.PlanId!, preview.Revision, cancellationToken: TestContext.Current.CancellationToken).Ok);
        var expected = fixture.Save.Bytes.ToArray();
        var entries = SemanticReader.Read(fixture.Save.Bytes, EldenRingParser.Parse(fixture.Save.Bytes).Slots[0])
            .Containers.Single(container => container.Id == "held").Entries;
        var fruitEntry = entries.Single(entry => entry.RawId == "0x400050F0");
        var stoneEntry = entries.Single(entry => entry.RawId == "0x40002774");
        BinaryPrimitives.WriteUInt32LittleEndian(expected.AsSpan(fruitEntry.Offset + sizeof(uint)), 12);
        BinaryPrimitives.WriteUInt32LittleEndian(expected.AsSpan(stoneEntry.Offset + sizeof(uint)), 3);
        WriteTestSupport.EqualBytes(expected, document);
        WriteTestSupport.OnlyRanges(fixture.Save.Bytes, expected,
            (fruitEntry.Offset + sizeof(uint), sizeof(uint)), (stoneEntry.Offset + sizeof(uint), sizeof(uint)));
        var ceilingPage = session.QueryCollection(HeldId, Editable with { Limit = 2 }, expectedRevision: null, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(preview.Scope.RowKeys!.Order(StringComparer.Ordinal), ceilingPage.Rows.Select(row => row.Key).Order(StringComparer.Ordinal));
        var atCeiling = session.PlanAction(RefillId, NoParameters, Scope(), expectedRevision: null, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Empty(atCeiling.Plan.Changes);
        Assert.Equal(2, atCeiling.Plan.SkippedRows.Count);
        Assert.All(atCeiling.Plan.SkippedRows, row => Assert.Equal("Already at the conservative observed ceiling.", row.Reason));
        var editedState = session.State;
        Assert.True(session.ApplyActionPlan(atCeiling.PlanId!, atCeiling.Revision, cancellationToken: TestContext.Current.CancellationToken).Ok);
        Assert.Equal(editedState, session.State);
        Assert.False(session.ApplyEdits([WriteTestSupport.Incoming(fruitQuantity.ValueId, 13)], expectedRevision: null, cancellationToken: TestContext.Current.CancellationToken).Ok);
        var protectedRow = Assert.Single(session.QueryCollection(HeldId, new() { Text = "0x4000FFFF" }, expectedRevision: null, cancellationToken: TestContext.Current.CancellationToken).Rows);
        Assert.False(session.ApplyEdits([WriteTestSupport.Incoming(Assert.IsType<FieldNode>(protectedRow.Cells["quantity"]).ValueId, 2)], expectedRevision: null, cancellationToken: TestContext.Current.CancellationToken).Ok);
        Assert.Equal(editedState, session.State);
        WriteTestSupport.EqualBytes(expected, document);
        using var reopened = WriteTestSupport.Reopen(document);
        Assert.Equal(2, ((IQueryableDocument)reopened).Query(HeldId, Editable).FilteredCount);
        Assert.True(session.Undo().Ok);
        WriteTestSupport.EqualBytes(fixture.Save.Bytes, document);
        Assert.False(session.State.CanUndo);
        Assert.False(session.State.IsDirty);
        Assert.True(session.Redo().Ok);
        WriteTestSupport.EqualBytes(expected, document);
    }

    private static SemanticFixture Fixture()
    {
        var fixture = new SemanticFixture();
        fixture.Row("held", "common", 0, 0xB000FFFF, 25, 1);
        fixture.Row("held", "common", 1, 0xB000FFFE, 4, 2);
        fixture.Row("held", "common", 1800, 0xB00050F0, 2, 3);
        fixture.Row("held", "common", 2200, 0xB0002774, 1, 4);
        fixture.Row("storage", "common", 0, 0xB00050F0, 1, 5);
        return fixture;
    }
}
