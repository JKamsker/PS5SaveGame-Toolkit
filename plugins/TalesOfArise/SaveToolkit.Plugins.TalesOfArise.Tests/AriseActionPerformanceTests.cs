using System.Buffers.Binary;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.TalesOfArise.Tests;

[Collection(typeof(AllocationTestCollection))]
public sealed class AriseActionPerformanceTests
{
    private const int SlotCount = 934;
    private const int KiB = 1024;
    private static readonly IReadOnlyDictionary<string, FieldValue> Parameters =
        new Dictionary<string, FieldValue> { ["quantity"] = FieldValue.Of(12L) };

    [Fact]
    public void ApplyingAnUnloadedSelectionNeverRendersInventoryRowsOrLooksUpReferenceRecords()
    {
        var (document, references) = Fixture();
        using (document)
        {
            var scope = new ActionScope { RowKeys = ["inventory/933/quantity"] };
            var plan = document.PlanAction(AriseInventoryActions.SetQuantityAction, Parameters, scope);
            Assert.Empty(plan.Errors);
            Assert.Single(plan.Changes);
            Assert.Equal(0, references.FindCalls);
            var result = document.ApplyActionPlan(AriseInventoryActions.SetQuantityAction, Parameters, scope, plan);
            Assert.True(result.Succeeded);
            Assert.Equal(0, references.FindCalls);
            Assert.Equal(12L, document.GetValue("inventory/933/quantity").AsInt64());
            Assert.Throws<KeyNotFoundException>(() => document.GetValue("inventory/0/quantity"));
            var change = Assert.Single(result.Changes);
            Assert.True(document.ApplyEdits([new(change.ValueId, change.OldValue)]).Succeeded);
            Assert.Equal(1L, document.GetValue("inventory/933/quantity").AsInt64());
        }
    }

    [Fact]
    public void ApplyingOneRowInAFilledInventoryHasBoundedAllocations()
    {
        var allocated = ApplyAndMeasure();
        allocated.AssertLessThan(512 * KiB, $"One-row apply for {SlotCount} occupied slots.");
    }

    [Fact]
    public void DocumentPlanningDoesNotRenderRowsAndHasBoundedAllocations()
    {
        var (document, references) = Fixture();
        using (document)
        {
            _ = document.PlanAction(AriseInventoryActions.SetQuantityAction, Parameters, new());
            var allocated = AllocationProbe.Measure(() => document.PlanAction(AriseInventoryActions.SetQuantityAction, Parameters, new()), out var plan);
            Assert.Empty(plan.Errors);
            Assert.Equal(SlotCount, plan.Changes.Count);
            Assert.Equal(0, references.FindCalls);
            allocated.AssertLessThan(1024 * KiB, "Full-inventory planning.");
        }
    }

    private static AllocationProbeResult ApplyAndMeasure()
    {
        AriseDocument document = null!;
        var scope = new ActionScope { RowKeys = ["inventory/933/quantity"] };
        ActionPlan plan = null!;
        var allocated = AllocationProbe.Measure(() => document.ApplyActionPlan(
            AriseInventoryActions.SetQuantityAction, Parameters, scope, plan), out var result, beforeRun: () =>
        {
            (document, _) = Fixture();
            plan = document.PlanAction(AriseInventoryActions.SetQuantityAction, Parameters, scope);
        }, afterRun: () => document?.Dispose());
        Assert.True(result.Succeeded);
        return allocated;
    }

    private static (AriseDocument Document, CountingContext References) Fixture()
    {
        var fixture = new AriseTestContainer();
        var inventory = fixture.Body("SaveData_ItemManager");
        for (var index = 0; index < SlotCount; index++)
        {
            var offset = inventory + index * AriseTestContainer.ItemRecordSize;
            fixture.Payload[offset] = 1;
            BinaryPrimitives.WriteUInt16LittleEndian(fixture.Payload.AsSpan(offset + 2), 1);
        }

        var plugin = new TalesOfArisePlugin();
        var references = new CountingContext(AriseTestReferences.CreateContext(plugin));
        var document = new AriseDocument("generated", AriseTestContainer.FileName, new([], fixture.Payload, fixture.Sections), references);
        _ = document.ExpandNode("inventory");
        return (document, references);
    }

    private sealed class CountingContext(IReferenceContext inner) : IReferenceContext
    {
        public int FindCalls { get; private set; }
        public IReadOnlyList<ReferencePackInfo> Packs => inner.Packs;
        public IReferenceCatalog? Catalog(string catalogId, string? gameBuild = null) =>
            inner.Catalog(catalogId, gameBuild) is { } catalog ? new CountingCatalog(catalog, () => FindCalls++) : null;
        public AssetContent? GetAsset(string assetId) => inner.GetAsset(assetId);
        public ReferenceDatasetInfo? AssetProvenance(string assetId) => inner.AssetProvenance(assetId);
    }

    private sealed class CountingCatalog(IReferenceCatalog inner, Action onFind) : IReferenceCatalog
    {
        public string Id => inner.Id;
        public ReferencePackInfo Pack => inner.Pack;
        public ReferenceDatasetInfo Dataset => inner.Dataset;
        public int Count => inner.Count;
        public IReadOnlyList<string> Categories => inner.Categories;
        public IReadOnlyList<ReferenceSortKey> SortKeys => inner.SortKeys;
        public ReferencePage Search(ReferenceQuery query) => inner.Search(query);
        public ReferenceRecord? Find(string key)
        {
            onFind();
            return inner.Find(key);
        }
    }
}
