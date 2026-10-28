using System.Text;
using SaveToolkit.Core.References;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.BaldursGate3.Tests;

public sealed class ReferenceViewTests
{
    [Fact]
    public void StarterPackHasAttributedExactKeysAndNoGameplayLimitsOrAssets()
    {
        var (plugin, store, context) = References();
        Assert.Empty(store.LoadErrors);
        var pack = Assert.Single(store.Packs);
        Assert.Equal("bg3.starter.en", pack.Id);
        Assert.Equal("en", pack.Info.Locale);
        Assert.Empty(pack.Info.GameBuilds);
        var templates = context.Catalog(Bg3References.Templates)!;
        Assert.Equal(4, templates.Count);
        Assert.Equal(3, context.Catalog(Bg3References.Origins)!.Count);
        foreach (var record in templates.Search(new ReferenceQuery()).Items)
        {
            Assert.Equal("CC-BY-SA-4.0", record.Dataset.Attribution.License);
            Assert.NotNull(record.Source);
            Assert.Contains("bg3.wiki", record.AttributionText, StringComparison.Ordinal);
            Assert.Null(record.Limit);
            Assert.Null(record.AssetId);
            Assert.Contains(record.Source!.Split("/w/")[0], string.Join(' ', plugin.Info.References), StringComparison.Ordinal);
        }

        Assert.Null(templates.Find("OBJ_GoldCoin")); // quantity or a similar Stats name is not identity
    }

    [Fact]
    public void PartyLabelsHavePerCellProvenanceAndRawOriginsRemainVisible()
    {
        var (plugin, _, context) = References();
        var json = Encoding.UTF8.GetBytes("""
            {"Active Party":{"Characters":[{"Origin":"Laezel","Level":1},{"Origin":"DarkUrge"},{"Origin":"unlisted"}]}}
            """);
        var file = MemorySaveSet.SingleFile("test.lsv", PackageFixture.Create(payloads: [("SaveInfo.json", json)]));
        using var document = plugin.Open(file, context);
        var party = Assert.Single(Assert.IsType<TabsNode>(document.BuildView()).Tabs, t => t.Title == "Party");
        var rows = Assert.Single(party.Children.OfType<TableNode>()).Rows;
        var label = Assert.IsType<FieldNode>(rows[0].Cells["name"]);
        Assert.Equal("Lae'zel", label.Value.AsString());
        Assert.Equal(label.Value, document.GetValue(label.ValueId));
        Assert.Contains("CC-BY-SA-4.0", label.Description, StringComparison.Ordinal);
        Assert.Contains("bg3.wiki", label.Description, StringComparison.Ordinal);
        Assert.True(label.Spec.ReadOnly);
        Assert.Equal("Laezel", Assert.IsType<FieldNode>(rows[0].Cells["origin"]).Value.AsString());
        Assert.Equal("The Dark Urge (origin)", Assert.IsType<FieldNode>(rows[1].Cells["name"]).Value.AsString());
        Assert.Equal(Confidence.Unknown, Assert.IsType<FieldNode>(rows[2].Cells["name"]).Confidence);
        Assert.Equal(file.ReadAllBytes(), Assert.Single(document.Serialize()).Content.ToArray());

        using var missingPack = plugin.Open(file);
        var rawParty = Assert.Single(Assert.IsType<TabsNode>(missingPack.BuildView()).Tabs, t => t.Title == "Party");
        var raw = Assert.IsType<FieldNode>(Assert.Single(rawParty.Children.OfType<TableNode>()).Rows[0].Cells["name"]);
        Assert.Equal("Laezel", raw.Value.AsString());
        Assert.Equal(Confidence.Unknown, raw.Confidence);
    }

