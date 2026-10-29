using System.Globalization;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.AtomicHeart.Tests;

public sealed class AtomicHeartQuantityStateTests
{
    [Fact]
    public void MixedStatesFilterSearchAndSortTheCompleteModelBeforePaging()
    {
        var state = PerformanceInventory.Create(600);
        var collection = PerformanceInventory.Collection(state);
        var reduced = state.Items.Take(250).ToArray();
        foreach (var item in reduced) state.Set(item.Quantity, FieldValue.Of(0));
        foreach (var item in state.Items.Skip(250).Take(100)) state.Set(item.Quantity, FieldValue.Of(600));
        var query = Filter("rEdUcEd SiNcE iMpOrT") with { SortColumn = "quantityState", Descending = true, Limit = int.MaxValue };
        var first = collection.Query(query);
        var last = collection.Query(query with { Offset = 200 });
        Assert.Equal(200, first.Rows.Count);
        Assert.Equal(50, last.Rows.Count);
        Assert.All(new[] { first, last }, page =>
        {
            Assert.Equal(600, page.TotalCount);
            Assert.Equal(250, page.FilteredCount);
            Assert.Equal(0, page.HiddenEmptyCount);
            Assert.All(page.Rows, row => Assert.Equal("Reduced since import", State(row)));
        });
        Assert.Equal(reduced.Select(item => item.Key).Order(StringComparer.Ordinal),
            first.Rows.Concat(last.Rows).Select(row => row.Key));
        Assert.Equal(first.Rows.Select(row => row.Key), collection.Query(query with { Descending = false }).Rows.Select(row => row.Key));
        Assert.Equal(250, collection.Query(new() { Text = "REDUCED SINCE IMPORT" }).FilteredCount);
        Assert.Equal(100, collection.Query(Filter("Increased since import")).FilteredCount);
        Assert.Equal(250, collection.Query(Filter("Unchanged")).FilteredCount);
        Assert.Empty(collection.Query(Filter("Reduced")).Rows); // Exact filters, substring search.
        var combined = collection.Query(query with { Text = "Synthetic249" });
        Assert.Equal(reduced[^1].Key, Assert.Single(combined.Rows).Key);
        Assert.Equal(1, combined.FilteredCount);
        var ascending = collection.Query(new() { SortColumn = "quantityState", Limit = 1 });
        Assert.Equal("Increased since import", State(Assert.Single(ascending.Rows)));
        var descending = collection.Query(new() { SortColumn = "quantityState", Descending = true, Limit = 1 });
        Assert.Equal("Unchanged", State(Assert.Single(descending.Rows)));
        var numeric = collection.Query(Filter("Reduced since import") with { SortColumn = "instance", Descending = true, Limit = 1 });
        Assert.Equal(reduced[^1].Key, Assert.Single(numeric.Rows).Key);
    }

