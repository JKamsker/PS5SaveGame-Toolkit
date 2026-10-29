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
        var tables = RegulationArchiveReader.Read(RegulationDecoder.Decode(
            original.AsSpan(EldenRingLayout.RegulationOffset, EldenRingLayout.RegulationSize))).Tables;
        Assert.Contains(tables.GroupBy(table => table.Rows), group => group.Count() > 1); // Exercise stable ties too.
        foreach (var column in new[] { "rows", "size" })
        foreach (var descending in new[] { false, true })
        {
            int Number(RegulationTable table) => column == "rows" ? table.Rows : table.Size;
            var ordered = descending ? tables.OrderByDescending(Number) : tables.OrderBy(Number);
            var expected = ordered.ThenBy(table => table.Name, StringComparer.Ordinal).ToArray();
            var keys = new List<string>();
            for (var offset = 0; offset < tables.Count; offset += 50)
            {
                var page = browser.Query(new() { Offset = offset, Limit = 50, SortColumn = column, Descending = descending });
                Assert.Equal(tables.Count, page.TotalCount);
                Assert.Equal(tables.Count, page.FilteredCount);
                Assert.Equal(offset, page.Offset);
                Assert.Equal(50, page.Limit);
                var expectedPage = expected.Skip(offset).Take(50).ToArray();
                Assert.Equal(expectedPage.Select(table => table.Name), page.Rows.Select(row => row.Key));
                Assert.Equal(expectedPage.Select(Number), page.Rows.Select(row => int.Parse(
                    Assert.IsType<TextNode>(row.Cells[column]).Text, System.Globalization.CultureInfo.InvariantCulture)));
                keys.AddRange(page.Rows.Select(row => row.Key));
            }
            Assert.Equal(expected.Select(table => table.Name), keys);
            Assert.Equal(tables.Count, keys.Distinct(StringComparer.Ordinal).Count());
        }
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
