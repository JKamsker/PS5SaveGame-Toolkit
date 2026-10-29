using SaveToolkit.Core.Editing;
using SaveToolkit.Core.References;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.Witcher3.Tests;

public sealed class WitcherInventoryViewTests
{
    [Fact]
    public void SearchFiltersAndNumericSortCoverRowsBeyondTheFirstPage()
    {
        var items = Enumerable.Range(1, 550).Select(i => new InventoryItem(i == 540 ? "needle" : "same label", i, 0,
            (ushort)i, i % 2 == 0) { Slot = i % 2 == 0 ? "armor" : "" }).ToArray();
        var view = new InventoryViews(items, null, Quantity);
        var match = view.Query(new QueryableCollectionQuery { Text = "needle", Limit = 1 });
        Assert.Equal(550, match.TotalCount);
        Assert.Equal(1, match.FilteredCount);
        Assert.Equal("540", Assert.Single(match.Rows).Key);
        var filtered = view.Query(new QueryableCollectionQuery {
            Filters = new Dictionary<string, string> { ["equipped"] = "true", ["slot"] = "armor" },
            SortColumn = "quantity", Descending = true, Limit = 2,
        });
        Assert.Equal(275, filtered.FilteredCount);
        Assert.Equal("550", filtered.Rows[0].Key);
        Assert.Equal("548", filtered.Rows[1].Key);
        Assert.Equal("550", view.Query(new QueryableCollectionQuery { Offset = 549, Limit = 1 }).Rows[0].Key);
        Assert.Empty(view.Query(new QueryableCollectionQuery { Offset = int.MaxValue }).Rows);
        Assert.Equal(200, view.Query(new QueryableCollectionQuery { Limit = int.MaxValue }).Rows.Count);
        Assert.Single(view.Query(new QueryableCollectionQuery { Limit = 0 }).Rows);
        Assert.Throws<ArgumentException>(() => view.Query(new QueryableCollectionQuery { Cursor = "unknown" }));
        Assert.Throws<ArgumentException>(() => view.Query(new QueryableCollectionQuery { SortColumn = "unknown" }));
        Assert.Throws<ArgumentException>(() => view.Query(new QueryableCollectionQuery { Filters = new Dictionary<string, string> { ["unknown"] = "x" } }));
    }

    [Fact]
    public void EqualLabelsKeepSeparateIdentitiesWithDeterministicPagingAndRawFallbacks()
    {
        InventoryItem[] items = [new("modded-id", 9, 0, 1, false), new("modded-id", 3, 0, 1, false)];
        var view = new InventoryViews(items, null, Quantity);
        Assert.Equal("3", Assert.Single(view.Query(new QueryableCollectionQuery { SortColumn = "name", Limit = 1 }).Rows).Key);
        var second = Assert.Single(view.Query(new QueryableCollectionQuery { SortColumn = "name", Offset = 1, Limit = 1 }).Rows);
        Assert.Equal("9", second.Key);
        Assert.Equal("modded-id", Assert.IsType<SectionNode>(second.Cells["name"]).Title);
        Assert.Equal("Unmapped", Assert.IsType<TextNode>(second.Cells["category"]).Text);
        Assert.Equal("modded-id", Assert.IsType<TextNode>(second.Cells["raw"]).Text);
        Assert.Throws<ArgumentException>(() => view.Inspect(new Dictionary<string, FieldValue> { ["item"] = FieldValue.Of("modded-id") }));
    }

    [Fact]
    public void PackProvidesExactAttributedRecordsAndRejectsUnknownPickerIds()
    {
        var plugin = new Witcher3Plugin();
        var store = new ReferencePackStore([plugin]);
        Assert.Empty(store.LoadErrors);
        var context = store.Pin(plugin, "PPSA04018");
        var catalog = context.Catalog("items")!;
        var page = catalog.Search(new SaveToolkit.Sdk.References.ReferenceQuery { Limit = 200 });
        Assert.Equal(4011, page.Total);
        Assert.All(page.Items, record => {
            Assert.False(string.IsNullOrWhiteSpace(record.Description));
            Assert.False(string.IsNullOrWhiteSpace(record.Category));
            Assert.StartsWith("https://witcher.fandom.com/wiki/", record.Source!, StringComparison.Ordinal);
            Assert.Contains("?oldid=", record.Source!, StringComparison.Ordinal);
            Assert.Equal("CC-BY-SA-3.0", record.Dataset.Attribution.License);
            Assert.Null(record.Limit);
            if (record.AssetId is { } asset) { Assert.NotNull(context.GetAsset(asset)); }
        });
        var service = new ReferenceFieldService(context);
        var spec = new ReferenceSpec("items");
        Assert.True(FieldValueCoercer.Normalize(FieldValue.Of("Bread"), spec, references: service).Ok);
        Assert.False(FieldValueCoercer.Normalize(FieldValue.Of("bread"), spec, references: service).Ok);
        Assert.False(FieldValueCoercer.Normalize(FieldValue.Of("not-listed"), spec, references: service).Ok);
        Assert.Null(new ReferencePackStore([plugin]).Pin(plugin, "PPSA99999").Catalog("items"));
    }

    private static FieldNode Quantity(InventoryItem item) => new(item.ValueId, "Quantity", IntegerSpec.UInt16, FieldValue.Of(item.Quantity));
}
