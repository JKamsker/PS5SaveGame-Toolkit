using SaveToolkit.Core.References;
using SaveToolkit.Sdk.References;

namespace SaveToolkit.Plugins.FinalFantasy16.Tests;

public sealed class FaithEmbeddedCatalogTests
{
    [Theory]
    [InlineData(FaithCatalog.WikiItemsId)]
    [InlineData(FaithCatalog.ItemsId)]
    [InlineData(FaithCatalog.PublishedMapsId)]
    [InlineData(FaithCatalog.BattleNpcsId)]
    [InlineData(FaithCatalog.EventNpcsId)]
    [InlineData(FaithCatalog.MapsId)]
    [InlineData(FaithCatalog.SummonsId)]
    [InlineData(FaithCatalog.SkillsId)]
    [InlineData(FaithCatalog.CommandsId)]
    [InlineData(FaithCatalog.FeatsId)]
    [InlineData(FaithCatalog.AbilitiesId)]
    public void FallbackPreservesDefaultOrderFiltersSortsAndPageBounds(string id)
    {
        var plugin = new FinalFantasy16Plugin();
        var host = new ReferencePackStore([plugin]).Pin(plugin, "PPSA10665").Catalog(id)!;
        var records = ReferenceCatalogReader.ReadBounded(host, new(), host.Count);
        ReferenceQuery[] queries =
        [
            new(), new() { Descending = true }, // Descending alone never reversed the fallback order.
            new() { Offset = -1, Limit = 0 }, new() { Offset = 1, Limit = int.MaxValue },
            new() { Offset = int.MaxValue }, new() { Text = "   " },
            new() { Text = "  " + records[0].Name.ToUpperInvariant() + "  " },
            new() { Text = records[0].Key }, new() { Text = "no-such-ffxvi-record" },
            new() { Category = records[0].Category?.ToUpperInvariant() },
            new() { Category = "no-such-ffxvi-category" },
            new() { Text = records[0].Key, Category = records[0].Category, Offset = 1, Limit = 1 },
        ];
        foreach (var query in queries)
        {
            // Historical fallback order is ordinal key order, independent of resource file order.
            var expected = records.Where(record => Matches(record, query)).OrderBy(record => record.Key, StringComparer.Ordinal).ToArray();
            var actual = FaithEmbeddedCatalogs.Search(id, query);
            Assert.Equal(expected.Length, actual.Total);
            Assert.Equal(Math.Max(0, query.Offset), actual.Offset);
            Assert.Equal(expected.Skip(actual.Offset).Take(Math.Clamp(query.Limit, 1, ReferenceQuery.MaxLimit)).Select(record => record.Key),
                actual.Items.Select(record => record.Key));
            foreach (var sort in host.SortKeys)
            foreach (var descending in new[] { false, true })
            {
                var sorted = query with { SortBy = sort, Descending = descending };
                var hostPage = host.Search(sorted);
                var fallbackPage = FaithEmbeddedCatalogs.Search(id, sorted);
                Assert.Equal(hostPage.Total, fallbackPage.Total);
                Assert.Equal(hostPage.Offset, fallbackPage.Offset);
                Assert.Equal(hostPage.Items.Select(record => record.Key), fallbackPage.Items.Select(record => record.Key));
            }
        }
        Assert.Null(FaithEmbeddedCatalogs.Find(id, "no-such-ffxvi-record"));
    }

    [Fact]
    public void MissingCatalogRemainsEmptyAndClampsNegativeOffsets()
    {
        const string MissingCatalog = "no-such-ffxvi-catalog";
        Assert.Null(FaithEmbeddedCatalogs.Find(MissingCatalog, "1"));
        var page = FaithEmbeddedCatalogs.Search(MissingCatalog, new() { Offset = -1 });
        Assert.Empty(page.Items);
        Assert.Equal(0, page.Total);
        Assert.Equal(0, page.Offset);
    }

    private static bool Matches(ReferenceRecord record, ReferenceQuery query) =>
        (query.Category is null || string.Equals(record.Category, query.Category, StringComparison.OrdinalIgnoreCase)) &&
        (string.IsNullOrWhiteSpace(query.Text) || record.Key.Contains(query.Text.Trim(), StringComparison.OrdinalIgnoreCase) ||
            record.Name.Contains(query.Text.Trim(), StringComparison.OrdinalIgnoreCase));
}
