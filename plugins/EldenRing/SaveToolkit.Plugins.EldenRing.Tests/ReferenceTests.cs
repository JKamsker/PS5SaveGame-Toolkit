using SaveToolkit.Core.References;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SaveToolkit.Plugins.EldenRing.Tests;

public sealed class ReferenceTests
{
    [Fact]
    public void BuildSpecificPacks_RequireTheExactRegulationFingerprintAndHostUsesTheSameSelection()
    {
        var fixture = new SyntheticSave([0]);
        var plugin = new EldenRingPlugin();
        var store = new ReferencePackStore([plugin]);
        store.Import(Pack("test.wrong-build", "01.012.000", "Wrong build label"), "wrong.zip");
        var generic = store.Pin(plugin, "PPSA03169");
        using (var document = plugin.Open(fixture.File(), generic))
        {
            var page = ((IQueryableDocument)document).Query("catalog/items", new() { Text = "Rowa Fruit" });
            Assert.Equal("Rowa Fruit", Assert.IsType<TextNode>(Assert.Single(page.Rows).Cells["name"]).Text);
            Assert.Equal("eldenring.sample-identities.en", generic.CatalogForHost("eldenring-items")!.Pack.Id);
        }
        var fingerprint = Convert.ToHexStringLower(SHA256.HashData(fixture.Bytes.AsSpan(0x1960070, 0x240010)));
        store.Import(Pack("test.exact-build", fingerprint, "Exact synthetic label"), "exact.zip");
        var exact = store.Pin(plugin, "PPSA03169");
        using var matched = plugin.Open(fixture.File(), exact);
        var selected = ((IQueryableDocument)matched).Query("catalog/items", new());
        Assert.Equal(1, selected.TotalCount);
        Assert.Equal("Exact synthetic label", Assert.IsType<TextNode>(Assert.Single(selected.Rows).Cells["name"]).Text);
        Assert.Equal("test.exact-build", exact.CatalogForHost("eldenring-items")!.Pack.Id);
    }

    [Fact]
    public void EmbeddedPack_IsVersionedPartialAttributedAndTitleScoped()
    {
        var plugin = new EldenRingPlugin();
        var store = new ReferencePackStore([plugin]);
        Assert.Empty(store.LoadErrors);
        var pack = Assert.Single(store.Packs);
        Assert.Equal("1.0.0", pack.Info.Version);
        var context = store.Pin(plugin, "PPSA03169");
        var catalog = Assert.IsAssignableFrom<IReferenceCatalog>(context.Catalog("eldenring-items"));
        Assert.Equal(6, catalog.Count);
        var record = Assert.IsType<ReferenceRecord>(catalog.Find("0x400050F0"));
        Assert.Equal("Rowa Fruit", record.Name);
        Assert.Null(record.Limit);
        Assert.Null(record.AssetId);
        Assert.Contains("CC0", record.AttributionText);
        Assert.Contains("single sampled", record.Description);
        Assert.Null(store.Pin(plugin, "PPSA04610").Catalog("eldenring-items"));
    }

    [Fact]
    public void PickerInspection_ReturnsProvenanceAndNeverWrites()
    {
        var fixture = new SyntheticSave([0]);
        var plugin = new EldenRingPlugin();
        var references = new ReferencePackStore([plugin]).Pin(plugin, "PPSA03169");
        using var document = plugin.Open(fixture.File(), references);
        var tabs = Assert.IsType<TabsNode>(document.BuildView());
        var action = Assert.IsType<ActionNode>(tabs.Tabs.Single(tab => tab.Title == "Reference catalog").Children.Last());
        Assert.Equal("eldenring-items", Assert.IsType<ReferenceSpec>(Assert.Single(action.Parameters).Spec).CatalogId);
        Assert.False(action.Parameters[0].Spec.ReadOnly);
        var result = document.InvokeAction(action.ActionId, new Dictionary<string, FieldValue> { ["item"] = FieldValue.Of("0x400050F0") });
        Assert.Empty(result.Changes);
        Assert.True(result.Undoable);
        Assert.False(result.StructureChanged);
        Assert.Contains("Rowa Fruit", result.Message);
        Assert.Contains("Source:", result.Message);
        Assert.Throws<ArgumentException>(() => document.InvokeAction(action.ActionId,
            new Dictionary<string, FieldValue> { ["item"] = FieldValue.Of("0x4000FFFF") }));
        Assert.True(fixture.Bytes.AsSpan().SequenceEqual(Assert.Single(document.Serialize()).Content.Span));
    }

