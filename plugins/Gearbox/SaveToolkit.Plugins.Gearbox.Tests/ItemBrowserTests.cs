using System.Text.Json;
using SaveToolkit.Core.References;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Core.Storage;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.Gearbox.Tests;

public sealed class ItemBrowserTests
{
    [Theory]
    [InlineData("PPSA01463", 2803, 11453)]
    [InlineData("PPSA01493", 2030, 13674)]
    public void PacksAndPickers_AreAttributedTitleScopedAndInspectionDoesNotDirty(string title, int balanceCount, int indexCount)
    {
        var plugin = new GearboxPlugin();
        var store = new ReferencePackStore([plugin]);
        Assert.Empty(store.LoadErrors);
        Assert.Equal(2, store.Packs.Count);
        var references = store.Pin(plugin, title);
        var pack = Assert.Single(references.Packs);
        Assert.Equal(balanceCount, references.Catalog("balances")!.Count);
        Assert.Equal(indexCount, references.Catalog("serial-indices")!.Count);
        Assert.All(pack.Datasets, dataset =>
        {
            Assert.Equal("Zlib", dataset.Attribution.License);
            Assert.Contains("2026-10-28", dataset.Attribution.ToShortText());
        });
        var file = Character(title);
        using var document = plugin.Open(file, references);
        using var session = new DocumentSession("gearbox-picker", file, plugin, document, references: references);
        _ = session.BuildView();
        foreach (var (action, parameter, expectedTotal) in new[] { ("catalogue/inspect", "balance", balanceCount), ("catalogue/inspect-part", "part", indexCount) })
        {
            var candidates = session.SearchReferences(parameter, new ReferenceQuery { Offset = 201, Limit = 3 }, actionId: action);
            Assert.Equal(expectedTotal, candidates.Total);
            Assert.Equal(3, candidates.Items.Count);
            var candidate = candidates.Items[0];
            Assert.NotNull(candidate.Dataset);
            Assert.True(candidate.Eligibility.Selectable);
            var result = session.InvokeAction(action, new Dictionary<string, JsonElement> { [parameter] = JsonSerializer.SerializeToElement(candidate.RawId) });
            Assert.True(result.Ok);
            Assert.Contains(candidate.RawId, result.Message);
            Assert.Contains("Zlib", result.Message);
            Assert.False(result.State.IsDirty);
            Assert.False(result.State.CanUndo);
            var rejected = session.InvokeAction(action, new Dictionary<string, JsonElement> { [parameter] = JsonSerializer.SerializeToElement("not-a-real-id") });
            Assert.False(rejected.Ok);
        }
        Assert.Equal(file.ReadAllBytes(), document.Serialize().Single().Content.ToArray());
    }