    [Fact]
    public void ItemCandidatesKeepDuplicateTemplatesAndMissingDataWithoutInventingOwnership()
    {
        var (_, _, context) = References();
        var inspection = Items(context);
        Assert.Empty(inspection.Build().OfType<TableNode>()); // no eager item rows
        var rows = Assert.Single(inspection.Expand("saved-items/0/3").OfType<TableNode>()).Rows;
        Assert.Equal(3, rows.Select(row => row.Key).Distinct().Count());
        foreach (var row in rows.Take(2))
        {
            var name = Assert.IsType<FieldNode>(row.Cells["name"]);
            Assert.Equal("Potion of Healing", name.Value.AsString());
            Assert.Contains("Potion_of_Healing", name.Description, StringComparison.Ordinal);
            Assert.Contains("CC-BY-SA-4.0", name.Description, StringComparison.Ordinal);
            Assert.Equal(Confidence.Inferred, name.Confidence);
            Assert.Equal("Unknown / unknown", Assert.IsType<TextNode>(row.Cells["ownership"]).Text);
        }

        Assert.Equal(Confidence.Unknown, Assert.IsType<FieldNode>(rows[2].Cells["name"]).Confidence);
        Assert.Throws<KeyNotFoundException>(() => inspection.Expand("saved-items/0/4"));
        Assert.Throws<KeyNotFoundException>(() => inspection.Expand("saved-items/-1/1"));
        var raw = Assert.Single(Items(IReferenceContext.Empty).Expand("saved-items/0/1").OfType<TableNode>()).Rows[0];
        Assert.Equal("d47006e9-8a51-453d-b200-9e0d42e9bbab", Assert.IsType<FieldNode>(raw.Cells["name"]).Value.AsString());
    }

    [Fact]
    public void UnknownNativeBuildNeverUsesABuildSpecificCatalog()
    {
        var (_, _, context) = References();
        var catalog = context.Catalog(Bg3References.Templates)!;
        var references = new Bg3References(new CatalogContext(new BuildSpecificCatalog(catalog)));
        Assert.Null(references.Template("d47006e9-8a51-453d-b200-9e0d42e9bbab"));
    }

    private static SavedItemInspection Items(IReferenceContext context)
    {
        ResourceAttribute template = new("CurrentTemplate", LarianAttributeType.UUID,
            Convert.FromHexString("E90670D4518A3D4500B20D9EE942ABBB"));
        ResourceAttribute stats = new("Stats", LarianAttributeType.FixedString, "OBJ_Potion_Healing\0"u8.ToArray());
        var root = new ResourceNode("Items", -1);
        var first = new ResourceNode("Item", 0);
        first.Attributes.AddRange([0, 1]);
        var second = new ResourceNode("Item", 0);
        second.Attributes.AddRange([0, 1]);
        var missing = new ResourceNode("Item", 0);
        var resource = new LarianResource("synthetic", [root, first, second, missing], [template, stats], [0]);
        return new SavedItemInspection([new DocumentEntry("Globals.lsf", [], resource)], new Bg3References(context), Bind);
    }

    private static FieldNode Bind(string id, string label, FieldValue value) =>
        new(id, label, new TextSpec { ReadOnly = true }, value);

    internal static (BaldursGate3Plugin Plugin, ReferencePackStore Store, IReferenceContext Context) References()
    {
        var plugin = new BaldursGate3Plugin();
        var store = new ReferencePackStore([plugin]);
        return (plugin, store, store.Pin(plugin, "PPSA18463"));
    }

    private sealed class CatalogContext(IReferenceCatalog catalog) : IReferenceContext
    {
        public IReadOnlyList<ReferencePackInfo> Packs => [catalog.Pack];
        public IReferenceCatalog? Catalog(string catalogId, string? gameBuild = null) => catalog;
        public AssetContent? GetAsset(string assetId) => null;
        public ReferenceDatasetInfo? AssetProvenance(string assetId) => null;
    }

    private sealed class BuildSpecificCatalog(IReferenceCatalog inner) : IReferenceCatalog
    {
        public string Id => inner.Id;
        public ReferencePackInfo Pack => inner.Pack with { GameBuilds = ["different-native-build"] };
        public ReferenceDatasetInfo Dataset => inner.Dataset;
        public int Count => inner.Count;
        public IReadOnlyList<string> Categories => inner.Categories;
        public ReferenceRecord? Find(string key) => inner.Find(key);
        public ReferencePage Search(ReferenceQuery query) => inner.Search(query);
    }
}
