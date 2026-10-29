using SaveToolkit.Core.References;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.GenericJson.Tests;

public sealed class JsonSemanticReferencePerformanceTests
{
    [Fact]
    public void RepeatedViewAndPreviewDoNotResolveUnchangedChoiceRecordsAgain()
    {
        var plugin = new GenericJsonPlugin();
        var file = SampleSaves.Load("PPSA04027").AllDiskFiles.First(file => file.FileName == "settings.json");
        var references = new CountingContext(new ReferencePackStore([plugin]).Pin(plugin, "PPSA04027"));
        using var document = plugin.Open(file, references);
        _ = document.BuildView();
        var calls = references.FindCalls;
        Assert.True(calls > 0);
        _ = document.BuildView();
        _ = ((IActionPlanningDocument)document).PlanAction("semantic/defaults", new Dictionary<string, FieldValue>(), new() { RowKeys = [] });
        Assert.Equal(calls, references.FindCalls);
    }

    private sealed class CountingContext(IReferenceContext inner) : IReferenceContext
    {
        public int FindCalls { get; set; }
        public IReadOnlyList<ReferencePackInfo> Packs => inner.Packs;
        public IReferenceCatalog? Catalog(string catalogId, string? gameBuild = null) => inner.Catalog(catalogId, gameBuild) is { } catalog
            ? new CountingCatalog(catalog, this) : null;
        public AssetContent? GetAsset(string assetId) => inner.GetAsset(assetId);
        public ReferenceDatasetInfo? AssetProvenance(string assetId) => inner.AssetProvenance(assetId);
    }

    private sealed class CountingCatalog(IReferenceCatalog inner, CountingContext owner) : IReferenceCatalog
    {
        public string Id => inner.Id;
        public ReferencePackInfo Pack => inner.Pack;
        public ReferenceDatasetInfo Dataset => inner.Dataset;
        public int Count => inner.Count;
        public IReadOnlyList<string> Categories => inner.Categories;
        public IReadOnlyList<ReferenceSortKey> SortKeys => inner.SortKeys;
        public ReferenceRecord? Find(string key) { owner.FindCalls++; return inner.Find(key); }
        public ReferencePage Search(ReferenceQuery query) => inner.Search(query);
    }
}
