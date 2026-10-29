using SaveToolkit.Core.References;
using SaveToolkit.Sdk.Views;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.FinalFantasy7.Tests;

public sealed class AssetRoundTests
{
    [Theory]
    [InlineData("PPSA01911", 8)]
    [InlineData("PPSA08664", 8)]
    public void EveryCatalogueRecordResolvesAnAttributedIcon(string title, int expectedIconCount)
    {
        var plugin = new FinalFantasy7Plugin();
        var store = new ReferencePackStore([plugin]);
        var context = store.Pin(plugin, title);
        var schema = Ff7Schema.ForTitle(title)!;

        foreach (var id in new[] { "ff7r-items", "ff7r-materia", "ff7r-weapons" })
        {
            var catalog = context.Catalog(id, schema.Name)!;
            for (var offset = 0; offset < catalog.Count; offset += 200)
            {
                Assert.All(catalog.Search(new() { Offset = offset, Limit = 200 }).Items, record =>
                {
                    Assert.NotNull(record.AssetId);
                    var asset = context.GetAsset(record.AssetId!);
                    Assert.NotNull(asset);
                    Assert.Equal("image/webp", asset!.ContentType);
                });
            }
        }

        var assets = context.Packs.Single().Datasets.Single(dataset => dataset.Id == "icons");
        Assert.Equal(expectedIconCount, assets.ItemCount);
        Assert.StartsWith("Rights reserved by Square Enix;", assets.Attribution.License, StringComparison.Ordinal);
    }

    [Fact]
    public void PinnedRebirthMapsHaveBackgroundsAndBoundedListFallbacks()
    {
        var plugin = new FinalFantasy7Plugin();
        var file = SampleSaves.Load("PPSA08664").AllDiskFiles.First(file => file.FileName == "ff7rebirth000");
        var context = new ReferencePackStore([plugin]).Pin(plugin, "PPSA08664");
        using var document = plugin.Open(file, context);
        var tabs = Assert.IsType<TabsNode>(document.BuildView());
        var sections = tabs.Tabs.Single(tab => tab.Title == RegionalMaps.TabTitle).Children
            .OfType<SectionNode>().ToArray();
        Assert.Equal(6, sections.Length);

        foreach (var section in sections)
        {
            var map = Assert.IsType<MapNode>(document.ExpandNode(section.LazyId!)[0]);
            SpatialViewValidation.Validate(map);
            Assert.NotNull(map.Background);
            Assert.Equal("image/webp", context.GetAsset(map.Background!.AssetId)!.ContentType);
            Assert.StartsWith("Rights reserved by Square Enix;", map.Background.Attribution.License,
                StringComparison.Ordinal);
            Assert.All(map.Markers, marker =>
            {
                Assert.Null(marker.X);
                Assert.Null(marker.Y);
                Assert.NotNull(marker.Target);
            });
        }
    }
}
