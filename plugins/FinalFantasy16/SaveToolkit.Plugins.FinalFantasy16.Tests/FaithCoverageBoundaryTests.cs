using System.Buffers.Binary;
using SaveToolkit.Core.References;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Values;

namespace SaveToolkit.Plugins.FinalFantasy16.Tests;

public sealed class FaithCoverageBoundaryTests
{
    private const string Restore = "restore-imported-main-stacks";
    private readonly FinalFantasy16Plugin _plugin = new();
    private static readonly Dictionary<string, FieldValue> NoParameters = [];

    [Theory]
    [InlineData("set-gil", 1)]
    [InlineData("set-ap", 3)]
    [InlineData("set-renown", 2)]
    public void ResourceTargetsScopesAndForgedConsequencesAreValidated(string action, int id)
    {
        var original = FaithCoverageTests.Save(id, 1234, 101, 567);
        using var document = _plugin.Open(FaithTestData.File(original));
        var planner = (IActionPlanningDocument)document;
        foreach (var target in new[] { 0, FaithQuantityPlanning.MaximumTarget })
        {
            var parameters = Parameters(FieldValue.Of(target));
            var plan = planner.PlanAction(action, parameters, new());
            Assert.Empty(plan.Errors);
            Assert.Single(plan.Changes, change => !change.Derived);
            Assert.Equal(target, plan.Changes[0].NewValue.AsInt32());
            foreach (var forged in new[]
            {
                plan with { Warnings = [] },
                plan with { SkippedRows = [new("inventory/0", "Fake")] },
                plan with { Errors = [new("quantity", "Fake")] },
                plan with { Changes = [plan.Changes[0] with { Derived = true }, .. plan.Changes.Skip(1)] },
                plan with { Changes = [plan.Changes[0] with { OldExists = false }, .. plan.Changes.Skip(1)] },
                plan with { Changes = [plan.Changes[0] with { Label = "Fake" }, .. plan.Changes.Skip(1)] },
            }) Assert.False(planner.ApplyActionPlan(action, parameters, new(), forged).Succeeded);
            Assert.Equal(original, Assert.Single(document.Serialize()).Content.ToArray());
        }
        foreach (var value in new[] { FieldValue.Of(-1), FieldValue.Of(FaithQuantityPlanning.MaximumTarget + 1),
            FieldValue.Null, FieldValue.Of("1"), FieldValue.Of(false), FieldValue.Of(1.5), FieldValue.Of((Int128)int.MaxValue + 1) })
            Assert.NotEmpty(planner.PlanAction(action, Parameters(value), new()).Errors);
        Assert.NotEmpty(planner.PlanAction(action, NoParameters, new()).Errors);
        var valid = Parameters(FieldValue.Of(1));
        foreach (var keys in new[] { new[] { "inventory/1" }, new[] { "secondary/0" }, new[] { "inventory/0", "inventory/0" } })
            Assert.NotEmpty(planner.PlanAction(action, valid, new(keys)).Errors);
        Assert.Empty(planner.PlanAction(action, valid, new([])).Changes);
        var stale = planner.PlanAction(action, valid, new());
        Assert.True(document.ApplyEdits([new("inventory/0/quantity", FieldValue.Of(2))]).Succeeded);
        var changed = Assert.Single(document.Serialize()).Content.ToArray();
        Assert.False(planner.ApplyActionPlan(action, valid, new(), stale).Succeeded);
        Assert.Equal(changed, Assert.Single(document.Serialize()).Content.ToArray());
    }