    [Theory]
    [InlineData(long.MaxValue, long.MinValue, "Reduced since import")]
    [InlineData(long.MinValue, long.MaxValue, "Increased since import")]
    [InlineData(long.MinValue, long.MinValue, "Unchanged")]
    [InlineData(long.MaxValue, long.MaxValue, "Unchanged")]
    public void ComparisonDoesNotSubtractOrOverflowSignedQuantities(long opening, long current, string expected)
    {
        var bytes = InventoryFixture.Create(amountType: "Int64Property", firstAmount: opening);
        var state = new InventoryState(SlotDataDecoder.Decode(bytes, SaveExtensionFile.Parse(bytes)), null);
        var item = state.Items[0];
        // Exercise the comparison independently of the narrower public write policy.
        state.Set(item.Quantity, FieldValue.Of(current));
        var row = Assert.Single(PerformanceInventory.Collection(state).Query(Filter(expected) with { Text = "Shved" }).Rows);
        Assert.Equal(item.Key, row.Key);
        Assert.Equal(expected, State(row));
        Assert.Equal(opening, item.Amount);
        Assert.Contains(Details(row), text => text.StartsWith($"Opening Amount (this import): {opening.ToString(CultureInfo.InvariantCulture)}.", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ReadOnlyQueriesPreserveBytesAndOpeningDetailsSurviveEditsAndReimport(bool withPack)
    {
        var bytes = InventoryFixture.Create(firstAmount: 4, secondAmount: 2);
        var references = withPack ? WriteTestData.References() : IReferenceContext.Empty;
        using var document = WriteTestData.Plugin.Open(MemorySaveSet.SingleFile("slot", bytes, "PPSA10695"), references);
        var queryable = Assert.IsAssignableFrom<IQueryableDocument>(document);
        var column = Assert.Single(Assert.Single(queryable.Collections, c => c.Id == InventoryCollection.Id).Columns,
            c => c.Key == InventoryCollection.QuantityStateColumn);
        Assert.True(Assert.IsType<TextSpec>(column.Spec).ReadOnly);
        var original = queryable.Query(InventoryCollection.Id, new() { SortColumn = "instance" }).Rows;
        Assert.All(original, row => Assert.Equal("Unchanged", State(row)));
        Assert.Empty(queryable.Query(InventoryCollection.Id, Filter("Reduced since import")).Rows);
        Assert.Equal(bytes, WriteTestData.Output(document));
        var first = original[0];
        var second = original[1];
        Assert.True(document.ApplyEdits([
            new(first.Key + "/amount", FieldValue.Of(1)), new(second.Key + "/amount", FieldValue.Of(3)),
        ]).Succeeded);
        if (withPack)
            Assert.True(document.ApplyEdits([new(first.Key + "/asset", FieldValue.Of("DA_Item_ShotgunKS23"))]).Succeeded);
        var changed = WriteTestData.Output(document);
        var reduced = Assert.Single(queryable.Query(InventoryCollection.Id, Filter("Reduced since import")).Rows);
        Assert.Equal(first.Key, reduced.Key);
        Assert.Equal(1, WriteTestData.Field(reduced, "amount").Value.AsInt64());
        Assert.Contains(Details(reduced), text => text.StartsWith("Opening Amount (this import): 4.", StringComparison.Ordinal));
        Assert.Contains(Details(reduced), text => text.Contains("does not identify gameplay consumption or editing", StringComparison.Ordinal));
        Assert.Equal(second.Key, Assert.Single(queryable.Query(InventoryCollection.Id, Filter("Increased since import")).Rows).Key);
        Assert.Equal(changed, WriteTestData.Output(document));
        using var imported = WriteTestData.Open(changed);
        var importedQuery = Assert.IsAssignableFrom<IQueryableDocument>(imported);
        Assert.Empty(importedQuery.Query(InventoryCollection.Id, Filter("Reduced since import")).Rows);
        var importedRow = Assert.Single(importedQuery.Query(InventoryCollection.Id, Filter("Unchanged")).Rows, row => row.Key == first.Key);
        Assert.Contains(Details(importedRow), text => text.StartsWith("Opening Amount (this import): 1.", StringComparison.Ordinal));
        Assert.True(document.ApplyEdits(original.Select(row =>
            new ValueEdit(row.Key + "/amount", WriteTestData.Field(row, "amount").Value)).ToArray()).Succeeded);
        if (withPack) Assert.True(document.ApplyEdits([new(first.Key + "/asset", WriteTestData.Field(first, "asset").Value)]).Succeeded);
        Assert.Empty(queryable.Query(InventoryCollection.Id, Filter("Reduced since import")).Rows);
        Assert.Equal(bytes, WriteTestData.Output(document));
    }

    internal static QueryableCollectionQuery Filter(string state) => new() {
        Filters = new Dictionary<string, string> { [InventoryCollection.QuantityStateColumn] = state },
    };

    internal static string State(TableRow row) => Assert.IsType<TextNode>(row.Cells[InventoryCollection.QuantityStateColumn]).Text;
    internal static IEnumerable<string> Details(TableRow row) => Assert.IsType<SectionNode>(row.Cells["details"])
        .Children.OfType<TextNode>().Select(node => node.Text);
}
