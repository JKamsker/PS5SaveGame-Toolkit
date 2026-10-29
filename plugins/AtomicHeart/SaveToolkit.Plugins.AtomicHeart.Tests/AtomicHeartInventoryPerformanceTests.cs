using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.AtomicHeart.Tests;

[Collection(typeof(AllocationTestCollection))]
public sealed class AtomicHeartInventoryPerformanceTests
{
    [Theory]
    [InlineData("9999", null)]
    [InlineData(null, "asset")]
    [InlineData(null, "amount")]
    public void RepeatedLargeQueriesAvoidPerSlotLabelAndNumberStrings(string? text, string? sort)
    {
        var state = PerformanceInventory.Create(10000);
        var collection = PerformanceInventory.Collection(state);
        var query = new QueryableCollectionQuery { Text = text, SortColumn = sort, Limit = 1 };
        _ = collection.Query(query);
        var allocated = AllocationProbe.Measure(() => collection.Query(query), out var page);
        Assert.Single(page.Rows);
        allocated.AssertLessThan(450_000, $"Inventory query allocated {allocated.MinimumBytes:N0} bytes.");
    }

    [Fact]
    public void RepeatedSearchDoesNotResolveUnchangedAssetReferencesAgain()
    {
        var catalog = new CountingCatalog(WriteTestData.References().Catalog("items")!);
        var state = PerformanceInventory.Create(1000, catalog);
        var collection = PerformanceInventory.Collection(state);
        var query = new QueryableCollectionQuery { Text = "no matches" };
        Assert.Empty(collection.Query(query).Rows);
        var calls = catalog.FindCalls;
        Assert.Empty(collection.Query(query).Rows);
        Assert.Equal(calls, catalog.FindCalls);
        state.Set(state.Items[0].Asset, FieldValue.Of("/Game/New.Unknown"));
        Assert.Empty(collection.Query(query).Rows);
        Assert.Equal(calls + 1, catalog.FindCalls);
    }

    [Fact]
    public void SearchAndFiltersSeeAmountAssetAndEmptyChangesWithoutStaleMembership()
    {
        var state = PerformanceInventory.Create(450);
        var collection = PerformanceInventory.Collection(state);
        var item = state.Items[^1];
        var query = new QueryableCollectionQuery { Text = "Synthetic449", Limit = 1 };
        Assert.Equal(item.Key, Assert.Single(collection.Query(query).Rows).Key);
        state.Set(item.Asset, FieldValue.Of("/Game/Replacement.NewResource"));
        Assert.Empty(collection.Query(query).Rows);
        Assert.Equal(item.Key, Assert.Single(collection.Query(query with { Text = "New Resource" }).Rows).Key);
        state.Set(item.Quantity, FieldValue.Of(987654321));
        Assert.Equal(item.Key, Assert.Single(collection.Query(query with { Text = "765432" }).Rows).Key);
        Assert.Equal(item.Key, Assert.Single(collection.Query(new() { Filters = new Dictionary<string, string> { ["amount"] = "987654321" } }).Rows).Key);
        Assert.Empty(collection.Query(new() { Filters = new Dictionary<string, string> { ["amount"] = "0987654321" } }).Rows);
        Assert.Equal(item.Key, collection.Query(new() { SortColumn = "amount", Descending = true, Limit = 1 }).Rows[0].Key);
        state.Set(item.Asset, FieldValue.Of(item.RawAsset));
        state.Set(item.Quantity, FieldValue.Of(item.Amount));
        Assert.Equal(item.Key, Assert.Single(collection.Query(query).Rows).Key);
        var empty = state.Items[0];
        state.Set(empty.Asset, FieldValue.Of("None"));
        state.Set(empty.Quantity, FieldValue.Of(0));
        var hidden = collection.Query(new() { Text = "Empty slot" });
        Assert.Empty(hidden.Rows);
        Assert.Equal(1, hidden.HiddenEmptyCount);
        Assert.True(Assert.Single(collection.Query(new() { Text = "Empty slot", IncludeEmpty = true }).Rows).IsEmpty);
        Assert.Equal("0", Assert.IsType<TextNode>(collection.Query(new() { IncludeEmpty = true, SortColumn = "instance", Limit = 1 }).Rows[0].Cells["instance"]).Text);
    }

    private sealed class CountingCatalog(IReferenceCatalog inner) : IReferenceCatalog
    {
        internal int FindCalls { get; private set; }
        public string Id => inner.Id;
        public ReferencePackInfo Pack => inner.Pack;
        public ReferenceDatasetInfo Dataset => inner.Dataset;
        public int Count => inner.Count;
        public IReadOnlyList<string> Categories => inner.Categories;
        public ReferenceRecord? Find(string key) { FindCalls++; return inner.Find(key); }
        public ReferencePage Search(ReferenceQuery query) => inner.Search(query);
    }
}
