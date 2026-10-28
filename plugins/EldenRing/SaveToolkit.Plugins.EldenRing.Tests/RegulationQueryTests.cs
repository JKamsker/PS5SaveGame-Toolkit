using SaveToolkit.Core.Sessions;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.EldenRing.Tests;

public sealed class RegulationQueryTests
{
    [Fact]
    public void SampleDirectoryIsLazyQueryableNumericSortableAndPreservesRuneUndo()
    {
        var file = WriteTestSupport.Sample();
        var original = file.ReadAllBytes();
        var browser = new RegulationViews(original);
        Assert.Empty(browser.Collections); // No decode until inspected/queried.
        var view = Assert.Single(browser.BuildView().OfType<QueryableTableNode>());
        Assert.Equal(186, view.TotalCount);
        Assert.Equal(186, Assert.Single(browser.Collections).TotalCount);
        var keys = new HashSet<string>();
        for (var offset = 0; offset < 186; offset += 50)
        {
            var page = browser.Query(new() { Offset = offset, Limit = 50, SortColumn = "rows", Descending = true });
            Assert.Equal(186, page.TotalCount);
            Assert.InRange(page.Rows.Count, 1, 50);
            Assert.All(page.Rows, row => Assert.True(keys.Add(row.Key)));
            var counts = page.Rows.Select(row => int.Parse(Assert.IsType<TextNode>(row.Cells["rows"]).Text,
                System.Globalization.CultureInfo.InvariantCulture)).ToArray();
            Assert.Equal(counts.OrderDescending(), counts);
        }
        Assert.Equal(186, keys.Count);
        Assert.Single(browser.Query(new() { Text = "EquipParamGoods.param" }).Rows);
        Assert.Empty(browser.Query(new() { Offset = int.MaxValue }).Rows);
        Assert.Throws<ArgumentException>(() => browser.Query(new() { SortColumn = "unknown" }));

        var plugin = new EldenRingPlugin();
        using var document = plugin.Open(file);
        using var session = new DocumentSession("eldenring-regulation-query", file, plugin, document);
        _ = session.BuildView(); _ = session.Expand("characters/0");
        Assert.True(session.ApplyEdits([WriteTestSupport.Incoming("slots/0/runes", 1146)]).Ok);
        _ = session.Expand("regulation/metadata");
        Assert.Single(session.QueryCollection(RegulationViews.CollectionId, new() { Text = "EquipParamGoods.param" }).Rows);
        Assert.True(session.Undo().Ok);
        WriteTestSupport.EqualBytes(original, document);
    }

    [Fact]
    public void InvalidRegulationHasNoEagerTableAndAnEmptyQueryableFallback()
    {
        var fixture = new SyntheticSave([0]);
        var browser = new RegulationViews(fixture.Bytes);
        Assert.Empty(browser.Collections);
        Assert.IsType<TextNode>(Assert.Single(browser.BuildView()));
        Assert.Empty(browser.Query(new()).Rows);
        Assert.Empty(browser.BuildView().OfType<QueryableTableNode>());
    }
}
