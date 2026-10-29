using System.Globalization;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;
using SaveToolkit.Testing;
using SaveToolkit.Testing.Diff;

namespace SaveToolkit.Plugins.BaldursGate3.Tests;

public sealed class FrameworkAdoptionTests
{
    [Fact]
    public void ClassSortingKeepsUnsignedLevelsAboveInt64InNumericOrder()
    {
        var bytes = LsmfFixture.Create(("game.stats.v0.ClassesComponent", 16, 1, new byte[16]),
            ("test.ClassHeap", 40, 0, new byte[80]));
        LsmfFixture.U64(bytes, 48, 16);
        LsmfFixture.U64(bytes, 56, 96);
        LsmfFixture.U64(bytes, 64 + 32, ulong.MaxValue);
        LsmfFixture.U64(bytes, 104 + 32, 9);
        using var document = Assert.IsType<BaldursGate3Document>(new BaldursGate3Plugin().Open(
            MemorySaveSet.SingleFile("Globals.lsf", LsmfFixture.Resource(bytes))));
        foreach (var descending in new[] { false, true })
        {
            var page = document.Query("live/0/0/classes", new() { SortColumn = "level", Descending = descending });
            var levels = page.Rows.Select(row => Assert.IsType<FieldNode>(row.Cells["level"]).Value.AsInteger());
            Assert.Equal(descending ? [(Int128)ulong.MaxValue, 9] : [9, (Int128)ulong.MaxValue], levels);
        }
    }

    [Fact]
    public void F12TraversesAllLiveValuesAcrossPagesAndReportsTheEditedQuantity()
    {
        var source = LiveNewAgeFixture.Source();
        var before = LsmfFixture.Resource(source.Write());
        var quantity = source.Numbers.First(n => n.Family == "Stacks");
        source.Set(quantity, FieldValue.Of(quantity.Original - 1));
        var after = LsmfFixture.Resource(source.Write());
        var decoder = new PluginViewDecoder(new BaldursGate3Plugin());
        var limits = new DecodeLimits { QueryPageSize = 1 };
        var oldTree = decoder.Decode(MemorySaveSet.SingleFile("Globals.lsf", before), limits);
        var newTree = decoder.Decode(MemorySaveSet.SingleFile("Globals.lsf", after), limits);
        Assert.False(oldTree.Truncated);
        Assert.False(newTree.Truncated);
        foreach (var number in source.Numbers)
        {
            Assert.Equal(number.Original.ToString(CultureInfo.InvariantCulture),
                Assert.Single(oldTree.Values, v => v.Path == source.Id(number)).Value);
            Assert.Equal(source.Get(number).ToString(CultureInfo.InvariantCulture),
                Assert.Single(newTree.Values, v => v.Path == source.Id(number)).Value);
        }
        Assert.Equal(before, LsmfFixture.Resource(LiveNewAgeFixture.Create()));
    }

    [Fact]
    public void F12ReportsIncompleteEvidenceWhenTheLiveRowBudgetIsExceeded()
    {
        var file = MemorySaveSet.SingleFile("Globals.lsf", LsmfFixture.Resource(LiveNewAgeFixture.Create()));
        var tree = new PluginViewDecoder(new BaldursGate3Plugin()).Decode(file,
            new DecodeLimits { MaxQueryRows = 1, QueryPageSize = 1 });
        Assert.True(tree.Truncated);
        Assert.Contains(tree.Warnings, warning => warning.Contains("row", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("id", ReferenceSortKey.Key, false)]
    [InlineData("name", ReferenceSortKey.Name, false)]
    [InlineData("icon", ReferenceSortKey.Name, false)]
    [InlineData("category", ReferenceSortKey.Category, false)]
    [InlineData("id", ReferenceSortKey.Key, true)]
    [InlineData("name", ReferenceSortKey.Name, true)]
    [InlineData("icon", ReferenceSortKey.Name, true)]
    [InlineData("category", ReferenceSortKey.Category, true)]
    public void F13CatalogUsesHostFilteringSortingAndBrowseOnlyIds(string column, ReferenceSortKey sort, bool descending)
    {
        var (plugin, _, context) = ReferenceViewTests.References();
        var catalog = context.Catalog(Bg3References.Items)!;
        Assert.Contains(sort, catalog.SortKeys);
        var category = catalog.Categories[0];
        var expected = catalog.Search(new ReferenceQuery
        {
            Category = category, SortBy = sort, Descending = descending, Limit = 1,
        });
        using var document = Assert.IsType<BaldursGate3Document>(plugin.Open(
            MemorySaveSet.SingleFile("Globals.lsf", LsmfFixture.Resource(LiveNewAgeFixture.Create())), context));
        var page = document.Query(catalog.Id, new QueryableCollectionQuery
        {
            Filters = new Dictionary<string, string> { ["category"] = category },
            SortColumn = column, Descending = descending, Limit = 1,
        });
        Assert.Equal(expected.Total, page.FilteredCount);
        var field = Assert.IsType<FieldNode>(Assert.Single(page.Rows).Cells["id"]);
        Assert.Equal(Assert.Single(expected.Items).Key, field.Value.AsString());
        Assert.True(Assert.IsType<ReferenceSpec>(field.Spec).ReadOnly);
        Assert.False(document.ApplyEdits([new(field.ValueId, FieldValue.Of("changed"))]).Succeeded);
    }
}
