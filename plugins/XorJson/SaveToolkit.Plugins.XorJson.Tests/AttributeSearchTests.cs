using SaveToolkit.Core.References;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.XorJson.Tests;

public sealed class AttributeSearchTests
{
    [Theory]
    [InlineData("Echo shards")]
    [InlineData("eChO sHaRdS")]
    [InlineData("SHARDS")]
    [InlineData("sPrInGsToNe")]
    public void DisplayedAndRawSearchResolveBeforeAnyViewExpansion(string text)
    {
        var plugin = new XorJsonPlugin();
        var references = new ReferencePackStore([plugin]).Pin(plugin, "PPSA16064");
        var bytes = AttributeSearchData.Hero([("SpringStone", 7), ("Emeralds", 8)]);
        using var document = plugin.Open(MemorySaveSet.SingleFile("hero.sav", bytes), references);
        var queryable = Assert.IsAssignableFrom<IQueryableDocument>(document);
        var row = Assert.Single(queryable.Query(AttributeSearchData.CollectionId, new() { Text = text }).Rows);
        var id = Assert.IsType<FieldNode>(row.Cells["id"]);
        var record = references.Catalog("xorjson-attributes")!.Find("SpringStone")!;
        Assert.Equal("Echo shards", record.Name);
        Assert.Equal("SpringStone", id.Value.AsString());
        Assert.Equal("xorjson-attributes", Assert.IsType<ReferenceSpec>(id.Spec).CatalogId);
        Assert.Equal(Confidence.Inferred, id.Confidence);
        Assert.Contains(record.Name, id.Description!);
        Assert.Contains(record.AttributionText, id.Description!);
        _ = document.BuildView();
        Assert.Equal(row.Key, Assert.Single(queryable.Query(AttributeSearchData.CollectionId, new() { Text = text }).Rows).Key);
        Assert.Equal(bytes, document.Serialize().Single().Content.ToArray());
    }

    [Fact]
    public void AliasSearchFiltersTheFullModelBeforePagingAndPreservesRawFiltersAndOrdering()
    {
        var plugin = new XorJsonPlugin();
        var attributes = Enumerable.Range(0, AttributeSearchData.UnmatchedRows).Select(index => ($"Unknown{index:D3}", index))
            .Concat(new[] { ("SpringStone", 3), ("SpringStone", 1), ("SpringStone", 2) });
        var bytes = AttributeSearchData.Hero(attributes);
        var references = new ReferencePackStore([plugin]).Pin(plugin, "PPSA16064");
        using var document = plugin.Open(MemorySaveSet.SingleFile("hero.sav", bytes), references);
        var queryable = Assert.IsAssignableFrom<IQueryableDocument>(document);
        var first = queryable.Query(AttributeSearchData.CollectionId, new() { Limit = 200 });
        Assert.Equal(200, first.Rows.Count);
        var matches = queryable.Query(AttributeSearchData.CollectionId, new() { Text = "echo SHARDS", Limit = 200 });
        Assert.Equal(AttributeSearchData.UnmatchedRows + 3, matches.TotalCount);
        Assert.Equal(3, matches.FilteredCount);
        var keys = matches.Rows.Select(row => row.Key).ToArray();
        Assert.Equal(3, keys.Distinct(StringComparer.Ordinal).Count());
        Assert.Empty(first.Rows.Select(row => row.Key).Intersect(keys));
        foreach (var descending in new[] { false, true })
        {
            var sorted = queryable.Query(AttributeSearchData.CollectionId, new()
            {
                Text = "Echo shards", SortColumn = "id", Descending = descending,
            });
            Assert.Equal(keys, sorted.Rows.Select(row => row.Key)); // Allocation-ID ties never follow the label or page offset.
            var page = queryable.Query(AttributeSearchData.CollectionId, new()
            {
                Text = "Echo shards", SortColumn = "id", Descending = descending, Offset = 1, Limit = 1,
            });
            Assert.Equal(3, page.FilteredCount);
            Assert.Equal(keys[1], Assert.Single(page.Rows).Key);
        }
        var filtered = queryable.Query(AttributeSearchData.CollectionId, new()
        {
            Text = "Echo shards", Filters = new Dictionary<string, string> { ["id"] = "sPrInGsToNe", ["value"] = "2" },
        });
        Assert.Equal(keys[2], Assert.Single(filtered.Rows).Key);
        Assert.Empty(queryable.Query(AttributeSearchData.CollectionId, new()
        {
            Filters = new Dictionary<string, string> { ["id"] = "Echo shards" },
        }).Rows);
        Assert.Empty(queryable.Query(AttributeSearchData.CollectionId, new()
        {
            Filters = new Dictionary<string, string> { ["id"] = "Spring" },
        }).Rows);
        Assert.Equal(keys[2], Assert.Single(queryable.Query(AttributeSearchData.CollectionId, new()
        {
            Text = "Echo shards", Filters = new Dictionary<string, string>
            {
                ["details"] = "$.member(\"CharacterSaveV1\",0).member(\"Ability\",0).member(\"Attributes\",0)" +
                    $".element({AttributeSearchData.UnmatchedRows + 2})",
            },
        }).Rows).Key);
        Assert.Empty(queryable.Query(AttributeSearchData.CollectionId, new() { Text = "Echo shards", Offset = 3 }).Rows);
        Assert.Equal(bytes, document.Serialize().Single().Content.ToArray());
    }

