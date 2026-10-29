using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;

namespace SaveToolkit.Plugins.AstroPlayroom.Tests;

public sealed class AstroPlanningPerformanceTests
{
    private const string Collect = "collectibles/collect-all";
    private const string WorldAction = "collectibles/collect-world";
    private const string World = "World/MEMORY_HEAVEN";
    private const string Artifact = "/PreInstall[0]/Artifact[0]/";

    [Fact]
    public void SingleRowPreviewDoesNotAllocateACompleteCollectionKeySet()
    {
        using var document = AstroTestData.Open(AstroTestData.Bytes("\r\n<PreInstall><Artifact>" +
            string.Concat(Enumerable.Range(0, 1500).Select(i => $"<item{i}><IsGot>false</IsGot></item{i}>")) +
            "</Artifact></PreInstall>"));
        var planner = Assert.IsAssignableFrom<IActionPlanningDocument>(document);
        var parameters = new Dictionary<string, FieldValue>();
        var scope = new ActionScope([Artifact + "item1499[0]"]);
        var allocated = AstroPerformanceData.Allocated(() => _ = planner.PlanAction(Collect, parameters, scope));
        Assert.InRange(allocated, 0, 4_000);
        Assert.Equal(Artifact + "item1499[0]/IsGot[0]/text[0]",
            Assert.Single(planner.PlanAction(Collect, parameters, scope).Changes).ValueId);
    }

    [Fact]
    public void WorldPreviewDoesNotConstructAndDiscardAnAllRecordsPreview()
    {
        var (plugin, _, context) = AstroCatalogueTestData.References();
        var file = AstroTestData.Samples()[0];
        using var document = plugin.Open(file, context);
        var planner = Assert.IsAssignableFrom<IActionPlanningDocument>(document);
        var parameters = new Dictionary<string, FieldValue> { ["world"] = FieldValue.Of(World) };
        var scope = new ActionScope();
        var allocated = AstroPerformanceData.Allocated(() => _ = planner.PlanAction(WorldAction, parameters, scope));
        Assert.InRange(allocated, 0, 30_000);
        var plan = planner.PlanAction(WorldAction, parameters, scope);
        Assert.Empty(plan.Errors);
        Assert.Equal(25, plan.Changes.Count);
        Assert.Equal(139, plan.SkippedRows.Count);
        Assert.Equal(file.ReadAllBytes(), AstroTestData.Output(document));
    }

    [Fact]
    public void IndexedSelectionsKeepNativeOrderingAndRejectUnknownDuplicateKeys()
    {
        const string root = "\r\n<PreInstall><Artifact><same><IsGot>false</IsGot></same>" +
            "<other/><same><IsGot>false</IsGot></same></Artifact></PreInstall>";
        using var document = AstroTestData.Open(AstroTestData.Bytes(root));
        var planner = Assert.IsAssignableFrom<IActionPlanningDocument>(document);
        var parameters = new Dictionary<string, FieldValue>();
        var scope = new ActionScope([Artifact + "same[1]", Artifact + "other[0]", Artifact + "same[0]"]);
        var plan = planner.PlanAction(Collect, parameters, scope);
        Assert.Equal(new[] { Artifact + "same[0]/IsGot[0]/text[0]", Artifact + "same[1]/IsGot[0]/text[0]" },
            plan.Changes.Select(change => change.ValueId));
        Assert.Equal(Artifact + "other[0]", Assert.Single(plan.SkippedRows).RowKey);
        foreach (var keys in new[]
        {
            new[] { Artifact + "same[0]", Artifact + "same[0]" },
            new[] { Artifact + "same[0]", "foreign" },
            new[] { Artifact + "same[0]", null! }
        })
        {
            var invalid = planner.PlanAction(Collect, parameters, new ActionScope(keys));
            Assert.Equal(new FieldError(Collect, "Selection must contain distinct existing collectible row keys only."),
                Assert.Single(invalid.Errors));
            Assert.Empty(invalid.Changes);
        }
        Assert.Empty(planner.PlanAction(Collect, parameters, new ActionScope([])).Changes);
        Assert.Equal(AstroTestData.Bytes(root), AstroTestData.Output(document));
    }

    [Fact]
    public void WorldScopeValidationKeepsErrorPrecedenceAndNativeSkipOrdering()
    {
        var (plugin, _, context) = AstroCatalogueTestData.References();
        using var document = plugin.Open(AstroTestData.Samples()[0], context);
        var planner = Assert.IsAssignableFrom<IActionPlanningDocument>(document);
        var parameters = new Dictionary<string, FieldValue> { ["world"] = FieldValue.Of(World) };
        var all = planner.PlanAction(WorldAction, parameters, new());
        var missing = all.Changes.Take(2).Select(change => change.ValueId[..change.ValueId.LastIndexOf("/IsGot", StringComparison.Ordinal)]).ToArray();
        var outside = all.SkippedRows.Take(2).Select(row => row.RowKey).ToArray();
        var scope = new ActionScope([outside[1], missing[1], outside[0], missing[0]]);
        var selected = planner.PlanAction(WorldAction, parameters, scope);
        Assert.Equal(all.Changes.Take(2), selected.Changes);
        Assert.Equal(all.SkippedRows.Take(2), selected.SkippedRows);
        foreach (var keys in new[] { new[] { missing[0], missing[0] }, new[] { missing[0], "foreign" } })
            Assert.Equal(new FieldError(Collect, "Selection must contain distinct existing collectible row keys only."),
                Assert.Single(planner.PlanAction(WorldAction, parameters, new ActionScope(keys)).Errors));
        parameters["world"] = FieldValue.Of("World/unknown");
        Assert.Equal("world", Assert.Single(planner.PlanAction(WorldAction, parameters, new ActionScope(["foreign"])).Errors).ValueId);
    }
}
