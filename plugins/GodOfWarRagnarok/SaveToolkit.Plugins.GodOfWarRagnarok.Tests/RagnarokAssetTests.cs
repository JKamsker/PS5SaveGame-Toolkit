using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.GodOfWarRagnarok.Tests;

public sealed class RagnarokAssetTests
{
    [Fact]
    public void VisualCatalogueAssetsResolveAndRealmBackgroundIsAttributed()
    {
        var plugin = new GodOfWarRagnarokPlugin();
        var context = RagnarokTestSupport.PinnedReferences(plugin);
        var catalog = Assert.IsAssignableFrom<IReferenceCatalog>(context.Catalog(RagnarokReferences.AssetCatalogId));

        Assert.Equal(11, catalog.Count);
        Assert.Contains(catalog.Categories, category => category is "weapon" or "gear" or "rune" or "skill" or "map");
        var bytes = 0L;
        foreach (var record in catalog.Search(new() { Limit = 32 }).Items)
        {
            Assert.NotNull(record.AssetId);
            var assetId = record.AssetId!;
            var asset = context.GetAsset(assetId);
            Assert.NotNull(asset);
            bytes += asset!.Content.Length;
        }

        Assert.True(bytes < 4 * 1024 * 1024);
        var map = RagnarokSpatialViews.RealmMap(new RagnarokReferences(context));
        SpatialViewValidation.Validate(map);
        Assert.NotNull(map.Background);
        var background = map.Background!;
        Assert.NotNull(context.GetAsset(background.AssetId));
        Assert.Contains("MapGenie", background.Attribution.Author);
    }
}
