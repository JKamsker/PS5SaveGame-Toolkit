using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Views;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.Witcher3.Tests;

[Collection(typeof(AllocationTestCollection))]
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

        var allocated = AllocationProbe.Measure(() => view.Query(id, query), out var repeated);
        Assert.Equal(first.Rows.Select(row => row.Key), repeated.Rows.Select(row => row.Key));
        allocated.AssertAtMost(maximumPageAllocation, $"{kind} allocated {allocated.MinimumBytes:N0} bytes for {pageSize} rows; budget {maximumPageAllocation:N0}.");

        var last = view.Query(id, query with { Offset = checked((int)first.TotalCount - pageSize) });
        Assert.Equal(pageSize, last.Rows.Count);
        Assert.DoesNotContain(last.Rows[0].Key, first.Rows.Select(row => row.Key));
        var identity = Assert.IsType<TextNode>(last.Rows[^1].Cells["id"]).Text;
        var exact = view.Query(id, query with { Filters = new Dictionary<string, string> { ["id"] = identity } });
        Assert.Equal(last.Rows[^1].Key, Assert.Single(exact.Rows).Key);
        allocated = AllocationProbe.Measure(() => view.Query(id, query with { Text = identity }), out var search);
        Assert.Equal(last.Rows[^1].Key, Assert.Single(search.Rows).Key);
        allocated.AssertAtMost(maximumPageAllocation, $"{kind} allocated {allocated.MinimumBytes:N0} bytes for its first text search over existing metadata.");
        var rowKind = Assert.IsType<TextNode>(last.Rows[^1].Cells["kind"]).Text;
        var acrossFields = view.Query(id, query with { Text = identity + "\n" + rowKind });
        Assert.Equal(last.Rows[^1].Key, Assert.Single(acrossFields.Rows).Key);
        if (kind != "recipes")
        {
            var details = Assert.IsType<SectionNode>(last.Rows[^1].Cells["details"]);
            Assert.NotNull(details.LazyId);
            Assert.NotEmpty(view.Expand(details.LazyId));
        }
    }
}
