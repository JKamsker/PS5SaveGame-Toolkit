using System.Buffers.Binary;
using System.Text.Json;
using SaveToolkit.Core;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;
using static SaveToolkit.Plugins.GodOfWarRagnarok.Tests.RagnarokRestoreFixture;

namespace SaveToolkit.Plugins.GodOfWarRagnarok.Tests;

public sealed class RagnarokRestoreQueryTests
{
    [Fact]
    public void WholeQueryRestoresUnloadedMatchesRetainingFiltersSortExclusionsAndGroupedHistory()
    {
        var original = RagnarokPerformanceFixture.Bytes();
        var file = ImportFile(original);
        var plugin = new GodOfWarRagnarokPlugin();
        using var document = (RagnarokDocument)plugin.Open(file);
        // Stage only bindings, without loading their table pages. Row 8 will be excluded;
        // row 9 is a different ID outside the query. Only rows 4 and 14 should restore.
        int[] reducedRows = [4, 8, 14, 9];
        var fields = reducedRows.Select(row => document.CounterField(QuantityOffset(row))).ToArray();
        Assert.True(document.ApplyEdits(fields.Select((field, i) => new ValueEdit(field.ValueId, FieldValue.Of((long)i + 1))).ToArray()).Succeeded);
        var reduced = original.ToArray();
        for (var i = 0; i < reducedRows.Length; i++) WriteQuantity(reduced, reducedRows[i], (uint)i + 1);
        using var session = new DocumentSession("query-restore", file, plugin, document);
        _ = session.BuildView();
        var loaded = Assert.Single(session.QueryCollection(CollectionId, new() { Limit = 1 }, expectedRevision: null,
            cancellationToken: TestContext.Current.CancellationToken).Rows);
        Assert.DoesNotContain(loaded.Key, reducedRows.Select(LargeRowKey));
        Assert.Equal(reduced, Serialize(document));

        var filters = new Dictionary<string, string> { ["id"] = "9E5240D27406E85A" };
        string[] excluded = [LargeRowKey(8), "not/a/matching/row?value=%26&other=1"];
        var scope = new ActionScope
        {
            QueryScope = new(CollectionId, new()
            {
                Text = "Hacksilver", Filters = filters, SortColumn = "quantity", Descending = true,
                IncludeEmpty = true, Offset = 77, Cursor = "reset transport paging", Limit = 1,
            }, session.State.Revision) { ExcludedRowKeys = excluded },
        };
        // Exercise the real wire vocabulary, preserving opaque slash/percent/ampersand keys.
        var wireScope = JsonSerializer.Deserialize<ActionScope>(JsonSerializer.Serialize(scope, ToolkitJson.Options), ToolkitJson.Options)!;
        Assert.Equal(excluded, wireScope.QueryScope!.ExcludedRowKeys);
        Assert.Equal(filters["id"], Assert.Single(wireScope.QueryScope.Query.Filters).Value);
        var preview = session.PlanAction(ActionId, Parameters, scope, session.State.Revision,
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotNull(preview.PlanId);
        Assert.Equal(RagnarokPerformanceFixture.RowCount / 2 - 1, preview.Scope.RowKeys!.Count);
        Assert.Contains(LargeRowKey(4), preview.Scope.RowKeys);
        Assert.Contains(LargeRowKey(14), preview.Scope.RowKeys);
        Assert.DoesNotContain(LargeRowKey(8), preview.Scope.RowKeys);
        Assert.DoesNotContain(LargeRowKey(9), preview.Scope.RowKeys);
        Assert.Equal(new[] { fields[0].ValueId, fields[2].ValueId }, preview.Plan.Changes.Select(change => change.ValueId));
        Assert.Equal(preview.Scope.RowKeys.Count - 2, preview.Plan.SkippedRows.Count);
        var retained = preview.Scope.QueryScope!;
        Assert.Equal(CollectionId, retained.CollectionId);
        Assert.Equal(session.State.Revision, retained.Revision);
        Assert.Equal("Hacksilver", retained.Query.Text);
        Assert.Equal(filters["id"], Assert.Single(retained.Query.Filters).Value);
        Assert.Equal("id", Assert.Single(retained.Query.Filters).Key);
        Assert.Equal("quantity", retained.Query.SortColumn);
        Assert.True(retained.Query.Descending);
        Assert.True(retained.Query.IncludeEmpty);
        Assert.Equal(0, retained.Query.Offset);
        Assert.Null(retained.Query.Cursor);
        Assert.Equal(excluded, retained.ExcludedRowKeys);
        Assert.Equal(reduced, Serialize(document));

        // Mutating the caller's request cannot change the retained preview/apply selection.
        filters["id"] = "no match";
        excluded[0] = LargeRowKey(4);
        Assert.True(session.ApplyActionPlan(preview.PlanId, preview.Revision, TestContext.Current.CancellationToken).Ok);
        var restored = reduced.ToArray();
        foreach (var row in new[] { 4, 14 }) WriteQuantity(restored, row, RagnarokPerformanceFixture.Quantity(row));
        Assert.Equal(restored, Serialize(document));
        Assert.True(session.Undo().Ok);
        Assert.False(session.State.CanUndo);
        Assert.Equal(reduced, Serialize(document));
        Assert.True(session.Redo().Ok);
        Assert.Equal(restored, Serialize(document));
    }

    [Fact]
    public void QueryRevisionAndPreviewRevisionBothRemainRequired()
    {
        using var fixture = new RagnarokRestoreFixture();
        fixture.Reduce();
        var scope = new ActionScope { QueryScope = new(CollectionId, new() { Text = "Hacksilver" }, fixture.Session.State.Revision) };
        var preview = fixture.Session.PlanAction(ActionId, Parameters, scope, fixture.Session.State.Revision,
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotNull(preview.PlanId);
        Assert.True(fixture.Session.ApplyEdits([Incoming(ValueId(1), 0)], expectedRevision: null,
            cancellationToken: TestContext.Current.CancellationToken).Ok);
        var changed = fixture.Expected(1, 0, 3);
        Assert.True(fixture.Session.ApplyActionPlan(preview.PlanId, preview.Revision, TestContext.Current.CancellationToken).Stale);
        var staleQuery = fixture.Session.PlanAction(ActionId, Parameters, scope, fixture.Session.State.Revision,
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(staleQuery.Stale);
        Assert.Null(staleQuery.PlanId);
        Assert.Equal(changed, fixture.Serialized());

        scope = scope with { QueryScope = scope.QueryScope! with { Revision = fixture.Session.State.Revision } };
        var refreshed = fixture.Session.PlanAction(ActionId, Parameters, scope, fixture.Session.State.Revision,
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotNull(refreshed.PlanId);
        Assert.True(fixture.Session.ApplyActionPlan(refreshed.PlanId, refreshed.Revision, TestContext.Current.CancellationToken).Ok);
        Assert.Equal(fixture.Expected(ImportedQuantity, ImportedQuantity, ImportedQuantity), fixture.Serialized());
        Assert.True(fixture.Session.Undo().Ok);
        Assert.Equal(changed, fixture.Serialized());
    }

    private static int QuantityOffset(int row) =>
        RagnarokPerformanceFixture.RowsOffset + row * RagnarokFormat.WalletRowSize + sizeof(ulong);

    private static void WriteQuantity(byte[] bytes, int row, uint value) =>
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(QuantityOffset(row)), value);

    private static string LargeRowKey(int row)
    {
        var id = RagnarokPerformanceFixture.Id(row);
        var occurrence = Enumerable.Range(0, row).Count(previous => RagnarokPerformanceFixture.Id(previous) == id);
        return $"wallet/00004895C643225F/resource/{id:X16}/{occurrence}";
    }

    private static byte[] Serialize(RagnarokDocument document) =>
        Assert.Single(document.Serialize()).Content.ToArray();
}
