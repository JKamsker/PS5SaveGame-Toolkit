using System.Text.Json;
using SaveToolkit.Core;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;
using SaveToolkit.Testing.Diff;

namespace SaveToolkit.Plugins.AtomicHeart.Tests;

public sealed class AtomicHeartEmptySlotTests
{
    [Fact]
    public void SampleVacanciesAreHiddenByDefaultWithoutLosingIdentitiesOrRawValues()
    {
        var bytes = WriteTestData.Sample().ReadAllBytes();
        var state = new InventoryState(SlotDataDecoder.Decode(bytes, SaveExtensionFile.Parse(bytes)), null);
        var vacancies = state.Items.Where(item => item.RawAsset is "" or "None").ToArray();
        Assert.Equal(79, vacancies.Length);
        Assert.All(vacancies, item => Assert.Equal(0, item.Amount));
        Assert.Equal(10, vacancies.Count(item => item.InstanceId == 0));
        Assert.Equal(69, vacancies.Count(item => item.InstanceId == -1));
        using var document = WriteTestData.Plugin.Open(WriteTestData.Sample());
        var queryable = Assert.IsAssignableFrom<IQueryableDocument>(document);
        var tabs = Assert.IsType<TabsNode>(document.BuildView());
        var inventory = Assert.Single(tabs.Tabs, tab => tab.Title == "Inventory inspection");
        var table = Assert.Single(inventory.Children.OfType<QueryableTableNode>());
        Assert.DoesNotContain(inventory.Children, node => node is SectionNode or TableNode);
        var metadata = Assert.Single(queryable.Collections, collection => collection.Id == table.CollectionId);
        Assert.Equal(new EmptyRowPresentation("asset"), metadata.EmptyRows);
        Assert.Equal(metadata.EmptyRows, table.EmptyRows);
        var visible = queryable.Query(table.CollectionId, new() { Limit = 200 });
        Assert.Equal(90, visible.TotalCount);
        Assert.Equal(11, visible.FilteredCount);
        Assert.Equal(79, visible.HiddenEmptyCount);
        Assert.Equal(11, visible.Rows.Count);
        Assert.All(visible.Rows, row => Assert.False(row.IsEmpty));
        var all = queryable.Query(table.CollectionId, new() { IncludeEmpty = true, Limit = 200 });
        Assert.Equal(90, all.Rows.Count);
        Assert.Equal(90, all.FilteredCount);
        Assert.Equal(0, all.HiddenEmptyCount);
        Assert.Equal(state.Items.Select(item => item.Key).Order(), all.Rows.Select(row => row.Key).Order());
        Assert.Equal(vacancies.Select(item => item.Key).Order(), all.Rows.Where(row => row.IsEmpty).Select(row => row.Key).Order());
        foreach (var row in all.Rows)
        {
            _ = JsonSerializer.Serialize(row, ToolkitJson.Options);
            foreach (var field in row.Cells.Values.OfType<FieldNode>()) Assert.Equal(field.Value, document.GetValue(field.ValueId));
            if (!row.IsEmpty) continue;
            Assert.Equal("None", WriteTestData.Field(row, "asset").Value.AsString());
            Assert.Equal(0, WriteTestData.Field(row, "amount").Value.AsInt64());
            Assert.Equal("Empty slot", InventoryLabels.Name(WriteTestData.Field(row, "asset").Value.AsString()));
            Assert.True(WriteTestData.Field(row, "amount").Spec.ReadOnly);
            Assert.IsType<SectionNode>(row.Cells["details"]);
        }
        Assert.Equal(bytes, WriteTestData.Output(document));
    }

    [Fact]
    public void EditingAVisibleSlotChangesOnlyItsQuantityAndDoesNotHideItsAsset()
    {
        var file = WriteTestData.Sample();
        var bytes = file.ReadAllBytes();
        var index = SlotDataDecoder.Decode(bytes, SaveExtensionFile.Parse(bytes));
        var state = new InventoryState(index, null);
        using var document = WriteTestData.Plugin.Open(file);
        var queryable = Assert.IsAssignableFrom<IQueryableDocument>(document);
        var page = queryable.Query(InventoryCollection.Id, new());
        var row = page.Rows.First(candidate => !WriteTestData.Field(candidate, "amount").Spec.ReadOnly);
        var item = Assert.Single(state.Items, candidate => candidate.Key == row.Key);
        var field = WriteTestData.Field(row, "amount");
        var result = document.ApplyEdits([new(field.ValueId, FieldValue.Of(0))]);
        Assert.True(result.Succeeded);
        Assert.False(result.StructureChanged);
        var updated = queryable.Query(InventoryCollection.Id, new());
        Assert.Equal(page.HiddenEmptyCount, updated.HiddenEmptyCount);
        Assert.False(Assert.Single(updated.Rows, candidate => candidate.Key == row.Key).IsEmpty);
        var output = WriteTestData.Output(document);
        var plain = WriteTestData.IndependentDecode(bytes, output);
        var diff = ByteDiffer.Compare(index.Plain, plain, options: new() { MergeGap = 0 });
        Assert.True(diff.HasChanges);
        Assert.False(diff.Truncated);
        Assert.All(diff.Ranges, range => Assert.True(range.Offset >= item.Quantity.Payload.Offset && range.End <= item.Quantity.Payload.End));
        using var reopened = WriteTestData.Open(output);
        var saved = Assert.Single(WriteTestData.Table(reopened).Rows, candidate => candidate.Key == row.Key);
        Assert.False(saved.IsEmpty);
        Assert.Equal(0, WriteTestData.Field(saved, "amount").Value.AsInt64());
        Assert.True(document.ApplyEdits([new(field.ValueId, field.Value)]).Succeeded);
        Assert.Equal(bytes, WriteTestData.Output(document));
    }

