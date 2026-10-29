using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.AstroPlayroom.Tests;

public sealed class AstroModelPerformanceTests
{
    [Fact]
    public void RebuildingViewsAndMapsDoesNotRepeatPinnedWorldLookups()
    {
        var (plugin, _, context) = AstroCatalogueTestData.References();
        var references = new CountingAstroReferences(context);
        using var document = plugin.Open(AstroTestData.Samples()[0], references);
        _ = document.BuildView();
        _ = document.ExpandNode("astro-map:World:0");
        _ = document.ExpandNode("astro-map:Artifact:0");
        var initial = references.Finds;
        Assert.True(initial > 0);
        for (var i = 0; i < 3; i++)
        {
            _ = document.BuildView();
            _ = document.ExpandNode("astro-map:World:0");
            _ = document.ExpandNode("astro-map:Artifact:0");
        }
        Assert.Equal(initial, references.Finds);
    }

    [Fact]
    public void FullOwnershipPlanDoesNotAllocatePerFlagLookup()
    {
        using var document = AstroTestData.Open(AstroTestData.Bytes("\r\n<PreInstall><Artifact>" +
            string.Concat(Enumerable.Range(0, 164).Select(i => $"<item{i}><IsGot>false</IsGot></item{i}>")) +
            "</Artifact></PreInstall>"));
        var planner = Assert.IsAssignableFrom<IActionPlanningDocument>(document);
        var parameters = new Dictionary<string, FieldValue>();
        var scope = new ActionScope();
        var allocated = AstroPerformanceData.Allocated(() =>
            _ = planner.PlanAction("collectibles/collect-all", parameters, scope));
        Assert.InRange(allocated, 0, 48_000);
        Assert.Equal(164, planner.PlanAction("collectibles/collect-all", parameters, scope).Changes.Count);
    }

    [Theory]
    [InlineData("<IsGot>false</IsGot>", "Missing")]
    [InlineData("<IsGot>true</IsGot>", "Owned")]
    [InlineData("<IsGot>false</IsGot><IsGot>true</IsGot>", "Unknown")]
    [InlineData("<IsGot value='false'/>", "Unknown")]
    [InlineData("<IsGot value='false'>false</IsGot>", "Unknown")]
    [InlineData("<IsGot>false<child/></IsGot>", "Unknown")]
    [InlineData("<IsGot>0</IsGot>", "Unknown")]
    [InlineData("<IsGot>false<child/>true</IsGot>", "Unknown")]
    public void StructuralFlagResolutionPreservesAmbiguousShapes(string contents, string expected)
    {
        var bytes = AstroTestData.Bytes($"\r\n<PreInstall><Artifact><item>{contents}</item></Artifact></PreInstall>");
        using var document = AstroTestData.Open(bytes);
        var row = Assert.Single(Assert.IsAssignableFrom<IQueryableDocument>(document)
            .Query("astro.collectibles", new()).Rows);
        Assert.Equal(expected, Assert.IsType<TextNode>(row.Cells["status"]).Text);
        Assert.Equal(bytes, AstroTestData.Output(document));
    }
}
