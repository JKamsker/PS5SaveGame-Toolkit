using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using SaveToolkit.Core.References;
using SaveToolkit.Sdk.References;

namespace SaveToolkit.Plugins.EldenRing.Tests;

public sealed class ProgressReferenceTests
{
    [Theory]
    [InlineData("grace", 314, "b441f50b80ae9a3170fc8076e68a45cac3aa3fd71b469c7c252e69e4d35cf1a2")]
    [InlineData("boss", 157, "79b3eb883303fc4d10f0e4a5804258928dde95f086adf4065747b524866de943")]
    [InlineData("map-control", 28, "4b7a7b26066323ec0785f2c02c1e939a29456008e0d89978ab8c7466a6aef061")]
    public void EveryPinnedNameAndEventPairIsPreserved(string category, int count, string expectedHash)
    {
        var plugin = new EldenRingPlugin();
        var store = new ReferencePackStore([plugin]);
        Assert.Empty(store.LoadErrors);
        var catalog = store.Pin(plugin, "PPSA03169").Catalog(ProgressReferences.CatalogId)!;
        Assert.Equal(499, catalog.Count);
        var entries = ProgressReferences.Read(catalog).Regions.SelectMany(region => region.Entries)
            .Where(entry => entry.Category == category).OrderBy(entry => entry.Key, StringComparer.Ordinal).ToArray();
        Assert.Equal(count, entries.Length);
        // Independently calculated from literal source symbol/name/event pairs, not generated JSON.
        var canonical = string.Concat(entries.Select(entry => $"{entry.Key}\t{entry.Reference.Attributes["upstreamName"]}\t{entry.EventId.ToString(CultureInfo.InvariantCulture)}\n"));
        Assert.Equal(expectedHash, Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))));
        Assert.All(entries, entry =>
        {
            Assert.Contains(entry.Reference.Source!, plugin.Info.References);
            Assert.Contains(entry.Reference.Attributes["lookupSource"], plugin.Info.References);
            Assert.Equal("MIT", entry.Reference.Dataset.Attribution.License);
            Assert.Equal(entry.Reference.Attributes["upstreamName"].Trim(), entry.Reference.Name);
            Assert.Equal("014107f0ca1cff867b5f7565cb79d6684f9b35df", entry.Reference.Attributes["sourceRevision"]);
            Assert.InRange(entry.Offset, 0, SemanticLayout.EventSize - 1);
            Assert.InRange(entry.Bit, 0, 7);
            Assert.Null(entry.Reference.AssetId);
            Assert.Null(entry.Reference.Limit);
        });
    }

    [Theory]
    [InlineData("grace:ChurchofElleh", 76100U, 0xCBE, 3, "Limgrave")]
    [InlineData("boss:GodricktheGrafted", 10000800U, 0x151C33, 7, "Limgrave")]
    [InlineData("map-control:LimgraveWest", 62010U, 0x5DD, 5, "Map controls (not necessarily collectible fragments)")]
    public void SparseLookupIsExplicitNotEventIdDividedByEight(string key, uint eventId, int offset, int bit, string region)
    {
        var entry = Assert.IsType<ProgressEntry>(ProgressReferences.Parse(Catalog().Find(key)!));
        Assert.Equal(eventId, entry.EventId);
        Assert.Equal(offset, entry.Offset);
        Assert.Equal(bit, entry.Bit);
        Assert.Equal(region, entry.Region);
        Assert.NotEqual(eventId / 8, (uint)entry.Offset);
    }

    [Theory]
    [InlineData("offset", "-1")]
    [InlineData("offset", "1833375")]
    [InlineData("offset", "4294967295")]
    [InlineData("bit", "8")]
    [InlineData("bit", "-1")]
    [InlineData("eventId", "4294967296")]
    [InlineData("regionId", "bad/path")]
    [InlineData("region", " ")]
    [InlineData("schema", "unsupported")]
    public void MalformedImportedMappingsAreSkippedWithDiagnostics(string key, string value)
    {
        var source = Catalog();
        var original = source.Find("grace:ChurchofElleh")!;
        var attributes = original.Attributes.ToDictionary();
        attributes[key] = value;
        var invalid = original with { Attributes = attributes };
        var index = ProgressReferences.Read(new ProgressTestCatalog(source, [invalid, original]));
        Assert.Empty(index.Regions);
        Assert.Contains("rejected 2", index.Notice);
    }

    [Fact]
    public void OversizedCatalogIsRefusedBeforeSearchAndLargeRegionsAreSplitWithoutLosingRows()
    {
        var catalog = Catalog();
        var tooLarge = new ProgressTestCatalog(catalog, [], 4097);
        Assert.Empty(ProgressReferences.Read(tooLarge).Regions);
        Assert.Equal(0, tooLarge.Searches);
        var template = catalog.Find("grace:ChurchofElleh")!;
        var rows = Enumerable.Range(0, 601).Select(index => template with { Key = $"grace:test{index:D4}" }).ToArray();
        var source = new ProgressTestCatalog(catalog, rows);
        var result = ProgressReferences.Read(source);
        Assert.Equal(7, result.Regions.Count);
        Assert.Equal(601, result.Regions.Sum(region => region.Entries.Count));
        Assert.All(result.Regions, region => Assert.InRange(region.Entries.Count, 1, 100));
        Assert.Equal(4, source.Searches);
        Assert.Contains("rejected 0", result.Notice);
    }

    [Fact]
    public void MissingPackAndInvalidPagingRemainBounded()
    {
        Assert.Empty(ProgressReferences.Read(null).Regions);
        Assert.Contains("No compatible", ProgressReferences.Read(null).Notice);
        var incomplete = new ProgressTestCatalog(Catalog(), [], 1);
        Assert.Empty(ProgressReferences.Read(incomplete).Regions);
        Assert.Equal(1, incomplete.Searches);
    }

    [Fact]
    public void AllTenStartingClassesHaveAttributedReferencesWithoutClaimingLegalMinima()
    {
        var plugin = new EldenRingPlugin();
        var context = new ReferencePackStore([plugin]).Pin(plugin, "PPSA03169");
        var classes = context.Catalog(ProgressReferences.ClassCatalogId)!;
        Assert.Equal(10, classes.Count);
        Assert.Equal("Warrior", classes.Find("1")!.Name);
        Assert.Equal("11", classes.Find("1")!.Attributes["vigor"]);
        Assert.Equal("16", classes.Find("1")!.Attributes["dexterity"]);
        foreach (var row in classes.Search(new() { Limit = 200 }).Items)
        {
            string[] stats = ["vigor", "mind", "endurance", "strength", "dexterity", "intelligence", "faith", "arcane"];
            Assert.Equal(int.Parse(row.Attributes["level"], CultureInfo.InvariantCulture),
                stats.Sum(stat => int.Parse(row.Attributes[stat], CultureInfo.InvariantCulture)) - 79);
            Assert.Contains(row.Source!, plugin.Info.References);
            Assert.Contains("not verified PS5", row.Description);
            Assert.Equal("MIT", row.Dataset.Attribution.License);
            Assert.Null(row.Limit);
        }
    }

    internal static IReferenceCatalog Catalog()
    {
        var plugin = new EldenRingPlugin();
        return new ReferencePackStore([plugin]).Pin(plugin, "PPSA03169").Catalog(ProgressReferences.CatalogId)!;
    }
}

internal sealed class ProgressTestCatalog(IReferenceCatalog source, IReadOnlyList<ReferenceRecord> rows, int? count = null) : IReferenceCatalog
{
    public string Id => source.Id;
    public ReferencePackInfo Pack => source.Pack;
    public ReferenceDatasetInfo Dataset => source.Dataset;
    public int Count => count ?? rows.Count;
    public IReadOnlyList<string> Categories => rows.Select(row => row.Category!).Distinct().ToArray();
    public ReferenceRecord? Find(string key) => rows.FirstOrDefault(row => row.Key == key);
    public int Searches { get; private set; }
    public ReferencePage Search(ReferenceQuery query)
    {
        Searches++;
        return new(rows.Skip(query.Offset).Take(query.Limit).ToArray(), rows.Count, query.Offset);
    }
}