    [Theory]
    [InlineData("None", 0, -1, true)]
    [InlineData("None", 0, 0, true)]
    [InlineData("None", 1, -1, false)]
    [InlineData("None", 0, 17, false)]
    [InlineData("", 0, -1, false)]
    [InlineData("DA_Unknown", 0, -1, false)]
    [InlineData("DA_Unknown", 0, 0, false)]
    public void OnlyTheConfirmedVacancyPatternIsFlagged(string asset, int amount, int instance, bool empty)
    {
        var (state, collection) = Collection([Element(0, asset, amount, instance)]);
        var row = Assert.Single(collection.Query(new() { IncludeEmpty = true }).Rows);
        Assert.Equal(empty, row.IsEmpty);
        Assert.Equal(empty, state.IsEmpty(Assert.Single(state.Items)));
        Assert.Equal(empty ? 0 : 1, collection.Query(new()).Rows.Count);
        Assert.Equal(empty ? 1 : 0, collection.Query(new()).HiddenEmptyCount);
    }

    [Fact]
    public void VisibilityAndMatchingCountsAreComputedBeforePagingAndSorting()
    {
        var (_, collection) = Collection(Enumerable.Range(0, 450)
            .Select(id => id % 3 == 0 ? Element(id, "None", 0, -1) : Element(id, $"Synthetic{id}", id, id)).ToArray());
        var query = new QueryableCollectionQuery { Limit = 127, SortColumn = "instance", Descending = true };
        var first = collection.Query(query);
        var second = collection.Query(query with { Offset = 127 });
        var third = collection.Query(query with { Offset = 254 });
        Assert.Equal(450, first.TotalCount);
        Assert.Equal(300, first.FilteredCount);
        Assert.Equal(150, first.HiddenEmptyCount);
        Assert.Equal(150, third.HiddenEmptyCount);
        var rows = first.Rows.Concat(second.Rows).Concat(third.Rows).ToArray();
        Assert.Equal(300, rows.Length);
        Assert.Equal(300, rows.Select(row => row.Key).Distinct().Count());
        Assert.All(rows, row => Assert.False(row.IsEmpty));
        var instances = rows.Select(row => int.Parse(Assert.IsType<TextNode>(row.Cells["instance"]).Text)).ToArray();
        Assert.Equal(instances.OrderDescending(), instances);
        var emptyQuery = query with { Text = "Empty slot" };
        var hidden = collection.Query(emptyQuery);
        Assert.Empty(hidden.Rows);
        Assert.Equal(0, hidden.FilteredCount);
        Assert.Equal(150, hidden.HiddenEmptyCount);
        var shown = collection.Query(emptyQuery with { IncludeEmpty = true });
        Assert.Equal(127, shown.Rows.Count);
        Assert.Equal(150, shown.FilteredCount);
        Assert.Equal(0, shown.HiddenEmptyCount);
        Assert.All(shown.Rows, row => Assert.True(row.IsEmpty));
        var exact = collection.Query(query with { Filters = new Dictionary<string, string> { ["category"] = "empty" }, Offset = 200 });
        Assert.Empty(exact.Rows);
        Assert.Equal(150, exact.HiddenEmptyCount);
        Assert.Equal(0, collection.Query(query with { Text = "no match" }).HiddenEmptyCount);
        Assert.Equal(450, collection.Query(query with { IncludeEmpty = true }).FilteredCount);
    }

    private static (InventoryState State, InventoryCollection Collection) Collection(TaggedProperty[] elements)
    {
        var slots = new TaggedProperty("Slots", "ArrayProperty", "StructProperty", 0, default, default, null) { Children = elements };
        var list = new TaggedProperty("ResourcesList", "StructProperty", "InventoryList", 0, default, default, null) { Children = [slots] };
        var record = new ObjectRecord("authored", RecordKinds.Component, "player", "Inventory", InventoryClasses.Player, "",
            default, default, [], [list], default, []);
        var state = new InventoryState(new([], [], [record], []), null);
        var views = new InventoryViews(state, (id, label, spec, get, _, _) => new(id, label, spec, get()));
        return (state, new(state, views));
    }

    private static TaggedProperty Element(int slot, string asset, int amount, int instance)
    {
        TaggedProperty Scalar(string name, string type, FieldValue value, int offset) =>
            new(name, type, "", 0, new(offset, 4), new(offset, 4), value);
        return new(slot.ToString(), "StructElement", "", 0, default, default, null) { Children = [
            Scalar("ItemDataAsset", "ObjectProperty", FieldValue.Of(asset), slot * 3),
            Scalar("Amount", "IntProperty", FieldValue.Of(amount), slot * 3 + 1),
            Scalar("ItemId", "Int64Property", FieldValue.Of(instance), slot * 3 + 2),
        ] };
    }
}