    [Fact]
    public void UnknownAndMissingCatalogsStayRawAndCatalogIdentifiersRemainExact()
    {
        var plugin = new XorJsonPlugin();
        var bytes = AttributeSearchData.Hero([("SpringStone", 1), ("springstone", 2), ("FutureAttribute", 3)]);
        foreach (var references in new IReferenceContext[] { IReferenceContext.Empty, new ReferencePackStore([plugin]).Pin(plugin, "PPSA16064") })
        {
            using var document = plugin.Open(MemorySaveSet.SingleFile("hero.sav", bytes), references);
            var queryable = Assert.IsAssignableFrom<IQueryableDocument>(document);
            Assert.Equal(2, queryable.Query(AttributeSearchData.CollectionId, new() { Text = "SPRINGSTONE" }).FilteredCount);
            var unknown = Assert.Single(queryable.Query(AttributeSearchData.CollectionId, new() { Text = "futureattribute" }).Rows);
            var id = Assert.IsType<FieldNode>(unknown.Cells["id"]);
            Assert.Equal("FutureAttribute", id.Value.AsString());
            Assert.Equal(Confidence.Unknown, id.Confidence);
            var aliases = queryable.Query(AttributeSearchData.CollectionId, new() { Text = "Echo shards" });
            Assert.Equal(references == IReferenceContext.Empty ? 0 : 1, aliases.FilteredCount);
            Assert.Equal(bytes, document.Serialize().Single().Content.ToArray());
        }
    }