    [Fact]
    public void CatalogSortSearchAndExactFilters_CoverUnloadedPages()
    {
        var source = new LargeCatalog(803);
        var browser = new ReferenceBrowser(source);
        var page = browser.Query(new() { SortColumn = "name", Descending = true, Offset = 1, Limit = 2 });
        Assert.Equal(803, page.FilteredCount);
        Assert.Equal(["Item 0801", "Item 0800"], page.Rows.Select(row => Assert.IsType<TextNode>(row.Cells["name"]).Text));
        var filtered = browser.Query(new() { Filters = new Dictionary<string, string> { ["category"] = "rare" }, Limit = 200 });
        Assert.Equal(402, filtered.FilteredCount);
        Assert.Equal(200, filtered.Rows.Count);
        Assert.Single(browser.Query(new() { Text = "Item 0802" }).Rows);
        Assert.Equal(803, browser.Collection.TotalCount);
    }

    private sealed class LargeCatalog : IReferenceCatalog
    {
        private readonly ReferenceRecord[] _records;
        public LargeCatalog(int count) => _records = Enumerable.Range(0, count).Select(index =>
            new ReferenceRecord($"id-{index:D4}", $"Item {index:D4}") { Category = index % 2 == 0 ? "rare" : "common", Dataset = Dataset }).ToArray();
        public string Id => "eldenring-items";
        public ReferencePackInfo Pack { get; } = new("test.names", "1.0.0", "Synthetic names", ReferencePackOrigin.Embedded);
        public ReferenceDatasetInfo Dataset { get; } = new("test.names", "eldenring-items", ReferenceDatasetKind.Records,
            new("https://example.org/synthetic", "Test authors", "CC0-1.0", "https://creativecommons.org/publicdomain/zero/1.0/", new(2026, 10, 28), "Synthetic test data"));
        public int Count => _records.Length;
        public IReadOnlyList<string> Categories => ["rare", "common"];
        public ReferenceRecord? Find(string key) => _records.FirstOrDefault(record => record.Key == key);
        public ReferencePage Search(ReferenceQuery query) => new(_records.Skip(query.Offset).Take(query.Limit).ToArray(), Count, query.Offset);
    }

    private static byte[] Pack(string id, string build, string name)
    {
        var records = JsonSerializer.SerializeToUtf8Bytes(new { records = new[] { new { id = "0x400050F0", name, category = "goods" } } });
        var manifest = new
        {
            schema = 1, id, version = "9.0.0", title = "Synthetic test pack", plugins = new[] { "eldenring" },
            titleIds = new[] { "PPSA03169" }, gameBuilds = new[] { build }, locale = "en",
            datasets = new[]
            {
                new
                {
                    id = "eldenring-items", kind = "records", title = "Synthetic identities",
                    files = new[] { new { path = "items.json", sha256 = Convert.ToHexStringLower(SHA256.HashData(records)) } },
                    attribution = new { sourceUrl = "https://example.org/synthetic", author = "Test authors", license = "CC0-1.0",
                        licenseUrl = "https://creativecommons.org/publicdomain/zero/1.0/", retrieved = "2026-10-28", modifications = "Synthetic test data" },
                },
            },
        };
        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, true))
        {
            using (var entry = zip.CreateEntry("pack.json").Open()) entry.Write(JsonSerializer.SerializeToUtf8Bytes(manifest));
            using (var entry = zip.CreateEntry("items.json").Open()) entry.Write(records);
        }
        return stream.ToArray();
    }
}
