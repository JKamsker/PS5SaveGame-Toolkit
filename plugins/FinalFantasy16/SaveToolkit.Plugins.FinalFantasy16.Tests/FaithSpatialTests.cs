using System.Buffers.Binary;
using System.Text.Json;
using SaveToolkit.Core.References;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.FinalFantasy16.Tests;

public sealed class FaithSpatialTests
{
    private readonly FinalFantasy16Plugin _plugin = new();

    [Theory]
    [InlineData("PPSA10665")]
    [InlineData("PPSA13803")]
    public void SpoilerTreeIsLazyAttributedUnknownAndNavigatesToReadOnlyReferenceFields(string title)
    {
        var file = FaithWriteTests.SampleFile(title);
        var references = new ReferencePackStore([_plugin]).Pin(_plugin, title);
        using var document = _plugin.Open(file, references);
        using var session = new DocumentSession("ff16-spatial", file, _plugin, document, references: references);
        var root = Assert.IsType<TabsNode>(session.BuildView());
        var tab = root.Tabs.Single(tab => tab.Title == FaithAbilityView.TabTitle);
        var lazy = Assert.Single(tab.Children.OfType<SectionNode>());
        Assert.Equal(FaithAbilityView.LazyId, lazy.LazyId);
        Assert.Empty(lazy.Children); // Names are not exposed until the spoiler section is expanded.
        var expanded = session.Expand(FaithAbilityView.LazyId);
        var graph = Assert.Single(expanded.OfType<GraphNode>());
        SpatialViewValidation.Validate(graph);
        Assert.Equal(10, graph.Items.Select(item => item.Branch).Distinct().Count());
        var fields = expanded.SelectMany(FaithViewFields.All).ToDictionary(field => field.ValueId);
        Assert.Equal(48, fields.Count);
        Assert.All(graph.Items, item =>
        {
            Assert.Equal(GraphState.Unknown, item.State);
            Assert.Contains("CC-BY-SA-3.0", item.Description);
            Assert.Contains("oldid=", item.Description);
            Assert.Contains("Unknown", graph.TextAlternative);
            var target = Assert.IsType<ViewTarget>(item.Target);
            Assert.Equal(ViewTargetKind.Field, target.Kind);
            var field = fields[target.Id];
            Assert.True(field.Spec.ReadOnly);
            Assert.Equal(FaithCatalog.AbilitiesId, Assert.IsType<ReferenceSpec>(field.Spec).CatalogId);
            Assert.Equal(Confidence.Inferred, field.Confidence);
            Assert.Equal(field.Value, document.GetValue(field.ValueId));
            Assert.Equal([$"tab:{FaithAbilityView.TabTitle}", $"section:{FaithAbilityView.LazyId}", $"row:{target.Id}"], target.Path);
        });
        Assert.All(graph.Edges, edge => Assert.Equal(edge.From[..edge.From.LastIndexOf('/')], edge.To[..edge.To.LastIndexOf('/')]));
        Assert.Equal("600 AP (this stage)", graph.Items.Single(item => item.Id == "ability/ice-age/upgrade").Cost);
        Assert.Equal("2000 AP (this stage)", graph.Items.Single(item => item.Id == "ability/limit-break/master").Cost);
        Assert.All(graph.Items.Where(item => item.Branch == "Leviathan"), item => Assert.Equal("AP cost Unknown", item.Cost));
        Assert.False(document.ApplyEdits([new("abilities/ice-age", FieldValue.Of("rising-flames"))]).Succeeded);
        Assert.Equal(file.ReadAllBytes(), Assert.Single(document.Serialize()).Content.ToArray());
        Assert.False(session.State.CanUndo);
        Assert.False(session.State.IsDirty);
    }

