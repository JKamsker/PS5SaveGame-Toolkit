using SaveToolkit.Core.References;
using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.GenericJson.Tests;

public sealed class JsonAssetRoundTests
{
    private const string OfficialSource =
        "https://www.cyberpunk.net/en/news/41435/patch-1-5-next-generation-update-list-of-changes";
    private const string OfficialDescription = "Ensures smooth gameplay at 60fps with dynamic 4K scaling.";
    private const string DisplayCatalog = "cp2077-v136-display";

    [Fact]
    public void OfficialDescriptionKeepsHistoricalContextPublisherRightsAndUnverifiedJoins()
    {
        var plugin = new GenericJsonPlugin();
        var store = new ReferencePackStore([plugin]);
        Assert.Empty(store.LoadErrors); // Includes manifest schema and normalized file-hash verification.
        var context = store.Pin(plugin, "PPSA04027");
        var catalog = Assert.IsAssignableFrom<IReferenceCatalog>(context.Catalog(DisplayCatalog, "136"));
        var performance = catalog.Find("Performance");
        Assert.NotNull(performance);
        Assert.Contains(OfficialDescription, performance.Description);
        Assert.Contains("patch 1.5", performance.Description);
        Assert.Contains("inferred", performance.Description);
        Assert.Contains("installed-build behavior is unverified", performance.Description);
        Assert.Equal(OfficialSource, performance.Source);
        Assert.Contains(OfficialSource, plugin.Info.References);
        Assert.Equal("Rights reserved by CD PROJEKT RED; used with attribution per owner decision 2026-10-29",
            performance.Dataset.Attribution.License);
        Assert.Contains("2022-02-15", performance.Dataset.Attribution.Modifications);
        Assert.Contains("CD PROJEKT RED", performance.AttributionText);
        var quality = catalog.Find("Quality");
        Assert.NotNull(quality);
        Assert.Equal("Quality", quality.Name);
        Assert.Contains("No resolution or frame-rate guarantee is inferred", quality.Description);
        Assert.DoesNotContain("Ray Tracing", quality.Description);
        Assert.Null(quality.Source);

        // These are settings choices, with no verified native icon associations.
        Assert.All(catalog.Search(new()).Items, record => Assert.Null(record.AssetId));
        var difficulty = context.Catalog("cp2077-v136-difficulty", "136");
        Assert.NotNull(difficulty);
        Assert.All(difficulty.Search(new()).Items, record =>
        {
            Assert.Null(record.AssetId);
            Assert.Contains("unverified", record.Description);
            Assert.Equal("GPL-3.0-or-later", record.Dataset.Attribution.License);
        });
        Assert.Same(catalog, context.Catalog(DisplayCatalog, "136"));
        Assert.Same(catalog, store.Pin(plugin, "PPSA04027").Catalog(DisplayCatalog, "136"));
    }

    [Fact]
    public void RealSettingsPickerExposesOfficialTextAndSourcesWithoutChangingTheSave()
    {
        var sample = SampleSaves.Load("PPSA04027").AllDiskFiles.First(file => file.FileName == "settings.json");
        var original = sample.ReadAllBytes();
        using var fixture = new JsonSessionFixture(System.Text.Encoding.UTF8.GetString(original),
            title: "PPSA04027", fileName: "settings.json", useReferences: true);
        var field = Assert.Single(fixture.Fields().DistinctBy(field => field.ValueId),
            field => field.Spec is ReferenceSpec { CatalogId: DisplayCatalog });
        var record = Assert.Single(fixture.Session.SearchReferences(field.ValueId, new() { Text = "Performance" }).Items);
        Assert.Contains(OfficialDescription, record.Description);
        Assert.NotNull(record.Dataset);
        Assert.Contains("CD PROJEKT RED", record.Dataset.Attribution.Author);
        Assert.Equal(original, fixture.Bytes);
    }
}