    [Theory]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(8)]
    [InlineData(9)]
    [InlineData(10)]
    [InlineData(101)]
    [InlineData(102)]
    [InlineData(103)]
    [InlineData(104)]
    [InlineData(105)]
    [InlineData(106)]
    [InlineData(107)]
    [InlineData(108)]
    [InlineData(109)]
    public void EveryOrdinaryAllowlistedIdentityRestoresAtTheConservativeCeiling(int id)
    {
        var original = FaithCoverageTests.Save(id, FaithQuantityPlanning.MaximumTarget, 1, 567);
        using var document = _plugin.Open(FaithTestData.File(original));
        Assert.True(document.ApplyEdits([new("inventory/0/quantity", FieldValue.Of(1))]).Succeeded);
        var planner = (IActionPlanningDocument)document;
        var plan = planner.PlanAction(Restore, NoParameters, new());
        Assert.Empty(plan.Errors);
        Assert.Equal(FaithQuantityPlanning.MaximumTarget, Assert.Single(plan.Changes, change => !change.Derived).NewValue.AsInt32());
        Assert.Single(plan.SkippedRows); // Resource stays unchanged.
        Assert.True(planner.ApplyActionPlan(Restore, NoParameters, new(), plan).Succeeded);
        Assert.Equal(original, Assert.Single(document.Serialize()).Content.ToArray());
    }

    [Fact]
    public void SystemUnsupportedAndUnresolvedQueryScopesCannotPlanWrites()
    {
        var system = FaithCoverageTests.Save(1, 1234, 5, 567);
        BinaryPrimitives.WriteUInt64LittleEndian(system.AsSpan(FaithContainer.VersionOffset), FaithContainer.SystemVersion);
        var unsupported = FaithTestData.Container((FaithSchema.ItemType, FaithSchema.ItemVersion + 1, FaithTestData.Items()));
        var duplicate = FaithTestData.Container((FaithSchema.ItemType, FaithSchema.ItemVersion, FaithTestData.Items()),
            (FaithSchema.ItemType, FaithSchema.ItemVersion, FaithTestData.Items()));
        foreach (var bytes in new[] { system, unsupported, duplicate })
        {
            using var document = _plugin.Open(FaithTestData.File(bytes, name: ReferenceEquals(bytes, system) ? "system-save-data" : "game-save-data"));
            var planner = (IActionPlanningDocument)document;
            Assert.NotEmpty(planner.PlanAction("set-gil", Parameters(FieldValue.Of(1)), new()).Errors);
            Assert.NotEmpty(planner.PlanAction(Restore, NoParameters, new()).Errors);
            Assert.Equal(bytes, Assert.Single(document.Serialize()).Content.ToArray());
        }
        using var supported = _plugin.Open(FaithTestData.File(FaithCoverageTests.Save(1, 1234, 5, 567)));
        var unresolved = new ActionScope { QueryScope = new(FaithInventoryView.CollectionId, new(), 0) };
        var plans = (IActionPlanningDocument)supported;
        Assert.NotEmpty(plans.PlanAction("set-gil", Parameters(FieldValue.Of(1)), unresolved).Errors);
        Assert.NotEmpty(plans.PlanAction(Restore, NoParameters, unresolved).Errors);
    }

    [Fact]
    public void NegativeResourceAndChangedResourceIdentityAreExcluded()
    {
        using var negative = _plugin.Open(FaithTestData.File(FaithCoverageTests.Save(1, -1, 5, 567)));
        var plan = ((IActionPlanningDocument)negative).PlanAction("set-gil", Parameters(FieldValue.Of(1)), new());
        Assert.Empty(plan.Changes);
        Assert.Single(plan.SkippedRows);
        using var changed = _plugin.Open(FaithTestData.File(FaithCoverageTests.Save(5, 1234, 101, 567)));
        Assert.True(changed.ApplyEdits([new("inventory/0/item", FieldValue.Of("1"))]).Succeeded);
        Assert.NotEmpty(((IActionPlanningDocument)changed).PlanAction("set-gil", Parameters(FieldValue.Of(1)), new()).Errors);
    }

    [Theory]
    [InlineData("Different resource", "resource")]
    [InlineData("Gil", "quest-item")]
    public void UnqualifiedReferenceJoinsNeverAuthorizeResourceWrites(string name, string category)
    {
        var original = FaithCoverageTests.Save(1, 1234, 5, 567);
        var references = new AlteredReferences(new ReferencePackStore([_plugin]).Pin(_plugin, "PPSA10665"), name, category);
        using var document = _plugin.Open(FaithTestData.File(original), references);
        Assert.NotEmpty(((IActionPlanningDocument)document).PlanAction("set-gil", Parameters(FieldValue.Of(1)), new()).Errors);
        Assert.Equal(original, Assert.Single(document.Serialize()).Content.ToArray());
    }

    private static Dictionary<string, FieldValue> Parameters(FieldValue target) => new() { ["quantity"] = target };

    private sealed class AlteredReferences(IReferenceContext inner, string name, string category) : IReferenceContext
    {
        public IReadOnlyList<ReferencePackInfo> Packs => inner.Packs;
        public AssetContent? GetAsset(string assetId) => inner.GetAsset(assetId);
        public ReferenceDatasetInfo? AssetProvenance(string assetId) => inner.AssetProvenance(assetId);
        public IReferenceCatalog? Catalog(string catalogId, string? gameBuild = null) => inner.Catalog(catalogId, gameBuild) is { } catalog
            ? catalogId == FaithCatalog.ItemsId ? new AlteredCatalog(catalog, name, category) : catalog : null;
    }

    private sealed class AlteredCatalog(IReferenceCatalog inner, string name, string category) : IReferenceCatalog
    {
        public string Id => inner.Id;
        public ReferencePackInfo Pack => inner.Pack;
        public ReferenceDatasetInfo Dataset => inner.Dataset;
        public int Count => inner.Count;
        public IReadOnlyList<string> Categories => inner.Categories;
        public IReadOnlyList<ReferenceSortKey> SortKeys => inner.SortKeys;
        public ReferencePage Search(ReferenceQuery query) => inner.Search(query);
        public ReferenceRecord? Find(string key) => key == "1" ? inner.Find(key)! with { Name = name, Category = category } : inner.Find(key);
    }
}
