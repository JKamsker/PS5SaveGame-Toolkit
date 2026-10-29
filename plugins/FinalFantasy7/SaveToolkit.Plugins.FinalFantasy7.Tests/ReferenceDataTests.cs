using System.Globalization;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.References;

namespace SaveToolkit.Plugins.FinalFantasy7.Tests;

public sealed class ReferenceDataTests
{
    [Theory]
    [InlineData("PPSA01911")]
    [InlineData("PPSA08664")]
    [InlineData("PPSA18936")]
    public void MissingAndEmptyPacksRetainCompleteBuildScopedEmbeddedFallback(string title)
    {
        var schema = Ff7Schema.ForTitle(title)!;
        var absent = new ReferenceData(IReferenceContext.Empty, schema);
        var empty = new ReferenceData(new TestContext(new TestCatalog([])), schema);
        foreach (var id in new[] { "ff7r-items", "ff7r-materia", "ff7r-weapons" })
        {
            var expected = EmbeddedCatalogs.Records(schema, id).OrderBy(record => uint.Parse(record.Key, CultureInfo.InvariantCulture));
            Assert.Equal(expected, absent.Records(id));
            Assert.Equal(absent.Records(id), empty.Records(id));
        }
    }

    [Fact]
    public void ImportedNumericAliasesOverrideFallbackInSourceOrderAndKeepProvenance()
    {
        var schema = Ff7Schema.ForTitle("PPSA08664")!;
        var fallback = EmbeddedCatalogs.Records(schema, "ff7r-items");
        var key = fallback[0].Key;
        var alias = Record("000" + key, "Imported alias");
        var canonical = Record(key, "Last imported override");
        var maximum = Record("4294967295", "Maximum raw ID");
        var catalog = new TestCatalog([alias, canonical, maximum, Record("-1"), Record("+1"), Record(" 1"),
            Record("0x1"), Record("4294967296"), Record("not-numeric")]);
        var context = new TestContext(catalog);
        var references = new ReferenceData(context, schema);
        var records = references.Records("ff7r-items");
        Assert.Equal(fallback.Count + 1, records.Count);
        Assert.Same(canonical, references.Record("ff7r-items", uint.Parse(key, CultureInfo.InvariantCulture)));
        Assert.Same(maximum, records[^1]);
        Assert.Equal(canonical.AttributionText, references.Source("ff7r-items", uint.Parse(key, CultureInfo.InvariantCulture)));
        Assert.Equal($"{canonical.Description} {canonical.AttributionText}", references.Describe(false, uint.Parse(key, CultureInfo.InvariantCulture)));
        Assert.Equal(fallback.Skip(1), records.Where(record => record != canonical && record != maximum));
        Assert.Equal(schema.Name, context.LastBuild);
        Assert.Same(records, references.Records("ff7r-items"));
        Assert.Single(catalog.Queries);

        var aliasOnly = new ReferenceData(new TestContext(new TestCatalog([alias])), schema);
        Assert.Same(alias, aliasOnly.Record("ff7r-items", uint.Parse(key, CultureInfo.InvariantCulture)));
        Assert.Contains(aliasOnly.CatalogPage(false, 0).Rows, row => row.Key == $"catalog/items/{uint.Parse(key, CultureInfo.InvariantCulture):X8}");
    }

    [Fact]
    public void CompleteMultiPageIndexUsesNumericOrderAndCachesOnlyAfterSuccess()
    {
        var imported = Enumerable.Range(1_000_000, 401).Reverse().Select(id => Record(id.ToString(CultureInfo.InvariantCulture))).ToArray();
        var catalog = new TestCatalog(imported);
        var references = new ReferenceData(new TestContext(catalog), Ff7Schema.ForTitle("PPSA08664")!);
        var records = references.Records("ff7r-items");
        Assert.Equal(imported.Reverse(), records.TakeLast(imported.Length));
        Assert.Equal([0, 200, 400], catalog.Queries.Select(query => query.Offset));
        Assert.All(catalog.Queries, query => Assert.Equal(new ReferenceQuery { Offset = query.Offset, Limit = ReferenceQuery.MaxLimit }, query));
        Assert.Same(records, references.Records("ff7r-items"));
        Assert.Equal(3, catalog.Queries.Count);
    }

    [Fact]
    public void FailedPageDoesNotCachePartialOverridesOrFallbackAsComplete()
    {
        var catalog = new TestCatalog(Enumerable.Range(1_000_000, 201).Select(id => Record(id.ToString(CultureInfo.InvariantCulture))).ToArray());
        catalog.SearchOverride = query => query.Offset == 0 ? catalog.Page(query) : throw new InvalidOperationException("Fixture page failure.");
        var references = new ReferenceData(new TestContext(catalog), Ff7Schema.ForTitle("PPSA08664")!);
        Assert.Equal("Fixture page failure.", Assert.Throws<InvalidOperationException>(() => references.Records("ff7r-items")).Message);
        catalog.SearchOverride = null;
        Assert.Same(catalog.Records[^1], references.Record("ff7r-items", 1_000_200));
        Assert.Equal([0, 200, 0, 200], catalog.Queries.Select(query => query.Offset));
    }

    private static ReferenceRecord Record(string key, string name = "Imported record") => new(key, name)
    {
        Dataset = Dataset, Description = "Imported description.", Category = "Fixture category",
        Source = "https://example.com/record", AssetId = "test.references:fixture.png",
    };

    private static ReferenceDatasetInfo Dataset { get; } = new("test.references", "ff7r-items", ReferenceDatasetKind.Records,
        new("https://example.com/catalog", "Fixture contributors", "MIT", "https://opensource.org/license/mit", new(2026, 10, 29), "None"));

    private sealed class TestContext(IReferenceCatalog catalog) : IReferenceContext
    {
        public string? LastBuild { get; private set; }
        public IReadOnlyList<ReferencePackInfo> Packs => [];
        public IReferenceCatalog? Catalog(string catalogId, string? gameBuild = null)
        {
            LastBuild = gameBuild;
            return catalogId == catalog.Id ? catalog : null;
        }
        public AssetContent? GetAsset(string assetId) => null;
        public ReferenceDatasetInfo? AssetProvenance(string assetId) => null;
    }

    private sealed class TestCatalog(IReadOnlyList<ReferenceRecord> records) : IReferenceCatalog
    {
        public IReadOnlyList<ReferenceRecord> Records { get; } = records;
        public List<ReferenceQuery> Queries { get; } = [];
        public Func<ReferenceQuery, ReferencePage>? SearchOverride { get; set; }
        public string Id => "ff7r-items";
        public ReferencePackInfo Pack { get; } = new("test.references", "1.0.0", "Fixture", ReferencePackOrigin.User);
        public ReferenceDatasetInfo Dataset => ReferenceDataTests.Dataset;
        public int Count => Records.Count;
        public IReadOnlyList<string> Categories => [];
        public ReferenceRecord? Find(string key) => Records.FirstOrDefault(record => record.Key == key);
        public ReferencePage Page(ReferenceQuery query) => new(Records.Skip(query.Offset).Take(query.Limit).ToArray(), Count, query.Offset);
        public ReferencePage Search(ReferenceQuery query)
        {
            Queries.Add(query);
            return SearchOverride?.Invoke(query) ?? Page(query);
        }
    }
}
