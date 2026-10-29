using SaveToolkit.Sdk;
using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.Gearbox.Tests;

public sealed class ReferenceCompatibilityTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void VersionMismatchedNameOverride_FallsBackAndIsIneligible(bool wonderlands)
    {
        var db = SerialDatabase.For(wonderlands);
        var original = db.Records("balances").First(r => r.Attributes["displayKind"] == "family");
        var attributes = original.Attributes.ToDictionary();
        attributes["databaseVersion"] = "999";
        var candidate = original with { Name = "Wrong database label", Attributes = attributes };
        var context = new SingleCatalogContext(new SingleRecordCatalog(db.Pack, candidate));
        var resolver = new ItemReferences(wonderlands, context);
        Assert.Equal(original, resolver.Balance(original.Key));
        var title = wonderlands ? "PPSA01493" : "PPSA01463";
        var container = GearboxContainer.Parse(GearboxTestData.CreateContainer(wonderlands, false), title, false);
        using var document = new GearboxDocument("1", title, container, container.Decode(), context);
        var tabs = Assert.IsType<TabsNode>(document.BuildView());
        var action = tabs.Tabs.Single(t => t.Title == "Inventory").Children.OfType<ActionNode>().Single(a => a.ActionId == "catalogue/inspect");
        var spec = Assert.IsType<ReferenceSpec>(Assert.Single(action.Parameters).Spec);
        var assessment = spec.Assess!(candidate);
        Assert.False(assessment.Selectable);
        Assert.NotEmpty(assessment.Reasons);
        attributes = original.Attributes.ToDictionary();
        candidate = candidate with { Attributes = attributes };
        resolver = new ItemReferences(wonderlands, new SingleCatalogContext(new SingleRecordCatalog(db.Pack, candidate)));
        Assert.Equal("Wrong database label", resolver.Balance(original.Key)!.Name);
        Assert.True(spec.Assess(candidate).Selectable);
    }

    private sealed class SingleCatalogContext(IReferenceCatalog catalog) : IReferenceContext
    {
        public IReadOnlyList<ReferencePackInfo> Packs => [catalog.Pack];
        public IReferenceCatalog? Catalog(string catalogId, string? gameBuild = null) => catalogId == catalog.Id ? catalog : null;
        public AssetContent? GetAsset(string assetId) => null;
        public ReferenceDatasetInfo? AssetProvenance(string assetId) => null;
    }

    private sealed class SingleRecordCatalog(ReferencePackInfo pack, ReferenceRecord record) : IReferenceCatalog
    {
        public string Id => "balances";
        public ReferencePackInfo Pack => pack;
        public ReferenceDatasetInfo Dataset => record.Dataset;
        public int Count => 1;
        public IReadOnlyList<string> Categories => [];
        public ReferenceRecord? Find(string key) => key == record.Key ? record : null;
        public ReferencePage Search(ReferenceQuery query) => new([record], 1, 0);
    }
}
