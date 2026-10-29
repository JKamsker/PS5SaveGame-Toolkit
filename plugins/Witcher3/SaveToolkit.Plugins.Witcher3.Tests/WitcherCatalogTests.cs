using SaveToolkit.Core.References;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.Witcher3.Tests;

public sealed class WitcherCatalogTests
{
    [Fact]
    public void CompleteCatalogsHaveUniqueNativeKeysDescriptionsAndPinnedAttribution()
    {
        var context = Context();
        foreach (var (id, count, license) in new[] { ("items", 4011, "CC-BY-SA-3.0"),
            ("abilities", 107, "CC-BY-SA-3.0"), ("gwent", 225, "MIT") })
        {
            var catalog = context.Catalog(id)!;
            var records = All(catalog);
            Assert.Equal(count, catalog.Count);
            Assert.Equal(count, records.Count);
            Assert.Equal(count, records.Select(r => r.Key).Distinct(StringComparer.Ordinal).Count());
            Assert.All(records, record => {
                Assert.False(string.IsNullOrWhiteSpace(record.Name));
                Assert.False(string.IsNullOrWhiteSpace(record.Description));
                Assert.False(string.IsNullOrWhiteSpace(record.Category));
                Assert.Equal(license, record.Dataset.Attribution.License);
                Assert.Equal("Inferred", record.Attributes["mappingConfidence"]);
                Assert.Null(record.Limit);
                if (record.AssetId is { } asset) { Assert.NotNull(context.GetAsset(asset)); }
                Assert.DoesNotContain("{{", record.Description!, StringComparison.Ordinal);
                Assert.DoesNotContain("[[", record.Description!, StringComparison.Ordinal);
                Assert.DoesNotContain("<", record.Name, StringComparison.Ordinal);
                Assert.Contains(license, record.AttributionText, StringComparison.Ordinal);
                Assert.Contains(id == "gwent" ? "d8eab8c25c409be42a0ea7b43b901a43dfefed18" : "?oldid=", record.Source!, StringComparison.Ordinal);
            });
        }

        Assert.NotNull(context.Catalog("items")!.Find("NGP Bear Armor"));
        Assert.NotNull(context.Catalog("items")!.Find("q401_bread"));
        Assert.NotEqual(context.Catalog("items")!.Find("Bread")!.Key, context.Catalog("items")!.Find("q401_bread")!.Key);
        Assert.Equal("Whirl", context.Catalog("abilities")!.Find("S_Sword_s01")!.Name);
        Assert.Equal("Muscle Memory", context.Catalog("abilities")!.Find("S_Sword_s21")!.Name);
        Assert.Equal("Acquired Tolerance", context.Catalog("abilities")!.Find("S_Alchemy_s18")!.Name);
        Assert.Equal("Yarpen Zigrin", context.Catalog("gwent")!.Find("108")!.Name);
        Assert.Equal("130", context.Catalog("gwent")!.Find("131")!.Attributes["canonicalIndex"]);
        Assert.Equal("3", context.Catalog("gwent")!.Find("0")!.Attributes["expectedCopies"]);
        Assert.Contains("not a legal", context.Catalog("gwent")!.Find("0")!.Attributes["copyExpectationScope"], StringComparison.Ordinal);
        var items = All(context.Catalog("items")!);
        Assert.Equal(3670, items.Count(r => !r.Key.StartsWith("wiki-entry/", StringComparison.Ordinal)));
        Assert.Equal(341, items.Count(r => r.Attributes.ContainsKey("nativeIdStatus")));
        var apple = Assert.Single(items, r => r.Name == "Apple");
        Assert.StartsWith("wiki-entry/", apple.Key, StringComparison.Ordinal);
        Assert.Equal("Unknown", apple.Attributes["nativeIdStatus"]);
        Assert.Contains("not a native save ID", apple.Attributes["referenceKeyScope"], StringComparison.Ordinal);
    }

