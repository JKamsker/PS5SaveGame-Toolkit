using System.Globalization;
using SaveToolkit.Core.References;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Views;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.FinalFantasy7.Tests;

public sealed class CatalogCollectionTests
{
    [Fact]
    public void OptionalImportedImageCellsRetainAttributionAndUseTheDocumentAssetRoute()
    {
        var file = SampleSaves.Load("PPSA08664").AllDiskFiles.First(file => file.FileName == "ff7rebirth000");
        using var document = new FinalFantasy7Plugin().Open(file, new OverrideContext());
        var row = Assert.Single(((IQueryableDocument)document).Query("catalog-weapons", new() { Text = "Imported Sword" }).Rows);
        var details = Assert.IsType<SectionNode>(row.Cells["details"]);
        var image = Assert.Single(details.Children.OfType<ImageNode>());
        Assert.Equal("test.overrides:fixture.png", image.AssetId);
        Assert.Equal("Imported Sword", image.Alt);
        Assert.Contains(details.Children.OfType<TextNode>(), text => text.Text.Contains("Fixture contributors", StringComparison.Ordinal));
        Assert.Equal(new byte[] { 1, 2, 3 }, document.GetAsset(image.AssetId)!.Content.ToArray());
        Assert.Null(document.GetAsset("missing:fixture.png"));
        Assert.Equal(file.ReadAllBytes(), Assert.Single(document.Serialize()).Content.ToArray());
    }
    [Fact]
    public void ImportedNamesKeepTheirOwnProvenanceInCharactersAndOwnedItems()
    {
        var file = SampleSaves.Load("PPSA08664").AllDiskFiles.First(file => file.FileName == "ff7rebirth000");
        var context = new OverrideContext();
        using var document = new FinalFantasy7Plugin().Open(file, context);
        var tabs = Assert.IsType<TabsNode>(document.BuildView());
        var characters = Assert.IsType<TableNode>(Assert.Single(tabs.Tabs.Single(tab => tab.Title == "Characters").Children));
        Assert.Equal("Imported Cloud", ((TextNode)characters.Rows[0].Cells["name"]).Text);
        Assert.Contains("Fixture contributors", ((TextNode)characters.Rows[0].Cells["source"]).Text, StringComparison.Ordinal);
        var page = Assert.IsAssignableFrom<IQueryableDocument>(document).Query("owned-weapon", new() { Text = "Imported Sword" });
        Assert.NotEmpty(page.Rows);
        Assert.All(page.Rows, row => Assert.Contains("Fixture contributors", ((TextNode)row.Cells["source"]).Text, StringComparison.Ordinal));
        Assert.Equal(file.ReadAllBytes(), Assert.Single(document.Serialize()).Content.ToArray());
    }

    [Theory]
    [InlineData("PPSA01911", 369, 54, 32)]
    [InlineData("PPSA08664", 934, 88, 51)]
    [InlineData("PPSA18936", 934, 88, 51)]
    public void EmbeddedPacksAreBuildScopedAttributedAndComplete(string title, int items, int materia, int weapons)
    {
        var plugin = new FinalFantasy7Plugin();
        var store = new ReferencePackStore([plugin]);
        Assert.Empty(store.LoadErrors);
        var context = store.Pin(plugin, title);
        var schema = Ff7Schema.ForTitle(title)!;
        foreach (var (id, count) in new[] { ("ff7r-items", items), ("ff7r-materia", materia), ("ff7r-weapons", weapons) })
        {
            var catalog = Assert.IsAssignableFrom<IReferenceCatalog>(context.Catalog(id, schema.Name));
            Assert.Equal(count, catalog.Count);
            Assert.Null(context.Catalog(id, "Unqualified build"));
            var fallback = EmbeddedCatalogs.Records(schema, id);
            Assert.All(fallback, record =>
            {
                var loaded = Assert.IsType<ReferenceRecord>(catalog.Find(record.Key));
                Assert.Equal(record.Name, loaded.Name);
                Assert.Equal(record.Category, loaded.Category);
                Assert.Equal(record.Description, loaded.Description);
                Assert.False(string.IsNullOrWhiteSpace(loaded.Category));
                Assert.False(string.IsNullOrWhiteSpace(loaded.Description));
                Assert.Equal("MIT", loaded.Dataset.Attribution.License);
                Assert.Contains(EmbeddedCatalogs.Revision, loaded.AttributionText, StringComparison.Ordinal);
            });
        }
    }

