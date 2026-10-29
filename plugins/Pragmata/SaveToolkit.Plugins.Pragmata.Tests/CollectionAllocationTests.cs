using SaveToolkit.Plugins.Pragmata.Format;
using SaveToolkit.Plugins.Pragmata.Views;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Views;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.Pragmata.Tests;

[Collection(typeof(AllocationTestCollection))]
public sealed class CollectionAllocationTests
{
    [Theory]
    [InlineData(false, "plain")]
    [InlineData(false, "search")]
    [InlineData(false, "sort")]
    [InlineData(true, "plain")]
    [InlineData(true, "search")]
    [InlineData(true, "sort")]
    public void RepeatedPagesAllocateOnlyRequestedViews(bool evidence, string mode)
    {
        using var document = new PragmataDocument("data.bin", SyntheticInventory.Create(10_000));
        _ = document.BuildView();
        var detailCalls = 0;
        var id = evidence ? document.Evidence.Table(document.Layout.Inventory!.Find(KnownHashes.Items)!,
            _ => { detailCalls++; return new TextNode("details"); }).CollectionId : InventoryCollections.CollectionId;
        var query = new QueryableCollectionQuery
        {
            Limit = 10,
            Text = mode == "search" ? "3" : null,
            SortColumn = mode == "sort" ? evidence ? "values" : "quantity" : null,
            Descending = mode == "sort",
        };
        for (var index = 0; index < 3; index++) _ = document.Query(id, query);
        var callsBefore = detailCalls;
        var allocated = AllocationProbe.Measure(() => document.Query(id, query with { Offset = 10, Limit = 7 }), out var page, beforeRun: () => callsBefore = detailCalls);
        Assert.Equal(7, page.Rows.Count);
        Assert.Equal(evidence ? 7 : 0, detailCalls - callsBefore);
        allocated.AssertLessThan(200_000, $"{mode} page allocated {allocated.MinimumBytes:N0} bytes (evidence={evidence}).");
    }

    [Fact]
    public void FirstEvidencePageDefersUnrequestedValueSummariesAndDetails()
    {
        PragmataDocument current = null!;
        var calls = 0;
        string collectionId = null!;
        var allocated = AllocationProbe.Measure(() => current.Query(collectionId, new() { Limit = 3 }), out var page,
            beforeRun: () =>
            {
                current?.Dispose();
                current = new PragmataDocument("data.bin", SyntheticInventory.Create(10_000));
                calls = 0;
                var array = current.Layout.Inventory!.Find(KnownHashes.Items)!;
                collectionId = current.Evidence.Table(array, _ => { calls++; return new TextNode("details"); }).CollectionId;
                Assert.Equal(0, calls);
            });
        using var document = current;
        Assert.Equal(3, calls);
        Assert.Equal(3, page.Rows.Count);
        allocated.AssertLessThan(6_000_000, $"First evidence page allocated {allocated.MinimumBytes:N0} bytes.");
        Assert.Empty(document.Query(collectionId, new() { Offset = int.MaxValue }).Rows);
        Assert.Equal(3, calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DifferentSearchesReuseProjectedValues(bool evidence)
    {
        PragmataDocument current = null!;
        string id = null!;
        var allocated = AllocationProbe.Measure(() => current.Query(id, new() { Text = "2", Limit = 1 }), out var result,
            beforeRun: () =>
            {
                current?.Dispose();
                current = new PragmataDocument("data.bin", SyntheticInventory.Create(10_000));
                id = evidence ? current.Evidence.Table(current.Layout.Inventory!.Find(KnownHashes.Items)!,
                    _ => new TextNode("details")).CollectionId : InventoryCollections.CollectionId;
                _ = current.Query(id, new() { Text = "3", Limit = 1 });
            });
        using var document = current;
        Assert.NotEmpty(result.Rows);
        allocated.AssertLessThan(1_000_000, $"Changing search allocated {allocated.MinimumBytes:N0} bytes (evidence={evidence}).");
    }

    [Fact]
    public void ReRegisteringEvidenceKeepsTheIndexButPublishesFreshDetails()
    {
        using var document = new PragmataDocument("data.bin", SyntheticInventory.Create(10_000));
        var array = document.Layout.Inventory!.Find(KnownHashes.Items)!;
        var table = document.Evidence.Table(array, _ => new TextNode("old details"));
        _ = document.Query(table.CollectionId, new());
        _ = document.Evidence.Table(array, _ => new TextNode("new details"));
        var allocated = AllocationProbe.Measure(() => document.Query(table.CollectionId, new() { Limit = 1 }), out var page,
            beforeRun: () => _ = document.Evidence.Table(array, _ => new TextNode("new details")));
        Assert.Equal("new details", Assert.IsType<TextNode>(Assert.Single(page.Rows).Cells["details"]).Text);
        allocated.AssertLessThan(200_000, $"Evidence registration rebuilt its index: {allocated.MinimumBytes:N0} bytes.");
    }
}