    [Fact]
    public void CatalogQueriesSearchTheWholeDatasetAndKeepStableBoundedPages()
    {
        var view = new CatalogViews(Context(), "sav3-64.23.163", () => [new GwentCard(108, 1)]);
        var collection = "catalog/items";
        var first = view.Query(collection, new QueryableCollectionQuery { Limit = 200 });
        Assert.Equal(4011, first.TotalCount);
        Assert.Equal(200, first.Rows.Count);
        var keys = new HashSet<string>(StringComparer.Ordinal);
        for (var offset = 0; offset < first.TotalCount; offset += 200)
        {
            var page = view.Query(collection, new QueryableCollectionQuery { Offset = offset, Limit = int.MaxValue });
            Assert.InRange(page.Rows.Count, 1, 200);
            Assert.All(page.Rows, row => Assert.True(keys.Add(row.Key)));
        }

        Assert.Equal(first.TotalCount, keys.Count);
        var exact = view.Query(collection, new QueryableCollectionQuery { Filters = new Dictionary<string, string> { ["raw"] = "NGP Bear Armor" } });
        Assert.Equal("NGP Bear Armor", Assert.Single(exact.Rows).Key);
        Assert.Empty(view.Query(collection, new QueryableCollectionQuery { Offset = int.MaxValue }).Rows);
        Assert.Throws<ArgumentException>(() => view.Query(collection, new QueryableCollectionQuery { Cursor = "x" }));
        Assert.Throws<ArgumentException>(() => view.Query(collection, new QueryableCollectionQuery { Filters = new Dictionary<string, string> { ["unsupported"] = "x" } }));
        var card = Assert.Single(view.Query("catalog/gwent", new QueryableCollectionQuery { Text = "Yarpen" }).Rows);
        Assert.Equal("1 saved copies", Assert.IsType<TextNode>(card.Cells["owned"]).Text);
        var absent = Assert.Single(view.Query("catalog/gwent", new QueryableCollectionQuery { Filters = new Dictionary<string, string> { ["raw"] = "0" } }).Rows);
        Assert.Contains("availability unknown", Assert.IsType<TextNode>(absent.Cells["owned"]).Text, StringComparison.Ordinal);
        var empty = new CatalogViews(IReferenceContext.Empty, "sav3-64.23.163", () => null);
        Assert.Empty(empty.Query(collection, new QueryableCollectionQuery()).Rows);
        Assert.Throws<ArgumentException>(() => empty.Inspect("reference/inspect/items", new Dictionary<string, FieldValue> { ["record"] = FieldValue.Of("Bread") }));
        foreach (var column in first.Columns)
        {
            Assert.Equal(first.TotalCount, view.Query(collection, new QueryableCollectionQuery { SortColumn = column.Key,
                Filters = new Dictionary<string, string> { [column.Key] = "" } }).FilteredCount);
        }
    }

    [Fact]
    public void EverySampleJoinsNamedAbilitiesAndAllGwentIndicesWithoutChangingBytes()
    {
        var context = Context();
        foreach (var file in EnabledWriteFixture.Files())
        {
            var container = WitcherContainer.Parse(file.ReadAllBytes());
            var directory = Sav3Directory.Parse(container);
            var tree = Sav3Tree.Parse(container.Payload, directory);
            var player = PlayerData.Read(container, directory, tree);
            var features = new ObservedFeatures(player, tree, container.Payload, directory.Names, context.Catalog("abilities"), context.Catalog("gwent"));
            var skills = Assert.Single(features.Abilities, a => a.Name == "skills").Entries;
            Assert.Equal(95, skills.Count(s => context.Catalog("abilities")!.Find(s.Id) is not null));
            foreach (var skill in skills.Where(s => context.Catalog("abilities")!.Find(s.Id) is not null))
            {
                Assert.Equal(AbilityReader.NameValue(skill.Record.Find("abilityName")!, directory.Names),
                    context.Catalog("abilities")!.Find(skill.Id)!.Attributes["abilityName"]);
            }

            Assert.Equal(12, Assert.Single(features.Abilities, a => a.Name == "mutations").Entries
                .Count(m => context.Catalog("abilities")!.Find(m.Id) is not null));
            Assert.Null(context.Catalog("abilities")!.Find("S_Perk_08")); // Core bookkeeping has no invented friendly skill.
            Assert.All(features.Cards!, card => Assert.NotNull(context.Catalog("gwent")!.Find(card.Index.ToString())));
            using var document = new Witcher3Plugin().Open(file, context);
            _ = document.BuildView();
            var gwent = Assert.IsAssignableFrom<IQueryableDocument>(document).Query("catalog/gwent", new QueryableCollectionQuery { Text = "Yarpen" });
            Assert.Single(gwent.Rows);
            var lookup = document.InvokeAction("reference/inspect/abilities", new Dictionary<string, FieldValue> { ["record"] = FieldValue.Of("S_Sword_s01") });
            Assert.Empty(lookup.Changes);
            Assert.Contains("Whirl", lookup.Message!, StringComparison.Ordinal);
            Assert.Contains("CC-BY-SA-3.0", lookup.Message!, StringComparison.Ordinal);
            Assert.Equal(file.ReadAllBytes(), Assert.Single(document.Serialize()).Content.ToArray());
        }
    }

    private static IReferenceContext Context()
    {
        var plugin = new Witcher3Plugin();
        var store = new ReferencePackStore([plugin]);
        Assert.Empty(store.LoadErrors);
        return store.Pin(plugin, "PPSA04018");
    }

    private static IReadOnlyList<ReferenceRecord> All(IReferenceCatalog catalog) => Enumerable.Range(0, (catalog.Count + 199) / 200)
        .SelectMany(page => catalog.Search(new ReferenceQuery { Offset = page * 200, Limit = 200 }).Items).ToArray();
}
