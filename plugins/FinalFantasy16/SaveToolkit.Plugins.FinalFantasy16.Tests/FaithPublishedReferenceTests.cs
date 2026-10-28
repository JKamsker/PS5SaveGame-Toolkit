using System.Buffers.Binary;
using SaveToolkit.Core.References;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.FinalFantasy16.Tests;

public sealed class FaithPublishedReferenceTests
{
    private const string License = "No licence published; reused with attribution per owner decision 2026-10-28";
    private readonly FinalFantasy16Plugin _plugin = new();

    [Theory]
    [InlineData(110, "Iron Filings", "material")]
    [InlineData(501, "Drakeslayer Gear Design Draft", "curiosity")]
    [InlineData(614, "Before the Storm Orchestrion Roll", "orchestrion")]
    [InlineData(100024, "Diamond Sword +1", "weapon")]
    [InlineData(100390, "Reinforced Eludium Wristlets", "vambrace")]
    [InlineData(100616, "Reflection of Fire (Phoenix Shift)", "accessory")]
    public void PublishedLabelsAreCategorizedSearchableAttributedAndDoNotClaimGameplayCaps(int id, string name, string category)
    {
        var context = new ReferencePackStore([_plugin]).Pin(_plugin, "PPSA10665");
        var host = context.Catalog(FaithCatalog.ItemsId)!;
        var record = Assert.IsType<ReferenceRecord>(host.Find(FaithCatalog.Key(id)));
        Assert.Equal(name, record.Name);
        Assert.Equal(category, record.Category);
        Assert.Equal(License, record.Dataset.Attribution.License);
        Assert.Equal("Inferred", record.Attributes["confidence"]);
        Assert.Equal("Inferred", record.Attributes["categoryConfidence"]);
        Assert.Null(record.Limit);
        Assert.Null(record.AssetId);
        Assert.Contains("No save-index", record.Description);
        Assert.Contains(record.Source!, _plugin.Info.References);
        var query = new ReferenceQuery { Text = name, Category = category, SortBy = ReferenceSortKey.Name };
        Assert.Contains(host.Search(query).Items, candidate => candidate.Key == record.Key);
        Assert.Equal(host.Search(query).Items.Select(candidate => candidate.Key),
            FaithEmbeddedCatalogs.Search(FaithCatalog.ItemsId, query).Items.Select(candidate => candidate.Key));
    }

    [Fact]
    public void UnnamedPlaceholdersAreOmittedAndMapVariantsKeepTheirIndependentAssetPaths()
    {
        var context = new ReferencePackStore([_plugin]).Pin(_plugin, "PPSA10665");
        var items = context.Catalog(FaithCatalog.ItemsId)!;
        Assert.Null(items.Find("0"));
        Assert.Null(items.Find("4"));
        Assert.Null(items.Find("100000"));
        Assert.Null(items.Find("900000"));
        Assert.Equal(0, items.Search(new ReferenceQuery { Text = "**(no name)**" }).Total);
        var map = context.Catalog(FaithCatalog.PublishedMapsId)!.Find("100000")!;
        Assert.Equal("2", map.Attributes["variantCount"]);
        Assert.Contains("map/a/f00/a00/a_f00_a00.mpb", map.Attributes["variant_0"]);
        Assert.Contains("map/a/f00/a20/a_f00_a20.mpb", map.Attributes["variant_1"]);
        Assert.Equal(License, map.Dataset.Attribution.License);
        Assert.Equal("Clive", context.Catalog(FaithCatalog.BattleNpcsId)!.Find("1")!.Name);
        Assert.Equal("Famiel", context.Catalog(FaithCatalog.EventNpcsId)!.Find("76")!.Name);
        // The differently sourced map schema annotations remain available independently.
        Assert.Equal("MIT", context.Catalog(FaithCatalog.MapsId)!.Find("100000")!.Dataset.Attribution.License);
        Assert.Equal(234, context.Catalog(FaithCatalog.WikiItemsId)!.Count);
    }

    [Theory]
    [InlineData("PPSA10665", 41)]
    [InlineData("PPSA13803", 0)]
    public void PreviouslyUnnamedIdentifiersArePresentInSampleBytesAndNowHaveInferredPickerLabels(string title, int newlyNamed)
    {
        var context = new ReferencePackStore([_plugin]).Pin(_plugin, title);
        var catalog = new FaithCatalog(context);
        var missingFromWiki = new HashSet<int>();
        foreach (var file in FaithSamples.Records(title))
        {
            var bytes = file.ReadAllBytes();
            var inventory = FaithInventory.Parse(bytes, FaithContainer.Parse(bytes));
            using var document = _plugin.Open(file, context);
            var fields = FaithViewFields.All(document.BuildView()).DistinctBy(field => field.ValueId).ToDictionary(field => field.ValueId, field => field,
                StringComparer.Ordinal); // Main inventory rows can also be queried below.
            foreach (var item in inventory.Items)
            {
                Assert.Equal(item.Id, BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(item.Offset)));
                Assert.NotNull(catalog.Item(item.Id));
                if (context.Catalog(FaithCatalog.WikiItemsId)!.Find(FaithCatalog.Key(item.Id)) is not null) continue;
                missingFromWiki.Add(item.Id);
                var valueId = $"inventory/{item.Slot}/item";
                var field = fields.GetValueOrDefault(valueId) ?? FaithViewFields.All(((IQueryableDocument)document)
                    .Query(FaithInventoryView.CollectionId, new() { Offset = item.Slot, Limit = 1 }).Rows.Single().Cells["id"]).Single();
                Assert.Equal(catalog.Name(item.Id), field.Label);
                Assert.Equal(Confidence.Inferred, field.Confidence);
                Assert.Equal(FaithCatalog.ItemsId, Assert.IsType<ReferenceSpec>(field.Spec).CatalogId);
                Assert.Contains(License, field.Description);
                Assert.Contains("Not validated on console", field.Warning);
            }
            Assert.Equal(bytes, Assert.Single(document.Serialize()).Content.ToArray());
        }
        Assert.Equal(newlyNamed, missingFromWiki.Count);
    }

    [Fact]
    public void ReferenceBrowsersAreReadOnlyAndNeverCreateSaveBindingsOrUndoHistory()
    {
        var bytes = FaithTestData.Container((99, 1, new byte[8]));
        using var fixture = new FaithSessionFile(bytes);
        var context = new ReferencePackStore([_plugin]).Pin(_plugin, "PPSA10665");
        using var document = _plugin.Open(fixture.File, context);
        using var session = new DocumentSession("published-references", fixture.File, _plugin, document, references: context);
        var view = Assert.IsType<TabsNode>(session.BuildView());
        var fields = view.Tabs.Single(tab => tab.Title == "Published references").Children.SelectMany(FaithViewFields.All).ToArray();
        Assert.Equal(3, fields.Length);
        Assert.All(fields, field =>
        {
            Assert.True(field.Spec.ReadOnly);
            Assert.IsType<ReferenceSpec>(field.Spec);
            Assert.Contains(License, field.Description);
            Assert.False(document.ApplyEdits([new(field.ValueId, field.Value)]).Succeeded);
        });
        Assert.False(document.CanEdit);
        Assert.False(session.State.CanUndo);
        Assert.False(session.State.IsDirty);
        Assert.Equal(bytes, Assert.Single(document.Serialize()).Content.ToArray());
    }
}
