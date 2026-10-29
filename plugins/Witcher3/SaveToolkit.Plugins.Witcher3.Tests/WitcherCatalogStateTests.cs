using SaveToolkit.Core.References;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.Witcher3.Tests;

public sealed class WitcherCatalogStateTests
{
    [Theory]
    [InlineData("page")]
    [InlineData("search")]
    [InlineData("sort")]
    [InlineData("filter")]
    public void EachGwentQueryCapturesCurrentCardsOnceAndRefreshesAfterEdits(string mode)
    {
        var plugin = new Witcher3Plugin();
        var context = new ReferencePackStore([plugin]).Pin(plugin, "PPSA04018");
        var calls = 0;
        var copies = 1;
        var view = new CatalogViews(context, "sav3-64.23.163", () => {
            calls++;
            return [new GwentCard(108, copies)];
        });
        var query = new QueryableCollectionQuery {
            Text = mode == "search" ? "saved copies" : null,
            SortColumn = mode == "sort" ? "owned" : null,
            Filters = mode == "page" ? new Dictionary<string, string>() :
                mode == "filter" ? new Dictionary<string, string> { ["owned"] = "1 saved copies" } :
                new Dictionary<string, string> { ["raw"] = "108" },
            Limit = 5,
        };
        var first = view.Query("catalog/gwent", query);
        if (mode == "page") { Assert.Equal(query.Limit, first.Rows.Count); }
        else { Assert.Equal("1 saved copies", Assert.IsType<TextNode>(Assert.Single(first.Rows).Cells["owned"]).Text); }
        Assert.Equal(1, calls);

        copies = 2;
        var updated = view.Query("catalog/gwent", query with {
            Text = "2 saved copies", Filters = new Dictionary<string, string> { ["raw"] = "108" },
        });
        Assert.Equal("2 saved copies", Assert.IsType<TextNode>(Assert.Single(updated.Rows).Cells["owned"]).Text);
        Assert.Equal(2, calls);
        Assert.Empty(view.Query("catalog/gwent", query with { Text = "1 saved copies" }).Rows);
        Assert.Equal(3, calls);
        _ = view.Query("catalog/items", new() { Text = "bread", Limit = 5 });
        Assert.Equal(3, calls);
    }

    [Theory]
    [InlineData(false, "Collection unavailable")]
    [InlineData(true, "Not recorded; availability unknown")]
    public void MissingAndEmptyCollectionsRetainTheirDistinctMeanings(bool available, string expected)
    {
        var plugin = new Witcher3Plugin();
        var context = new ReferencePackStore([plugin]).Pin(plugin, "PPSA04018");
        var view = new CatalogViews(context, "sav3-64.23.163", () => available ? [] : null);
        var page = view.Query("catalog/gwent", new() { Filters = new Dictionary<string, string> { ["raw"] = "108" } });
        Assert.Equal(expected, Assert.IsType<TextNode>(Assert.Single(page.Rows).Cells["owned"]).Text);
    }
}
