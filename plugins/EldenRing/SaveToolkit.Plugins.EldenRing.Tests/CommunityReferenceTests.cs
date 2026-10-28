using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using SaveToolkit.Core.References;
using SaveToolkit.Sdk.References;

namespace SaveToolkit.Plugins.EldenRing.Tests;

public sealed class CommunityReferenceTests
{
    private const string License = "No licence published; reused with attribution per owner decision 2026-10-28";

    [Theory]
    [InlineData(CommunityReferences.ItemsCatalogId, "items.json", 3221, "0087caa969e0f4a9d493efc79142f870f290bf41a0bc1113941ec3b46bdb3bc6")]
    [InlineData(CommunityReferences.FlagsCatalogId, "flags.json", 1748, "fef561839b1b50a845a046e8e23d60879542bdaa7f390d7b16093f07d5bf868a")]
    [InlineData(CommunityReferences.ParamLayoutsCatalogId, "param-layouts.json", 1072, "62664fc7a1eb9651b7a8b7e89e4603f38a081f899657c471b69b8b630d5d3e26")]
    public void ApprovedDatasetsKeepPinnedContentAttributionAndNoGameAssets(string id, string file, int count, string hash)
    {
        var plugin = new EldenRingPlugin();
        var store = new ReferencePackStore([plugin]);
        Assert.Empty(store.LoadErrors);
        var catalog = store.Pin(plugin, "PPSA03169").Catalog(id)!;
        var assembly = typeof(EldenRingPlugin).Assembly;
        var resource = Assert.Single(assembly.GetManifestResourceNames(), name =>
            name.Replace('\\', '/') == $"refpacks/eldenring.community.en/{file}");
        using var stream = assembly.GetManifestResourceStream(resource)!;
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var json = reader.ReadToEnd().Replace("\r\n", "\n", StringComparison.Ordinal);
        Assert.Equal(hash, Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(json))));
        Assert.Single(json.TrimEnd('\n').Split('\n')); // Generated files must stay minified.
        Assert.Equal(count, catalog.Count);
        Assert.Equal(License, catalog.Dataset.Attribution.License);
        Assert.Equal(new DateOnly(2026, 10, 29), catalog.Dataset.Attribution.Retrieved);
        var rows = Read(catalog);
        Assert.Equal(count, rows.Select(row => row.Key).Distinct(StringComparer.Ordinal).Count());
        Assert.All(rows, row =>
        {
            Assert.Contains(row.Source ?? row.Dataset.Attribution.SourceUrl, plugin.Info.References);
            Assert.Contains(License, row.AttributionText);
            Assert.Contains("community.source.md", ReferenceBrowser.Details(row));
            Assert.DoesNotContain("sourceSha256", row.Attributes.Keys);
            Assert.DoesNotContain("sourceRevision", row.Attributes.Keys);
            Assert.Null(row.Description); // Dataset-wide uncertainty lives in attribution.
            Assert.Null(row.Limit);
            Assert.Null(row.AssetId);
            Assert.DoesNotContain("DisplayName", row.Attributes.Keys);
            Assert.DoesNotContain("Description", row.Attributes.Keys);
        });
    }

    [Fact]
    public void ItemMembershipKeepsInheritedGroupsAndDlcNamesWithoutSpawnQuantities()
    {
        var context = Context();
        var items = context.Catalog(CommunityReferences.ItemsCatalogId)!;
        Assert.Equal("Scadutree Fragment", items.Find("0x401EAB90")!.Name);
        var fruit = items.Find("0x400050F0")!;
        Assert.Equal("Crafting: Plant", fruit.Attributes["editorGroups"]);
        Assert.Equal("Give all crafting materials", fruit.Attributes["groups"]);
        var rows = Read(items);
        var memberships = rows.Where(row => row.Attributes.ContainsKey("editorGroups")).ToArray();
        Assert.Equal(19, memberships.SelectMany(row => row.Attributes["editorGroups"].Split(" | ")).Distinct().Count());
        Assert.Equal(544, memberships.Sum(row => row.Attributes["editorGroups"].Split(" | ").Length));
        Assert.Equal(545, memberships.Sum(row => int.Parse(row.Attributes["editorMembershipOccurrences"], CultureInfo.InvariantCulture)));
        Assert.All(rows, row => Assert.DoesNotContain(row.Attributes.Keys, key => key.Contains("quantity", StringComparison.OrdinalIgnoreCase)));
        Assert.Null(items.Find("0xFFFFFFFF"));
        Assert.Equal("upstream-sentinel", items.Find("tga:FFFFFFFF")!.Category);
    }

    [Fact]
    public void EveryOverlappingEventAgreesWithExistingSparseMapAndSourceGroupsStayExplicit()
    {
        var context = Context();
        var old = Read(context.Catalog(ProgressReferences.CatalogId)!).GroupBy(row => row.Attributes["eventId"])
            .ToDictionary(group => group.Key, group => group.First());
        var rows = Read(context.Catalog(CommunityReferences.FlagsCatalogId)!);
        var index = ProgressReferences.Read(context.Catalog(CommunityReferences.FlagsCatalogId));
        var entries = index.Regions.SelectMany(region => region.Entries).ToArray();
        Assert.Equal(1748, entries.Length);
        Assert.Contains("rejected 0", index.Notice);
        Assert.Equal(1340, rows.Count(row => row.Attributes["contentGroup"] == "Base game"));
        Assert.Equal(392, rows.Count(row => row.Attributes["contentGroup"] == "DLC1 (source group)"));
        Assert.Equal(16, rows.Count(row => row.Attributes["contentGroup"] == "DLC3 (source group)"));
        Assert.Equal(26, rows.Count(row => row.Attributes["flagRole"] == "NG+ persistence"));
        var overlap = entries.Where(entry => old.ContainsKey(entry.EventId.ToString(CultureInfo.InvariantCulture))).ToArray();
        Assert.Equal(488, overlap.Select(entry => entry.EventId).Distinct().Count());
        foreach (var entry in overlap)
        {
            var reference = old[entry.EventId.ToString(CultureInfo.InvariantCulture)];
            Assert.Equal(reference.Attributes["offset"], entry.Offset.ToString(CultureInfo.InvariantCulture));
            Assert.Equal(reference.Attributes["bit"], entry.Bit.ToString(CultureInfo.InvariantCulture));
        }
        Assert.All(index.Regions, region => Assert.InRange(region.Entries.Count, 1, 100));
    }

    [Theory]
    [InlineData("event-flag:76100:668b1d7db931", 0xCBE, 3)]
    [InlineData("event-flag:76800:24617cc88d44", 0xD16, 7)]
    [InlineData("event-flag:60730:8292c1c3c080", 0x53D, 5)]
    public void BaseDlcAndNgPersistenceVectorsUseExplicitBoundedAddresses(string key, int offset, int bit)
    {
        var entry = ProgressReferences.Parse(Context().Catalog(CommunityReferences.FlagsCatalogId)!.Find(key)!);
        Assert.NotNull(entry);
        Assert.Equal(offset, entry.Offset);
        Assert.Equal(bit, entry.Bit);
    }

    [Fact]
    public void ParamFactsKeepNamesAndTechnicalDeclarationsWithoutTreatingSchemaBoundsAsCaps()
    {
        var context = Context();
        var layouts = context.Catalog(CommunityReferences.ParamLayoutsCatalogId)!;
        Assert.Null(context.Catalog("eldenring-param-rows")); // Unused dump, never a native save-data join.
        Assert.Equal("117", layouts.Find("EquipParamGoods:schema")!.Attributes["fieldCount"]);
        Assert.Equal("s16 maxNum", layouts.Find("EquipParamGoods:field:21")!.Name);
        Assert.Equal("s16 maxRepositoryNum", layouts.Find("EquipParamGoods:field:83")!.Name);
        Assert.All(Read(layouts), row =>
        {
            Assert.DoesNotContain('=', row.Name); // Default values and editor bounds are deliberately excluded.
            Assert.DoesNotContain("Maximum", row.Attributes.Keys);
            Assert.DoesNotContain("Minimum", row.Attributes.Keys);
            Assert.Null(row.Limit);
        });
    }

    internal static ReferenceContext Context()
    {
        var plugin = new EldenRingPlugin();
        return new ReferencePackStore([plugin]).Pin(plugin, "PPSA03169");
    }

    internal static IReadOnlyList<ReferenceRecord> Read(IReferenceCatalog catalog)
    {
        var rows = new List<ReferenceRecord>();
        for (var offset = 0; offset < catalog.Count; offset += ReferenceQuery.MaxLimit)
            rows.AddRange(catalog.Search(new() { Offset = offset, Limit = ReferenceQuery.MaxLimit }).Items);
        return rows;
    }
}
