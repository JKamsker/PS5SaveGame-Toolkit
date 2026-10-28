using SaveToolkit.Core.References;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.Insomniac.Tests;

public sealed class ReadOnlyViewTests
{
    [Fact]
    public void EmbeddedDatasetsHaveExactKeysAndCompleteAttribution()
    {
        var plugin = new InsomniacPlugin();
        var store = new ReferencePackStore([plugin]);
        Assert.Empty(store.LoadErrors);
        var context = store.Pin(plugin, "PPSA01474");
        var weapons = context.Catalog("rift-weapons")!;
        Assert.Equal(20, weapons.Count);
        Assert.Equal(57, context.Catalog("rift-armor")!.Count);
        Assert.Equal(20, context.Catalog("rift-world")!.Count);
        Assert.Equal(86, context.Catalog("rift-schema")!.Count);
        Assert.Equal(5, context.Catalog("rift-item-types")!.Count);
        foreach (var record in context.Catalog("rift-item-types")!.Search(new()).Items)
            Assert.Equal(uint.Parse(record.Key, System.Globalization.NumberStyles.HexNumber,
                System.Globalization.CultureInfo.InvariantCulture), DdlReader.NameHash(System.Text.Encoding.UTF8.GetBytes(record.Name)));
        foreach (var record in weapons.Search(new()).Items)
        {
            Assert.Equal("CC-BY-SA-3.0", record.Dataset.Attribution.License);
            Assert.Null(record.AssetId);
            Assert.Null(record.Limit);
            Assert.Contains("inferred", record.Description, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(record.Source!, plugin.Info.References);
        }
        foreach (var record in context.Catalog("rift-armor")!.Search(new() { Limit = 200 }).Items)
        {
            Assert.Equal("CC-BY-SA-3.0", record.Dataset.Attribution.License);
            Assert.EndsWith("?oldid=402364", record.Source);
            Assert.Contains(record.Source!, plugin.Info.References);
            Assert.False(string.IsNullOrWhiteSpace(record.Attributes["saveId"]));
            Assert.True(record.Attributes["slot"] is "Head" or "Torso" or "Legs");
            Assert.Null(record.Limit);
            Assert.Null(record.AssetId);
        }
        Assert.All(context.Catalog("rift-world")!.Search(new()).Items,
            record => Assert.Equal("CC0-1.0", record.Dataset.Attribution.License));
        Assert.Equal("Blackhole Storm", weapons.Find("configs\\equipment\\wpn_warprifle_item.config")!.Name);
        Assert.Null(weapons.Find("configs\\equipment\\wpn_unknown_item.config"));
        Assert.Null(store.Pin(plugin, "PPSA00000").Catalog("rift-weapons"));
    }

    [Fact]
    public void QuickSelectFieldsUseReadOnlyPickersAndPreserveUnknownPaths()
    {
        var plugin = new InsomniacPlugin();
        var context = new ReferencePackStore([plugin]).Pin(plugin, "PPSA01474");
        var file = PlayerCorpusTests.Find("i29slot0-s-autosave", 0);
        using var document = plugin.Open(file, context);
        var fields = Tab(document, "Loadout").Children.OfType<FieldNode>().ToArray();
        Assert.Equal(4, fields.Length);
        Assert.All(fields, f =>
        {
            var spec = Assert.IsType<ReferenceSpec>(f.Spec);
            Assert.Equal("rift-weapons", spec.CatalogId);
            Assert.True(spec.AllowCustom);
            Assert.True(spec.ReadOnly);
            Assert.Equal(f.Value, document.GetValue(f.ValueId));
        });
        var named = fields.Where(f => f.Confidence == Confidence.Inferred).ToArray();
        Assert.Equal(4, named.Length);
        Assert.All(named, f => Assert.Contains("CC-BY-SA-3.0", f.Description));
        var warp = Assert.Single(fields, f => f.Value.AsString().EndsWith("wpn_warprifle_item.config", StringComparison.Ordinal));
        Assert.False(document.ApplyEdits([new(warp.ValueId, named[0].Value)]).Succeeded);
        Assert.Equal(file.ReadAllBytes(), Assert.Single(document.Serialize()).Content.ToArray());

        using var noPacks = plugin.Open(file);
        Assert.All(Tab(noPacks, "Loadout").Children.OfType<FieldNode>(), f => Assert.Equal(Confidence.Unknown, f.Confidence));
    }

    [Fact]
    public void PreferencesAreGroupedWithoutInventingNumericMenuLabels()
    {
        var file = SampleSaves.Load("PPSA01474").AllDiskFiles.First(f => f.FileName == "i29-userprefs.save");
        using var document = new InsomniacPlugin().Open(file);
        var preferences = Tab(document, "Preferences");
        Assert.Contains(preferences.Children.OfType<SectionNode>(), s => s.Title == "Audio");
        var fields = preferences.Children.OfType<SectionNode>().SelectMany(s => s.Children).OfType<FieldNode>().ToArray();
        Assert.True(fields.Length > 100);
        Assert.All(fields, f =>
        {
            Assert.Equal(f.Value.Kind != FieldValueKind.Bool, f.Spec.ReadOnly);
            Assert.IsNotType<EnumSpec>(f.Spec);
            Assert.NotNull(f.Warning);
        });
        Assert.Contains(fields, f => f.ValueId == "preferences/MasterVolume");
    }

    [Fact]
    public void SystemAndExplorerDetailsExpandWithVisibleContentIncludingEmptyCollections()
    {
        using var document = new InsomniacPlugin().Open(PlayerCorpusTests.Find("i29slot0-s-autosave", 1));
        var tabs = Assert.IsType<TabsNode>(document.BuildView());
        var systems = Assert.Single(tabs.Tabs, t => t.Title == "Systems");
        Assert.Equal(12, systems.Children.Count);
        foreach (var root in systems.Children.Cast<SectionNode>())
            Assert.NotEmpty(document.ExpandNode(root.LazyId!));
        var sectionRows = Assert.Single(Assert.Single(tabs.Tabs, t => t.Title == "Sections").Children.OfType<TableNode>()).Rows;
        foreach (var row in sectionRows)
        {
            var explorer = Assert.IsType<SectionNode>(row.Cells["detail"]);
            Assert.NotEmpty(document.ExpandNode(explorer.LazyId!));
        }
        _ = document.ExpandNode("system/77f00be8");
        var empty = document.ExpandNode("ddl/77f00be8/CollectedBoltHashes");
        Assert.Contains(empty.OfType<TextNode>(), text => text.Text.Contains("0 entries", StringComparison.Ordinal));
    }

    [Fact]
    public void SystemsTabKeepsLazyRootsCollapsedAndEagerSurfaceBounded()
    {
        using var document = new InsomniacPlugin().Open(PlayerCorpusTests.Find("i29slot0-s-autosave", 1));
        var systems = Assert.Single(Assert.IsType<TabsNode>(document.BuildView()).Tabs, t => t.Title == "Systems");

        Assert.All(systems.Children, node =>
        {
            var root = Assert.IsType<SectionNode>(node);
            Assert.True(root.Collapsed);
            Assert.NotNull(root.LazyId);
        });
        Assert.InRange(CountEagerNodes(systems.Children), 1, 100);

        static int CountEagerNodes(IEnumerable<ViewNode> nodes) => nodes.Sum(node =>
            node is SectionNode section ? 1 + CountEagerNodes(section.Children) : 1);
    }

    [Fact]
    public void WorldTablesShowStoredKeysAndHonestConfidence()
    {
        using var document = new InsomniacPlugin().Open(PlayerCorpusTests.Find("i29slot0-s-autosave", 0));
        var tables = Tab(document, "World").Children.OfType<TableNode>().ToArray();
        var maps = Assert.Single(tables, t => t.Title == "Fog-of-war areas");
        Assert.Equal(15, maps.Rows.Count);
        Assert.All(maps.Rows, row =>
        {
            Assert.Equal(Confidence.Verified, Assert.IsType<FieldNode>(row.Cells["columns"]).Confidence);
            Assert.Equal(Confidence.Inferred, Assert.IsType<FieldNode>(row.Cells["bits"]).Confidence);
        });
        Assert.NotEmpty(Assert.Single(tables, t => t.Title == "Objective graphs").Rows);
        Assert.Equal(47, Assert.Single(tables, t => t.Title == "Award records").Rows.Count);
    }

    private static Tab Tab(ISaveDocument document, string title) =>
        Assert.Single(Assert.IsType<TabsNode>(document.BuildView()).Tabs, t => t.Title == title);
}
