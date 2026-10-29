using SaveToolkit.Plugins.Pragmata.Format;
using SaveToolkit.Plugins.Pragmata.Views;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.Pragmata.Tests;

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
        var start = GC.GetAllocatedBytesForCurrentThread();
        var page = document.Query(id, query with { Offset = 10, Limit = 7 });
        var allocated = GC.GetAllocatedBytesForCurrentThread() - start;
        Assert.Equal(7, page.Rows.Count);
        Assert.Equal(evidence ? 7 : 0, detailCalls - callsBefore);
        Assert.True(allocated < 200_000, $"{mode} page allocated {allocated:N0} bytes (evidence={evidence}).");
    }

    [Fact]
    public void FirstEvidencePageDefersUnrequestedValueSummariesAndDetails()
    {
        using var document = new PragmataDocument("data.bin", SyntheticInventory.Create(10_000));
        var calls = 0;
        var array = document.Layout.Inventory!.Find(KnownHashes.Items)!;
        var table = document.Evidence.Table(array, _ => { calls++; return new TextNode("details"); });
        Assert.Equal(0, calls);
        var start = GC.GetAllocatedBytesForCurrentThread();
        var page = document.Query(table.CollectionId, new() { Limit = 3 });
        var allocated = GC.GetAllocatedBytesForCurrentThread() - start;
        Assert.Equal(3, calls);
        Assert.Equal(3, page.Rows.Count);
        Assert.True(allocated < 6_000_000, $"First evidence page allocated {allocated:N0} bytes.");
        Assert.Empty(document.Query(table.CollectionId, new() { Offset = int.MaxValue }).Rows);
        Assert.Equal(3, calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DifferentSearchesReuseProjectedValues(bool evidence)
    {
        using var document = new PragmataDocument("data.bin", SyntheticInventory.Create(10_000));
        var id = evidence ? document.Evidence.Table(document.Layout.Inventory!.Find(KnownHashes.Items)!,
            _ => new TextNode("details")).CollectionId : InventoryCollections.CollectionId;
        _ = document.Query(id, new() { Text = "3", Limit = 1 });
        var start = GC.GetAllocatedBytesForCurrentThread();
        var result = document.Query(id, new() { Text = "2", Limit = 1 });
        var allocated = GC.GetAllocatedBytesForCurrentThread() - start;
        Assert.NotEmpty(result.Rows);
        Assert.True(allocated < 1_000_000, $"Changing search allocated {allocated:N0} bytes (evidence={evidence}).");
    }

    [Fact]
    public void ReRegisteringEvidenceKeepsTheIndexButPublishesFreshDetails()
    {
        using var document = new PragmataDocument("data.bin", SyntheticInventory.Create(10_000));
        var array = document.Layout.Inventory!.Find(KnownHashes.Items)!;
        var table = document.Evidence.Table(array, _ => new TextNode("old details"));
        _ = document.Query(table.CollectionId, new());
        _ = document.Evidence.Table(array, _ => new TextNode("new details"));
        var start = GC.GetAllocatedBytesForCurrentThread();
        var page = document.Query(table.CollectionId, new() { Limit = 1 });
        var allocated = GC.GetAllocatedBytesForCurrentThread() - start;
        Assert.Equal("new details", Assert.IsType<TextNode>(Assert.Single(page.Rows).Cells["details"]).Text);
        Assert.True(allocated < 200_000, $"Evidence registration rebuilt its index: {allocated:N0} bytes.");
    }
}
