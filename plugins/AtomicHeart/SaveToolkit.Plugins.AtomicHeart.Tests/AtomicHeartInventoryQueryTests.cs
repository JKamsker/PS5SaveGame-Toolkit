using System.Text.Json;
using SaveToolkit.Core.References;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Core.Storage;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.AtomicHeart.Tests;

public sealed class AtomicHeartInventoryQueryTests
{
    [Fact]
    public void InventoryQueriesUseLiveNamesNumericSortsAndExactFiltersBeforePaging()
    {
        using var document = WriteTestData.Plugin.Open(WriteTestData.Sample(), WriteTestData.References());
        var queryable = Assert.IsAssignableFrom<IQueryableDocument>(document);
        Assert.Equal(90, Assert.Single(queryable.Collections, c => c.Id == InventoryCollection.Id).TotalCount);
        var all = queryable.Query(InventoryCollection.Id, new() { IncludeEmpty = true, Limit = int.MaxValue, Offset = -10, SortColumn = "amount", Descending = true });
        Assert.Equal(90, all.Rows.Count);
        Assert.Equal(0, all.Offset);
        var amounts = all.Rows.Select(r => WriteTestData.Field(r, "amount").Value.AsInt64()).ToArray();
        Assert.Equal(amounts.OrderDescending(), amounts);
        Assert.Contains(all.Rows, r => Assert.IsType<TextNode>(r.Cells["category"]).Text == "consumable");
        Assert.Contains(all.Rows, r => Assert.IsType<TextNode>(r.Cells["category"]).Text == "quest item (inferred)");
        var query = new QueryableCollectionQuery { Text = "Swede", Limit = 1, Filters = new Dictionary<string, string> { ["category"] = "weapon" } };
        var before = Assert.Single(queryable.Query(InventoryCollection.Id, query).Rows);
        Assert.IsType<SectionNode>(before.Cells["details"]);
        var field = WriteTestData.Field(before, "asset");
        Assert.True(document.ApplyEdits([new(field.ValueId, FieldValue.Of("DA_Item_ShotgunKS23"))]).Succeeded);
        Assert.Empty(queryable.Query(InventoryCollection.Id, query).Rows);
        var after = queryable.Query(InventoryCollection.Id, query with { Text = "KS-23" });
        Assert.Equal(2, after.FilteredCount);
        Assert.Contains(queryable.Query(InventoryCollection.Id, query with { Text = "KS-23", Limit = 20 }).Rows, r => r.Key == before.Key);
        Assert.True(document.ApplyEdits([new(field.ValueId, field.Value)]).Succeeded);
        Assert.Equal(before.Key, Assert.Single(queryable.Query(InventoryCollection.Id, query).Rows).Key);
        Assert.Throws<ArgumentException>(() => queryable.Query(InventoryCollection.Id, new() { SortColumn = "missing" }));
        Assert.Throws<ArgumentException>(() => queryable.Query(InventoryCollection.Id, new() { Cursor = "bad" }));
        Assert.Throws<ArgumentException>(() => queryable.Query(InventoryCollection.Id, new() { Filters = new Dictionary<string, string> { ["missing"] = "bad" } }));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(false)]
    [InlineData(true)]
    public void WholeQueryPreviewIncludesUnloadedRowsHonorsExclusionsAndUndoesAsOneGroup(bool? includeEmpty)
    {
        var file = Assert.IsType<DiskSaveFile>(WriteTestData.Sample());
        var references = Assert.IsType<ReferenceContext>(WriteTestData.References());
        var document = WriteTestData.Plugin.Open(file, references);
        using var session = new DocumentSession("inventory-query", file, WriteTestData.Plugin, document, references: references);
        _ = session.BuildView();
        var first = session.QueryCollection(InventoryCollection.Id, new() { Limit = 1 });
        var excluded = Assert.Single(first.Rows).Key;
        var scope = new ActionScope { QueryScope = new(InventoryCollection.Id, new() { IncludeEmpty = includeEmpty }, session.State.Revision) { ExcludedRowKeys = [excluded] } };
        var parameters = new Dictionary<string, JsonElement> { ["amount"] = JsonSerializer.SerializeToElement(0) };
        var preview = session.PlanAction(InventoryPlanning.ActionId, parameters, scope, session.State.Revision);
        Assert.NotNull(preview.PlanId);
        Assert.Equal(includeEmpty is true ? 89 : 10, preview.Scope.RowKeys!.Count);
        Assert.Equal(includeEmpty ?? false, preview.Scope.QueryScope!.Query.IncludeEmpty);
        Assert.Equal(includeEmpty is true ? 79 : 0, preview.Plan.SkippedRows.Count);
        Assert.NotEmpty(preview.Plan.Changes);
        Assert.DoesNotContain(preview.Plan.Changes, c => c.ValueId.StartsWith(excluded + "/", StringComparison.Ordinal));
        Assert.True(session.ApplyActionPlan(preview.PlanId!, session.State.Revision).Ok);
        var output = WriteTestData.Output(document);
        _ = WriteTestData.IndependentDecode(file.ReadAllBytes(), output);
        using (var reopened = WriteTestData.Open(output))
            foreach (var change in preview.Plan.Changes) Assert.Equal(change.NewValue, reopened.GetValue(change.ValueId));
        Assert.True(session.Undo().Ok);
        Assert.False(session.State.CanUndo);
        Assert.Equal(file.ReadAllBytes(), WriteTestData.Output(document));
        Assert.True(session.Redo().Ok);
        Assert.Equal(output, WriteTestData.Output(document));
        var emptyScope = scope with { QueryScope = scope.QueryScope! with { Query = new() { Text = "no-such-asset" }, Revision = session.State.Revision } };
        var none = session.PlanAction(InventoryPlanning.ActionId, parameters, emptyScope, session.State.Revision);
        Assert.Empty(none.Scope.RowKeys!);
        Assert.Empty(none.Plan.Changes);
        var planner = Assert.IsAssignableFrom<IActionPlanningDocument>(document);
        Assert.NotEmpty(planner.PlanAction(InventoryPlanning.ActionId, new Dictionary<string, FieldValue> { ["amount"] = FieldValue.Of(0) }, scope).Errors);
        var foreign = new ActionScope([]) { QueryScope = new(RecordCollection.Id, new(), session.State.Revision) };
        Assert.NotEmpty(planner.PlanAction(InventoryPlanning.ActionId, new Dictionary<string, FieldValue> { ["amount"] = FieldValue.Of(0) }, foreign).Errors);
    }