    [Theory]
    [InlineData("PPSA01463")]
    [InlineData("PPSA01493")]
    public void FullInventoryAndCatalogueQueries_PageFilterSortAndExpandReadOnlyDetails(string title)
    {
        var file = Character(title);
        var plugin = new GearboxPlugin();
        using var document = plugin.Open(file);
        using var session = new DocumentSession("gearbox-browser", file, plugin, document);
        _ = session.BuildView();
        var first = session.QueryCollection("inventory_items", new() { Limit = 1 });
        Assert.True(first.TotalCount > 1);
        var last = session.QueryCollection("inventory_items", new() { Limit = 1, SortColumn = "index", Descending = true });
        Assert.NotEqual(first.Rows[0].Key, last.Rows[0].Key);
        Assert.Equal((first.TotalCount - 1).ToString(System.Globalization.CultureInfo.InvariantCulture), Assert.IsType<TextNode>(last.Rows[0].Cells["index"]).Text);
        var name = Assert.IsType<FieldNode>(last.Rows[0].Cells["name"]);
        Assert.True(name.Spec.ReadOnly);
        Assert.NotNull(name.Confidence);
        Assert.Equal(name.Value, document.GetValue(name.ValueId));
        Assert.Contains("Zlib", name.Description);
        var query = new QueryableCollectionQuery { Text = name.Value.AsString(), Limit = 1 };
        Assert.True(session.QueryCollection("inventory_items", query).FilteredCount > 0);
        var category = Assert.IsType<FieldNode>(last.Rows[0].Cells["category"]).Value.AsString();
        var filtered = session.QueryCollection("inventory_items", new() { Filters = new Dictionary<string, string> { ["category"] = category } });
        Assert.All(filtered.Rows, row => Assert.Equal(category, Assert.IsType<FieldNode>(row.Cells["category"]).Value.AsString()));
        var lazy = Assert.IsType<SectionNode>(last.Rows[0].Cells["detail"]);
        var details = session.Expand(lazy.LazyId!);
        Assert.Contains(details.OfType<TextNode>(), node => node.Text.StartsWith("BL3(", StringComparison.Ordinal));
        var raw = details.OfType<FieldNode>().First();
        Assert.True(raw.Spec.ReadOnly);
        Assert.False(session.ApplyEdits([new(raw.ValueId, JsonSerializer.SerializeToElement("changed"))]).Ok);
        var catalogue = session.QueryCollection("catalogue", new() { Offset = 1000, Limit = 7, SortColumn = "name" });
        Assert.Equal(7, catalogue.Rows.Count);
        Assert.True(catalogue.TotalCount > 2000);
        var target = catalogue.Rows[^1].Key;
        var exact = session.QueryCollection("catalogue", new() { Text = target, Limit = 200 });
        Assert.Contains(exact.Rows, r => r.Key == target);
        Assert.Throws<ArgumentException>(() => session.QueryCollection("catalogue", new() { SortColumn = "missing" }));
        Assert.Equal(file.ReadAllBytes(), document.Serialize().Single().Content.ToArray());
        Assert.False(session.State.IsDirty);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DuplicateAndInvalidSerials_KeepDistinctStableRowsAndUnrelatedEditing(bool wonderlands)
    {
        var title = wonderlands ? "PPSA01493" : "PPSA01463";
        var file = Character(title);
        var container = GearboxContainer.Parse(file.ReadAllBytes(), title, false);
        var root = container.Decode();
        var items = root.Children("inventory_items").ToArray();
        Assert.True(items.Length >= 3);
        var first = (byte[])items[0].Last("item_serial_number")!.Value!;
        items[1].Last("item_serial_number")!.Set(first.ToArray());
        items[2].Last("item_serial_number")!.Set(new byte[] { 4, 0 });
        var syntheticFile = container.Encode(root);
        using var document = new GearboxDocument(file.RelativePath, title,
            GearboxContainer.Parse(syntheticFile, title, false), root, IReferenceContext.Empty);
        _ = document.BuildView();
        var queryable = (IQueryableDocument)document;
        var before = queryable.Query("inventory_items", new() { Limit = 200 });
        Assert.Equal(before.Rows.Count, before.Rows.Select(r => r.Key).Distinct().Count());
        Assert.Equal(Assert.IsType<FieldNode>(before.Rows[0].Cells["name"]).Value, Assert.IsType<FieldNode>(before.Rows[1].Cells["name"]).Value);
        Assert.Equal("Unknown / invalid", Assert.IsType<TextNode>(before.Rows[2].Cells["status"]).Text);
        var lazy = Assert.IsType<SectionNode>(before.Rows[2].Cells["detail"]);
        Assert.NotEmpty(document.ExpandNode(lazy.LazyId!));
        var originalMoney = document.GetValue("currency/618814354");
        Assert.True(document.ApplyEdits([new("currency/618814354", FieldValue.Of(originalMoney.AsInt64() + 1))]).Succeeded);
        _ = document.BuildView();
        Assert.Equal(before.Rows.Select(r => r.Key), queryable.Query("inventory_items", new() { Limit = 200 }).Rows.Select(r => r.Key));
        Assert.True(document.ApplyEdits([new("currency/618814354", originalMoney)]).Succeeded);
        Assert.Equal(syntheticFile, document.Serialize().Single().Content.ToArray());
    }

    private static DiskSaveFile Character(string title) => SampleSaves.Load(title).AllDiskFiles
        .First(f => f.FileName == "1" && !f.Directory.IsBackup);
}
