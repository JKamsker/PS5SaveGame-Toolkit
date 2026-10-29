using SaveToolkit.Core.References;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.UnrealGvas.Tests;

public sealed class GvasQueryPerformanceTests
{
    [Fact]
    public void RepeatedQueriesResolvePinnedMetadataOncePerDistinctKeyAndKeepScalarValuesLive()
    {
        var bytes = GvasFixture.Build(writer =>
        {
            for (int i = 0; i < 251; i++)
                GvasFixture.Property(writer, "Repeated", "IntProperty", GvasFixture.Body(body => body.Write(i)));
        });
        var plugin = new UnrealGvasPlugin();
        var pinned = new ReferencePackStore([plugin]).Pin(plugin, "UNKNOWN");
        var catalog = new CountingCatalog(pinned.Catalog("unrealgvas_property_types")!);
        using var document = plugin.Open(MemorySaveSet.SingleFile("many.sav", bytes), new CountingContext(catalog));
        var queryable = Assert.IsAssignableFrom<IQueryableDocument>(document);
        for (int i = 0; i < 3; i++)
            Assert.Empty(queryable.Query("gvas/properties", new() { Text = "no-matching-property" }).Rows);
        Assert.True(catalog.Lookups <= 2, $"Resolved two distinct metadata keys {catalog.Lookups} times.");
        var first = queryable.Query("gvas/properties", new() { Limit = 200 });
        Assert.Equal(first.Rows.Select(row => row.Key).Order(StringComparer.Ordinal), first.Rows.Select(row => row.Key));
        document.BuildView();
        document.ExpandNode("search");
        Assert.True(document.ApplyEdits([new("property/0", FieldValue.Of(999))]).Succeeded);
        var changed = queryable.Query("gvas/properties", new() { Filters = new Dictionary<string, string> { ["value"] = "999" } });
        Assert.Equal("property/0", Assert.Single(changed.Rows).Key);
        Assert.Contains("trumank", ((TextNode)changed.Rows[0].Cells["source"]).Text);
    }

    private sealed class CountingContext(IReferenceCatalog catalog) : IReferenceContext
    {
        public IReadOnlyList<ReferencePackInfo> Packs => [catalog.Pack];
        public IReferenceCatalog? Catalog(string catalogId, string? gameBuild = null) => catalogId == catalog.Id ? catalog : null;
        public AssetContent? GetAsset(string assetId) => null;
        public ReferenceDatasetInfo? AssetProvenance(string assetId) => null;
    }

    private sealed class CountingCatalog(IReferenceCatalog inner) : IReferenceCatalog
    {
        public int Lookups { get; private set; }
        public string Id => inner.Id;
        public ReferencePackInfo Pack => inner.Pack;
        public ReferenceDatasetInfo Dataset => inner.Dataset;
        public int Count => inner.Count;
        public IReadOnlyList<string> Categories => inner.Categories;
        public ReferenceRecord? Find(string key) { Lookups++; return inner.Find(key); }
        public ReferencePage Search(ReferenceQuery query) => inner.Search(query);
    }
}