    [Fact]
    public void CardsAndBoosterPacksKeepSeparateTypeDomainsAndHonestDisplayLabels()
    {
        var rows = EmbeddedCatalogs.Records(Ff7Schema.ForTitle("PPSA08664")!, "ff7r-items");
        var cards = rows.Where(record => record.Category == "Queen's Blood Cards").ToArray();
        Assert.Equal(145, cards.Length);
        Assert.Equal(16, rows.Count(record => record.Category == "Queen's Blood Booster Packs"));
        Assert.Equal(2, cards.Count(record => record.Attributes["sourceDisplayNumber"] == "012"));
        Assert.DoesNotContain(cards, record => record.Attributes["sourceDisplayNumber"] == "013");
        Assert.All(cards, record => Assert.Contains("unverified", record.Description!, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("PPSA01911")]
    [InlineData("PPSA08664")]
    [InlineData("PPSA18936")]
    public void QueriesReachAllSlotsAndCatalogPagesWithoutDuplicateKeys(string title)
    {
        var schema = Ff7Schema.ForTitle(title)!;
        var file = SampleSaves.Load(title).AllDiskFiles.First(file => file.FileName == schema.Prefix + "000");
        using var document = new FinalFantasy7Plugin().Open(file);
        var queryable = Assert.IsAssignableFrom<IQueryableDocument>(document);
        var bytes = file.ReadAllBytes();
        foreach (var collection in queryable.Collections)
        {
            var keys = new HashSet<string>(StringComparer.Ordinal);
            for (var offset = 0; offset < collection.TotalCount; offset += 200)
            {
                var page = queryable.Query(collection.Id, new() { Offset = offset, Limit = 200 });
                Assert.Equal(collection.TotalCount, page.TotalCount);
                Assert.Equal(collection.TotalCount, page.FilteredCount);
                Assert.InRange(page.Rows.Count, 1, 200);
                Assert.All(page.Rows, row => Assert.True(keys.Add(row.Key), $"Duplicate {row.Key}"));
            }

            Assert.Equal(collection.TotalCount, keys.Count);
        }

        var last = queryable.Query("inventory", new()
        {
            Filters = new Dictionary<string, string> { ["slot"] = "2047" },
        });
        Assert.Equal("inventory/2047", Assert.Single(last.Rows).Key);
        var sorted = queryable.Query("inventory", new() { SortColumn = "slot", Descending = true, Limit = 2 });
        Assert.Equal(["inventory/2047", "inventory/2046"], sorted.Rows.Select(row => row.Key));
        Assert.Equal(bytes, Assert.Single(document.Serialize()).Content.ToArray());
    }

    [Fact]
    public void SearchAndExactFiltersReachWholeCatalogAndCards()
    {
        var file = SampleSaves.Load("PPSA08664").AllDiskFiles.First(file => file.FileName == "ff7rebirth000");
        using var document = new FinalFantasy7Plugin().Open(file);
        var queryable = Assert.IsAssignableFrom<IQueryableDocument>(document);
        var catalog = EmbeddedCatalogs.Records(Ff7Schema.ForTitle("PPSA08664")!, "ff7r-items");
        var last = catalog[^1];
        var page = queryable.Query("catalog-items", new() { Text = last.Name });
        Assert.Contains(page.Rows, row => row.Key == $"catalog-items/{last.Key}");
        var cards = queryable.Query("cards", new() { Limit = 200 });
        Assert.Equal(145, cards.TotalCount);
        Assert.Contains(cards.Rows, row => long.Parse(((TextNode)row.Cells["quantity"]).Text, CultureInfo.InvariantCulture) > 0);
        Assert.DoesNotContain(cards.Rows, row => ((TextNode)row.Cells["name"]).Text.StartsWith("QB Booster", StringComparison.Ordinal));
        Assert.Throws<ArgumentException>(() => queryable.Query("inventory", new() { SortColumn = "bogus" }));
        Assert.Throws<ArgumentException>(() => queryable.Query("inventory", new() { Limit = 201 }));
        Assert.Throws<ArgumentException>(() => queryable.Query("inventory", new() { Cursor = "unrecognized" }));
    }

    [Fact]
    public void PickerUsesPinnedAttributionAndRejectsUnavailableTypes()
    {
        var plugin = new FinalFantasy7Plugin();
        var file = SampleSaves.Load("PPSA08664").AllDiskFiles.First(file => file.FileName == "ff7rebirth000");
        var context = new ReferencePackStore([plugin]).Pin(plugin, "PPSA08664");
        using var session = new DocumentSession("ff7", file, plugin, plugin.Open(file, context), references: context);
        _ = session.BuildView();
        var page = session.SearchReferences("type", new() { Limit = 200 }, actionId: MateriaPlanning.ActionId);
        Assert.Equal(88, page.Total);
        Assert.Contains(page.Items, candidate => candidate.Eligibility.Selectable);
        Assert.All(page.Items, candidate => Assert.Equal("MIT", candidate.Dataset!.Attribution.License));
        Assert.False(session.FindReference("type", "14011", actionId: MateriaPlanning.ActionId).Eligibility.Selectable);
        var bad = session.PlanAction(MateriaPlanning.ActionId, new Dictionary<string, System.Text.Json.JsonElement>
        {
            ["type"] = System.Text.Json.JsonSerializer.SerializeToElement("4294967294"),
            ["ap"] = System.Text.Json.JsonSerializer.SerializeToElement("0"),
        });
        Assert.Null(bad.PlanId);
        Assert.NotEmpty(bad.Plan.Errors);
    }

    private sealed class OverrideContext : IReferenceContext
    {
        public IReadOnlyList<ReferencePackInfo> Packs => [];
        public IReferenceCatalog? Catalog(string catalogId, string? gameBuild = null) => catalogId switch
        {
            "ff7r-weapons" => new OverrideCatalog(catalogId, "1001", "Imported Sword"),
            "ff7r-characters" => new OverrideCatalog(catalogId, "0", "Imported Cloud"),
            _ => null,
        };
        public AssetContent? GetAsset(string assetId) => assetId == "test.overrides:fixture.png" ? new("image/png", new byte[] { 1, 2, 3 }) : null;
        public ReferenceDatasetInfo? AssetProvenance(string assetId) => null;
    }

    private sealed class OverrideCatalog(string id, string key, string name) : IReferenceCatalog
    {
        public string Id => id;
        public ReferencePackInfo Pack { get; } = new("test.overrides", "1.0.0", "Test overrides", ReferencePackOrigin.User);
        public ReferenceDatasetInfo Dataset { get; } = new("test.overrides", id, ReferenceDatasetKind.Records,
            new("https://example.com/fixture", "Fixture contributors", "MIT", "https://opensource.org/license/mit", new(2026, 10, 28), "None"));
        public int Count => 1;
        public IReadOnlyList<string> Categories => [];
        public ReferenceRecord? Find(string requested) => requested == key ? new(key, name)
        {
            Dataset = Dataset, AssetId = id == "ff7r-weapons" ? "test.overrides:fixture.png" : null,
        } : null;
        public ReferencePage Search(ReferenceQuery query) => new(query.Offset == 0 ? [Find(key)!] : [], 1, query.Offset);
    }
}
