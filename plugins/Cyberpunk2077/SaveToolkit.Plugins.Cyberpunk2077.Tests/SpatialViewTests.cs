using System.Text.Json;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.Cyberpunk2077.Tests;

public sealed class SpatialViewTests
{
    [Fact]
    public void StoredPerksUseBoundedStableIdentifiersAndNeverInventPrerequisitesOrEligibility()
    {
        var rows = Enumerable.Range(0, 1100).Select(i => Perk("CoolAttributeData", $"Cool_Perk_{i:D4}", i % 4)).ToArray();
        var graph = new PerkGraphInspection(rows, null);
        var reverse = new PerkGraphInspection(rows.Reverse().ToArray(), null);
        var fields = new Dictionary<string, FieldNode>();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var pages = graph.Build().OfType<SectionNode>().ToArray();
        Assert.Equal(9, pages.Length);
        foreach (var page in pages)
        {
            var expanded = graph.Expand(page.LazyId!, Read);
            var node = Assert.IsType<GraphNode>(expanded[0]);
            SpatialViewValidation.Validate(node);
            Assert.InRange(node.Items.Count, 1, 128);
            Assert.Empty(node.Edges);
            Assert.Contains("No prerequisite edges", node.TextAlternative);
            Assert.Equal(JsonSerializer.Serialize(expanded), JsonSerializer.Serialize(reverse.Expand(page.LazyId!, Read)));
            foreach (var item in node.Items)
            {
                Assert.True(ids.Add(item.Id));
                Assert.Equal(GraphState.Unknown, item.State);
                Assert.Contains("cost unknown", item.Cost);
                Assert.Equal("CoolAttributeData", item.Branch);
                Assert.Equal(["tab:Skills and perks", "section:" + page.LazyId, "section:Stored perk details"], item.Target!.Path);
                Assert.Contains("rank:", fields[item.Target.Id].Value.AsString());
                Assert.True(fields[item.Target.Id].Spec.ReadOnly);
            }
        }
        Assert.Equal(rows.Length, ids.Count);

        FieldNode Read(string id, string label, FieldSpec spec, string value) => fields[id] = new(id, label, spec, FieldValue.Of(value));
    }

    [Fact]
    public void DuplicatePerkIdentityOmitsOnlyTheGraphAndEmptyOrUnavailableDataDoesNotBecomeLocked()
    {
        var row = Perk("BodyAttributeData", "Body_Central_Milestone_1", 0);
        var duplicate = new PerkGraphInspection([row, row], null);
        Assert.Contains(duplicate.Build(), node => node is TextNode text && text.Text.Contains("Ambiguous duplicate"));
        Assert.Empty(duplicate.Build().OfType<SectionNode>());
        Assert.Empty(new PerkGraphInspection([], "Unverified schema").Build().OfType<SectionNode>());
        Assert.Empty(new PerkGraphInspection([], null).Build().OfType<SectionNode>());
        var longName = new string('x', 254);
        var bounded = new PerkGraphInspection([Perk(longName, longName, -1)], null);
        var item = Assert.Single(Assert.IsType<GraphNode>(bounded.Expand("perk-graph/0", Read)[0]).Items);
        Assert.InRange(item.Id.Length, 1, SpatialViewLimits.MaxIdLength);
        Assert.Equal(GraphState.Unknown, item.State);
        Assert.Contains("-1", item.Cost); // Diagnostic preservation, not a claim of a valid rank.
    }

    [Theory]
    [InlineData("perk-graph/-1")]
    [InlineData("perk-graph/2147483647")]
    [InlineData("perk-graph/0/extra")]
    [InlineData("perk-graph/1")]
    public void InvalidPerkPagesAreRejectedBeforeOffsetArithmetic(string id) =>
        Assert.Throws<KeyNotFoundException>(() => new PerkGraphInspection([Perk("Body", "perk", 1)], null).Expand(id, Read));