    [Fact]
    public void HostNamesDriveSearchWithoutCollapsingDifferentRawIdentifiers()
    {
        var plugin = new XorJsonPlugin();
        var pinned = new ReferencePackStore([plugin]).Pin(plugin, "PPSA16064");
        var references = new RenamedReferences(pinned);
        var bytes = AttributeSearchData.Hero([("SpringStone", 1), ("Emeralds", 2), ("SpringStone", 3)]);
        using var document = plugin.Open(MemorySaveSet.SingleFile("hero.sav", bytes), references);
        var queryable = Assert.IsAssignableFrom<IQueryableDocument>(document);
        var rows = queryable.Query(AttributeSearchData.CollectionId, new() { Text = "HOST LABEL", SortColumn = "id" }).Rows;
        Assert.Equal(3, rows.Count);
        Assert.Equal(3, rows.Select(row => row.Key).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(["Emeralds", "SpringStone", "SpringStone"], rows.Select(row => Assert.IsType<FieldNode>(row.Cells["id"]).Value.AsString()));
        Assert.All(rows, row => Assert.Contains("Host label", Assert.IsType<FieldNode>(row.Cells["id"]).Description!));
        Assert.Empty(queryable.Query(AttributeSearchData.CollectionId, new() { Text = "Echo shards" }).Rows);
        Assert.Equal(2, queryable.Query(AttributeSearchData.CollectionId, new() { Text = "springstone" }).FilteredCount);
        Assert.Equal(bytes, document.Serialize().Single().Content.ToArray());
    }

    [Fact]
    public void ScalarEditsRefreshBothResolvedNamesAndSearchValuesWithoutChangingIdentity()
    {
        var bytes = AttributeSearchData.Hero([("SpringStone", 7)]);
        var plugin = new XorJsonPlugin();
        var references = new ReferencePackStore([plugin]).Pin(plugin, "PPSA16064");
        using var document = plugin.Open(MemorySaveSet.SingleFile("hero.sav", bytes), references);
        var queryable = Assert.IsAssignableFrom<IQueryableDocument>(document);
        var row = Assert.Single(queryable.Query(AttributeSearchData.CollectionId, new() { Text = "Echo shards" }).Rows);
        var id = Assert.IsType<FieldNode>(row.Cells["id"]);
        var value = Assert.IsType<FieldNode>(row.Cells["value"]);
        Assert.True(document.ApplyEdits([new(value.ValueId, FieldValue.ExactNumber("42"))]).Succeeded);
        Assert.Empty(queryable.Query(AttributeSearchData.CollectionId, new() { Text = "7" }).Rows);
        Assert.Equal(row.Key, Assert.Single(queryable.Query(AttributeSearchData.CollectionId, new() { Text = "42" }).Rows).Key);
        Assert.True(document.ApplyEdits([new(id.ValueId, FieldValue.Of("Emeralds"))]).Succeeded);
        Assert.Empty(queryable.Query(AttributeSearchData.CollectionId, new() { Text = "Echo shards" }).Rows);
        Assert.Equal(row.Key, Assert.Single(queryable.Query(AttributeSearchData.CollectionId, new() { Text = "Emeralds" }).Rows).Key);
        Assert.True(document.ApplyEdits([new(id.ValueId, id.Value), new(value.ValueId, value.Value)]).Succeeded);
        Assert.Equal(row.Key, Assert.Single(queryable.Query(AttributeSearchData.CollectionId, new() { Text = "Echo shards" }).Rows).Key);
        Assert.Equal(bytes, document.Serialize().Single().Content.ToArray());
    }

    private sealed class RenamedReferences(IReferenceContext inner) : IReferenceContext
    {
        // Empty pack metadata must not cause the document to replace a supplied catalog context.
        public IReadOnlyList<ReferencePackInfo> Packs => [];
        public IReferenceCatalog? Catalog(string catalogId, string? gameBuild = null) => inner.Catalog(catalogId, gameBuild) is { } catalog
            ? catalogId == "xorjson-attributes" ? new RenamedCatalog(catalog) : catalog : null;
        public AssetContent? GetAsset(string assetId) => inner.GetAsset(assetId);
        public ReferenceDatasetInfo? AssetProvenance(string assetId) => inner.AssetProvenance(assetId);
    }

    private sealed class RenamedCatalog(IReferenceCatalog inner) : IReferenceCatalog
    {
        public string Id => inner.Id;
        public ReferencePackInfo Pack => inner.Pack;
        public ReferenceDatasetInfo Dataset => inner.Dataset;
        public int Count => inner.Count;
        public IReadOnlyList<string> Categories => inner.Categories;
        public ReferenceRecord? Find(string key) => inner.Find(key) is { } record ? record with { Name = "Host label" } : null;
        public ReferencePage Search(ReferenceQuery query) => throw new InvalidOperationException("Row projection must use exact lookup, not catalog search.");
    }
}
