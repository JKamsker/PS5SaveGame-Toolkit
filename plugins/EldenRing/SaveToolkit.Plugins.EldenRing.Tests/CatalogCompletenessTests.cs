using System.Globalization;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using SaveToolkit.Core.References;
using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Views;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.EldenRing.Tests;

public sealed class CatalogCompletenessTests
{
    private const string Revision = "014107f0ca1cff867b5f7565cb79d6684f9b35df";

    [Fact]
    public void SampleInitialViewRemainsBelowTwoSecondTargetWithFullCatalog()
    {
        const int initialViewBudgetSeconds = 2;
        var file = SampleSaves.Load("PPSA03169").AllDiskFiles.First(file => file.FileName == "memory.dat" && !file.Directory.IsBackup);
        var timer = Stopwatch.StartNew();
        var plugin = new EldenRingPlugin();
        var references = new ReferencePackStore([plugin]).Pin(plugin, "PPSA03169");
        using var document = plugin.Open(file, references);
        var view = Assert.IsType<TabsNode>(document.BuildView());
        timer.Stop();
        Assert.Contains(view.Tabs, tab => tab.Title == "Reference catalog");
        Assert.True(timer.Elapsed < TimeSpan.FromSeconds(initialViewBudgetSeconds), $"Initial view took {timer.Elapsed}.");
    }

    // Digests of sorted canonical-key TAB original-upstream-name LF pairs, independently
    // calculated from all literal pairs in the pinned Rust files, including empty/sentinel rows.
    [Theory]
    [InlineData("weapon", "weapon", 2974, 98, "b1b6a1c05b0c0879f515eb4987217e28286c7208e7ac69cdbe27fc8732f3e03f")]
    [InlineData("goods", "item", 1808, 143, "93c4a4c0da6629454a22749e71625729265fc5f3842963eb8d7c17caa0d7354a")]
    [InlineData("armor", "armor", 653, 30, "87b800c0e005252dc22f1cdac387c547b3e46a69c33ad731dcab849b9d336b6e")]
    [InlineData("talisman", "accessory", 117, 1, "ca9b707e7ec673c784c07f045f99b246ab3dff8e526a44020933f51c5838d210")]
    [InlineData("ash", "aow", 218, 1, "308b5d84d8495fdd1dfb4a35a52b3f34ce27221d955f743c50179f245f464fd4")]
    public void EveryPinnedSourcePairIsPreservedWithItsOwnProvenance(string category, string table,
        int count, int unnamed, string expectedHash)
    {
        var plugin = new EldenRingPlugin();
        var store = new ReferencePackStore([plugin]);
        Assert.Empty(store.LoadErrors);
        var catalog = store.Pin(plugin, "PPSA03169").Catalog("eldenring-items")!;
        var rows = ReadCategory(catalog, category);
        Assert.Equal(count, rows.Count);
        Assert.Equal(unnamed, rows.Count(row => row.Attributes["nameStatus"] == "unnamed"));
        var originalPairs = new StringBuilder();
        foreach (var row in rows.OrderBy(row => row.Key, StringComparer.Ordinal))
        {
            originalPairs.Append(row.Key).Append('\t').Append(row.Attributes["upstreamName"]).Append('\n');
            Assert.Equal(Revision, row.Attributes["sourceRevision"]);
            Assert.Equal($"https://github.com/ClayAmore/ER-Save-Editor/blob/{Revision}/src/db/{table}_name.rs", row.Source);
            Assert.Contains(row.Source!, plugin.Info.References);
            Assert.Contains("ClayAmore", row.AttributionText);
            Assert.Equal(ProgressMapAssets.Rights, row.Dataset.Attribution.License);
            Assert.Contains("names remain MIT", row.Dataset.Attribution.Modifications);
            Assert.Equal("2026-10-29", row.Dataset.Attribution.Retrieved.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            Assert.Equal(64, row.Attributes["sourceSha256"].Length);
            Assert.Null(row.Limit);
            Assert.Equal(row.Attributes["upstreamName"] is "" ? row.Key : row.Attributes["upstreamName"], row.Name);
        }
        Assert.Equal(expectedHash, Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(originalPairs.ToString()))));
    }

    [Fact]
    public void IdSpacesStayDistinctAndUnrepresentableSentinelsNeverBecomeRealItemIds()
    {
        var plugin = new EldenRingPlugin();
        var catalog = new ReferencePackStore([plugin]).Pin(plugin, "PPSA03169").Catalog("eldenring-items")!;
        Assert.Equal("weapon", catalog.Find("0x000003E8")!.Category);
        Assert.Equal("armor", catalog.Find("0x100003E8")!.Category);
        Assert.Equal("talisman", catalog.Find("0x200003E8")!.Category);
        Assert.Equal("goods", catalog.Find("0x400003E8")!.Category);
        var sentinel = catalog.Find("goods:upstream:999999999")!;
        Assert.Equal("upstream-sentinel", sentinel.Attributes["identityStatus"]);
        Assert.Equal("", sentinel.Attributes["upstreamName"]);
        Assert.Null(catalog.Find("0x7B9AC9FF"));
        Assert.Null(catalog.Find("0x4000FFFF"));
    }

    [Fact]
    public void FullCatalogPagesRemainBoundedAndSearchIncludesAffinityVariants()
    {
        var fixture = new SyntheticSave([0]);
        var plugin = new EldenRingPlugin();
        var context = new ReferencePackStore([plugin]).Pin(plugin, "PPSA03169");
        using var document = plugin.Open(fixture.File(), context);
        var browser = (SaveToolkit.Sdk.Collections.IQueryableDocument)document;
        var keys = new HashSet<string>(StringComparer.Ordinal);
        for (var offset = 0; offset < 5770; offset += 200)
        {
            var page = browser.Query("catalog/items", new() { Offset = offset, Limit = 200, SortColumn = "id" });
            Assert.Equal(5770, page.TotalCount);
            Assert.InRange(page.Rows.Count, 1, 200);
            Assert.All(page.Rows, row => Assert.True(keys.Add(row.Key)));
        }
        Assert.Equal(5770, keys.Count);
        var variants = browser.Query("catalog/items", new() { Text = "Scimitar" });
        Assert.Equal(13, variants.Rows.Count(row => Assert.IsType<TextNode>(row.Cells["name"]).Text.Contains("Scimitar", StringComparison.Ordinal)));
        Assert.True(variants.FilteredCount >= 13); // Official descriptions can now add relevant text-search matches.
        Assert.Contains(variants.Rows, row => Assert.IsType<TextNode>(row.Cells["name"]).Text == "Heavy Scimitar");
        Assert.True(fixture.Bytes.AsSpan().SequenceEqual(Assert.Single(document.Serialize()).Content.Span));
    }

    private static List<ReferenceRecord> ReadCategory(IReferenceCatalog catalog, string category)
    {
        var rows = new List<ReferenceRecord>();
        for (var offset = 0; ; offset += ReferenceQuery.MaxLimit)
        {
            var page = catalog.Search(new() { Category = category, Offset = offset, Limit = ReferenceQuery.MaxLimit });
            rows.AddRange(page.Items);
            if (rows.Count >= page.Total) return rows;
            Assert.NotEmpty(page.Items);
        }
    }
}