    [Fact]
    public void TravelMapIsPagedLinkOnlyAttributedAndDistinguishesRegistrationFromUnknown()
    {
        var rows = Enumerable.Range(0, 1100).Select(i => Travel((ulong)i,
            i == 1000 ? WorldInspection.RegisteredTravelState : "Not registered in saved FastTravelSystem")).ToArray();
        var map = new TravelMapInspection(rows);
        var markers = new List<MapMarker>();
        foreach (var page in map.Build().Cast<SectionNode>())
        {
            var expanded = map.Expand(page.LazyId!, Read);
            var node = Assert.IsType<MapNode>(expanded[0]);
            SpatialViewValidation.Validate(node);
            Assert.InRange(node.Markers.Count, 1, 128);
            Assert.Null(node.Background);
            Assert.False(node.CoordinateEmbeddingPermitted);
            Assert.Null(node.CoordinateAttribution);
            Assert.Equal(TravelMapInspection.SourceUrl, node.ExternalSource!.Url);
            Assert.Contains("used with attribution per owner decision 2026-10-29", node.ExternalSource.Attribution);
            Assert.Contains("Found means registered", node.TextAlternative);
            var fields = Assert.IsType<SectionNode>(expanded[1]).Children.Cast<FieldNode>().ToDictionary(field => field.ValueId);
            foreach (var marker in node.Markers)
            {
                Assert.Null(marker.X);
                Assert.Null(marker.Y);
                Assert.NotNull(marker.Target);
                Assert.True(fields[marker.Target.Id].Spec.ReadOnly);
                Assert.Contains("MIT", marker.Description);
                Assert.Equal(["tab:World", "section:" + page.LazyId, "section:Travel marker details"], marker.Target.Path);
            }
            markers.AddRange(node.Markers);
        }
        Assert.Equal(1100, markers.Select(marker => marker.Id).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal("travel/1000", Assert.Single(markers, marker => marker.Status == MapMarkerStatus.Found).Id);
        Assert.DoesNotContain(markers, marker => marker.Status == MapMarkerStatus.Missing);
        var unavailable = Assert.Single(Assert.IsType<MapNode>(new TravelMapInspection(
            [Travel(1, "Unknown: travel unavailable")]).Expand("travel-map/0", Read)[0]).Markers);
        Assert.Equal(MapMarkerStatus.Unknown, unavailable.Status);
        Assert.Contains("travel unavailable", unavailable.Description);
        Assert.Throws<KeyNotFoundException>(() => map.Expand("travel-map/2147483647", Read));
        Assert.Throws<KeyNotFoundException>(() => map.Expand("travel-map/-1", Read));
    }

    [Fact]
    public void OversizedSpatialProvenanceIsBoundedWithoutLosingTheLinkedDetail()
    {
        var attribution = new string('a', SpatialViewLimits.MaxTextLength * 2);
        var row = Travel(1, "Unknown") with { Attribution = attribution };
        var expanded = new TravelMapInspection([row]).Expand("travel-map/0", Read);
        var map = Assert.IsType<MapNode>(expanded[0]);
        SpatialViewValidation.Validate(map);
        Assert.Contains("full provenance", Assert.Single(map.Markers).Description);
        Assert.Equal(attribution, Assert.IsType<FieldNode>(Assert.Single(Assert.IsType<SectionNode>(expanded[1]).Children)).Description);
    }

    private static FieldNode Read(string id, string label, FieldSpec spec, string value) => new(id, label, spec, FieldValue.Of(value));
    private static InspectionRow Perk(string group, string name, int rank) => new("original-row-index-is-not-spatial-identity",
        new Dictionary<string, string> { ["kind"] = "Modern perk", ["group"] = group, ["name"] = name,
            ["rank"] = rank.ToString(System.Globalization.CultureInfo.InvariantCulture), ["status"] = "Stored rank only" });
    private static InspectionRow Travel(ulong id, string state) => new("travel/" + id, new Dictionary<string, string>
        { ["name"] = "FastTravelPoints.synthetic_" + id, ["id"] = $"0x{id:X16}", ["state"] = state, ["details"] = "Registration is not accessibility" }, "Synthetic MIT attribution");
}