    [Fact]
    public void HostAndFallbackTreeAgreeAndNeverJoinNexOrSavedSkillIndices()
    {
        var skills = new byte[FaithSchema.SkillLength];
        skills[39] = 3;
        var file = FaithTestData.File(FaithTestData.Container((FaithSchema.SkillType, FaithSchema.SkillVersion, skills)));
        var references = new ReferencePackStore([_plugin]).Pin(_plugin, "PPSA10665");
        using var host = _plugin.Open(file, references);
        using var fallback = _plugin.Open(file);
        _ = host.BuildView();
        _ = fallback.BuildView();
        var hostGraph = Assert.Single(host.ExpandNode(FaithAbilityView.LazyId).OfType<GraphNode>());
        var fallbackGraph = Assert.Single(fallback.ExpandNode(FaithAbilityView.LazyId).OfType<GraphNode>());
        Assert.Equal(JsonSerializer.Serialize(hostGraph), JsonSerializer.Serialize(fallbackGraph));
        Assert.DoesNotContain(hostGraph.Items, item => item.Target!.Id.StartsWith("skills/", StringComparison.Ordinal));
        Assert.All(hostGraph.Items, item => Assert.Equal(GraphState.Unknown, item.State));
        Assert.True(host.ApplyEdits([new("skills/ranks/39", FieldValue.Of(0))]).Succeeded);
        var changedGraph = Assert.Single(host.ExpandNode(FaithAbilityView.LazyId).OfType<GraphNode>());
        Assert.Equal(JsonSerializer.Serialize(hostGraph), JsonSerializer.Serialize(changedGraph));
        Assert.Equal("Unknown", references.Catalog(FaithCatalog.AbilitiesId)!.Find("wings-of-light")!.Attributes["saveIndexMapping"]);
        Assert.Equal("Wings of Light", references.Catalog(FaithCatalog.SkillsId)!.Find("39")!.Name);
    }

    [Theory]
    [InlineData("PPSA10665", "Hideaway 2")]
    [InlineData("PPSA13803", "Phoenix Gate")]
    public void LocationMapLinksToStoredFieldWithoutInventingCoordinatesOrCompletion(string title, string name)
    {
        var file = FaithSamples.Snapshot(title);
        using var document = _plugin.Open(file);
        var maps = Nodes(document.BuildView()).OfType<MapNode>();
        var map = Assert.Single(maps);
        SpatialViewValidation.Validate(map);
        var marker = Assert.Single(map.Markers);
        Assert.Contains(name, marker.Label);
        Assert.Equal(MapMarkerStatus.Unknown, marker.Status);
        Assert.Equal(Confidence.Inferred, marker.Confidence);
        Assert.Contains("MIT", marker.Description);
        Assert.Null(marker.X);
        Assert.Null(marker.Y);
        Assert.Null(map.Background);
        Assert.False(map.CoordinateEmbeddingPermitted);
        Assert.Equal(FaithLocationView.GuideUrl, map.ExternalSource!.Url);
        Assert.Equal("party/map", marker.Target!.Id);
        Assert.Contains(FaithViewFields.All(document.BuildView()), field => field.ValueId == marker.Target.Id);
        Assert.Equal(file.ReadAllBytes(), Assert.Single(document.Serialize()).Content.ToArray());
    }

    [Fact]
    public void MissingDuplicateAndUnsupportedPartySchemasDoNotProduceMapsAndUnknownIdsStayUnknown()
    {
        var party = new byte[FaithSchema.PartyLength];
        BinaryPrimitives.WriteInt32LittleEndian(party.AsSpan(FaithContextLayout.MapOffset), int.MaxValue);
        using var unknown = _plugin.Open(FaithTestData.File(FaithTestData.Container((FaithSchema.PartyType, FaithSchema.PartyVersion, party))));
        var marker = Assert.Single(Assert.Single(Nodes(unknown.BuildView()).OfType<MapNode>()).Markers);
        Assert.Equal("Unknown map 2147483647", marker.Label);
        Assert.Equal(Confidence.Unknown, marker.Confidence);
        foreach (var bytes in new[] { FaithTestData.Container((99, 1, new byte[8])),
            FaithTestData.Container((FaithSchema.PartyType, FaithSchema.PartyVersion + 1, party)),
            FaithTestData.Container((FaithSchema.PartyType, FaithSchema.PartyVersion, party),
                (FaithSchema.PartyType, FaithSchema.PartyVersion, party)) })
        {
            using var document = _plugin.Open(FaithTestData.File(bytes));
            Assert.Empty(Nodes(document.BuildView()).OfType<MapNode>());
            Assert.Equal(bytes, Assert.Single(document.Serialize()).Content.ToArray());
        }
    }

    private static IEnumerable<ViewNode> Nodes(ViewNode root) => new[] { root }.Concat(root switch
    {
        TabsNode tabs => tabs.Tabs.SelectMany(tab => tab.Children).SelectMany(Nodes),
        SectionNode section => section.Children.SelectMany(Nodes),
        TableNode table => table.Rows.SelectMany(row => row.Cells.Values).SelectMany(Nodes),
        _ => [],
    });
}