    [Fact]
    public void AuthoredLargeInventoryPagesAndSearchAreBoundedAndStable()
    {
        var properties = Enumerable.Range(0, 450).Select(i => Element(i)).ToArray();
        var slots = new TaggedProperty("Slots", "ArrayProperty", "StructProperty", 0, default, default, null) { Children = properties };
        var list = new TaggedProperty("ResourcesList", "StructProperty", "InventoryList", 0, default, default, null) { Children = [slots] };
        var record = new ObjectRecord("synthetic", RecordKinds.Component, "player", "Inventory", InventoryClasses.Player, "", default, default, [], [list], default, []);
        var state = new InventoryState(new([], [], [record], []), null);
        var views = new InventoryViews(state, (id, label, spec, get, _, _) => new(id, label, spec, get()));
        var collection = new InventoryCollection(state, views);
        var page = collection.Query(new() { Limit = int.MaxValue, SortColumn = "instance" });
        Assert.Equal(450, page.TotalCount);
        Assert.Equal(200, page.Rows.Count);
        var tail = collection.Query(new() { Text = "Synthetic449", Limit = 1 });
        Assert.Equal(1, tail.FilteredCount);
        Assert.Equal("449", Assert.IsType<TextNode>(Assert.Single(tail.Rows).Cells["instance"]).Text);
        var next = collection.Query(new() { Offset = 200, Limit = 200, SortColumn = "instance" });
        var last = collection.Query(new() { Offset = 400, Limit = 200, SortColumn = "instance" });
        var pages = new[] { page, next, last };
        Assert.Equal(200, next.Rows.Count);
        Assert.Equal(50, last.Rows.Count);
        Assert.Equal(0, page.Offset);
        Assert.Equal(200, next.Offset);
        Assert.Equal(400, last.Offset);
        Assert.All(pages, result =>
        {
            Assert.Equal(450, result.TotalCount);
            Assert.Equal(450, result.FilteredCount);
            Assert.Equal(200, result.Limit);
            Assert.Equal(0, result.HiddenEmptyCount);
        });
        var rows = pages.SelectMany(result => result.Rows).ToArray();
        Assert.Equal(450, rows.Select(row => row.Key).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(Enumerable.Range(0, 450).Select(id => id.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            rows.Select(row => Assert.IsType<TextNode>(row.Cells["instance"]).Text));
        var beyond = collection.Query(new() { Offset = 450, Limit = 200, SortColumn = "instance" });
        Assert.Empty(beyond.Rows);
        Assert.Equal(450, beyond.FilteredCount);
        Assert.Equal(450, beyond.Offset);
    }

    private static TaggedProperty Element(int id)
    {
        TaggedProperty Scalar(string name, string type, FieldValue value, int offset) => new(name, type, "", 0, new(offset, 4), new(offset, 4), value);
        return new(id.ToString(), "StructElement", "", 0, default, default, null) { Children = [
            Scalar("ItemDataAsset", "ObjectProperty", FieldValue.Of($"Synthetic{id}"), id * 3),
            Scalar("Amount", "IntProperty", FieldValue.Of(id + 1), id * 3 + 1),
            Scalar("ItemId", "Int64Property", FieldValue.Of(id), id * 3 + 2),
        ] };
    }

    [Theory]
    [InlineData("None", "Empty slot")]
    [InlineData("DA_Item_Synthetic_Material_Resource", "Synthetic Material Resource")]
    [InlineData("DA_Consumable_aid_small", "Consumable aid small")]
    [InlineData("DA_UnknownThing", "Unknown Thing")]
    public void IdentifierFormattingNeverNeedsAnInventedLocalizationMapping(string id, string expected) =>
        Assert.Equal(expected, InventoryLabels.Name(id));
}
