using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.Witcher3.Tests;

public sealed class WitcherWorldQueryAllocationTests
{
    [Theory]
    [InlineData("recipes")]
    [InlineData("facts")]
    [InlineData("journal")]
    public void RepeatedSmallPagesReuseImmutableMetadataAndDeferOffPageViews(string kind)
    {
        const int pageSize = 5;
        const int maximumPageAllocation = 40_000;
        var file = EnabledWriteFixture.Files().MaxBy(file => file.Length)!;
        var (container, directory, tree, player, _) = EnabledWriteFixture.Read(file.ReadAllBytes());
        var view = new SavedWorldViews(new SavedWorldData(player, tree, container.Payload, directory.Names), null);
        var id = "saved/" + kind;
        var query = new QueryableCollectionQuery { Limit = pageSize };
        var first = view.Query(id, query);
        Assert.True(first.TotalCount > pageSize);
        _ = view.Query(id, query);

        var before = GC.GetAllocatedBytesForCurrentThread();
        var repeated = view.Query(id, query);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(first.Rows.Select(row => row.Key), repeated.Rows.Select(row => row.Key));
        Assert.True(allocated <= maximumPageAllocation,
            $"{kind} allocated {allocated:N0} bytes for {pageSize} rows; budget {maximumPageAllocation:N0}.");

        var last = view.Query(id, query with { Offset = checked((int)first.TotalCount - pageSize) });
        Assert.Equal(pageSize, last.Rows.Count);
        Assert.DoesNotContain(last.Rows[0].Key, first.Rows.Select(row => row.Key));
        var identity = Assert.IsType<TextNode>(last.Rows[^1].Cells["id"]).Text;
        var exact = view.Query(id, query with { Filters = new Dictionary<string, string> { ["id"] = identity } });
        Assert.Equal(last.Rows[^1].Key, Assert.Single(exact.Rows).Key);
        if (kind != "recipes")
        {
            var details = Assert.IsType<SectionNode>(last.Rows[^1].Cells["details"]);
            Assert.NotNull(details.LazyId);
            Assert.NotEmpty(view.Expand(details.LazyId));
        }
    }
}
